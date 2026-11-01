using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown;

// BFB owns the engine and task cancellation. This client only waits for its own GID.
internal sealed class BBDownAria2Rpc
{
    private static int sequence;
    private readonly Uri endpoint;
    private readonly string secret;
    private readonly string gidPrefix;
    private readonly Dictionary<string, string> options = new();
    private static readonly HttpClient client = new(new HttpClientHandler { UseProxy = false })
    { Timeout = TimeSpan.FromSeconds(3) };

    public BBDownAria2Rpc(string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;
        var address = root.GetProperty("endpoint").GetString();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var parsed) || parsed.Scheme != "http"
            || parsed.Host != "127.0.0.1" || parsed.AbsolutePath != "/jsonrpc"
            || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
            throw new InvalidDataException("Invalid private aria2 endpoint");
        endpoint = parsed;
        secret = root.GetProperty("secret").GetString() ?? "";
        gidPrefix = root.GetProperty("gidPrefix").GetString() ?? "";
        if (!Regex.IsMatch(secret, "^[a-f0-9]{64}$") || !Regex.IsMatch(gidPrefix, "^[a-f0-9]{12}$"))
            throw new InvalidDataException("Invalid private aria2 task identity");
        foreach (var entry in root.GetProperty("options").EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid private aria2 option");
            options.Add(entry.Name, entry.Value.GetString()!);
        }
    }

    private async Task<JsonElement> CallAsync(string method, params JsonNode?[] arguments)
    {
        var parameters = new JsonArray((JsonNode?)JsonValue.Create("token:" + secret));
        foreach (var value in arguments) parameters.Add(value);
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "bbdown", ["method"] = "aria2." + method, ["params"] = parameters };
        using var body = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, body);
        if (!response.IsSuccessStatusCode) throw new IOException("Private aria2 control request failed");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (document.RootElement.TryGetProperty("error", out _) || !document.RootElement.TryGetProperty("result", out var result))
            throw new IOException("Private aria2 control response failed");
        return result.Clone();
    }

    public async Task DownloadAsync(string url, string path, Action<long, long> progress)
    {
        var number = Interlocked.Increment(ref sequence);
        if (number > ushort.MaxValue) throw new IOException("Too many transfers in this BBDown invocation");
        var gid = gidPrefix + number.ToString("x4");
        var transferOptions = new JsonObject();
        foreach (var entry in options) transferOptions[entry.Key] = entry.Value;
        transferOptions["gid"] = gid;
        transferOptions["dir"] = Path.GetDirectoryName(Path.GetFullPath(path));
        transferOptions["out"] = Path.GetFileName(path);
        var headers = new JsonArray((JsonNode?)JsonValue.Create("User-Agent: Mozilla/5.0"), (JsonNode?)JsonValue.Create("Cookie: " + Core.Config.COOKIE));
        if (Core.MediaRequestHeaderPolicy.SendReferer(url)) headers.Add((JsonNode?)JsonValue.Create("Referer: https://www.bilibili.com"));
        transferOptions["header"] = headers;
        var added = false;
        var completed = false;
        try
        {
            var result = await CallAsync("addUri", new JsonArray((JsonNode?)JsonValue.Create(url)), transferOptions);
            if (result.ValueKind != JsonValueKind.String || result.GetString() != gid) throw new IOException("Invalid aria2 transfer identity");
            added = true;
            while (true)
            {
                var state = await CallAsync("tellStatus", JsonValue.Create(gid), new JsonArray("status", "completedLength", "totalLength", "errorCode"));
                var status = state.GetProperty("status").GetString();
                if (!long.TryParse(state.GetProperty("completedLength").GetString(), out var downloaded) || downloaded < 0
                    || !long.TryParse(state.GetProperty("totalLength").GetString(), out var total) || total < 0)
                    throw new IOException("Invalid aria2 transfer progress");
                if (total > 0) progress(downloaded, total);
                if (status == "complete")
                {
                    if (downloaded != total) throw new IOException("Incomplete aria2 transfer");
                    completed = true;
                    return;
                }
                if (status == "error" || status == "removed")
                {
                    var code = state.TryGetProperty("errorCode", out var errorCode) && int.TryParse(errorCode.GetString(), out var parsedCode) ? parsedCode : -1;
                    throw new IOException($"aria2 transfer failed (code {code})");
                }
                if (status != "active" && status != "waiting" && status != "paused") throw new IOException("Invalid aria2 transfer state");
                await Task.Delay(500);
            }
        }
        finally
        {
            // The application also cancels this task's GIDs after a hard process stop.
            if (added && !completed)
            {
                try { await CallAsync("forceRemove", JsonValue.Create(gid)); }
                catch { /* The owning BFB process handles engine failure and cancellation. */ }
            }
        }
    }
}
