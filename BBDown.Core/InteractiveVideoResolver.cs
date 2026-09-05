using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core;

public sealed class InteractiveVideoException(string code) : Exception($"BFB_SIGNAL:{code}")
{
    public string Code { get; } = code;
}

public sealed class InteractiveVideoResolver
{
    private readonly Func<string, CancellationToken, Task<string>> request;
    private readonly int maxNodes;
    private readonly int maxChoices;

    public InteractiveVideoResolver(Func<string, CancellationToken, Task<string>>? request = null,
        int maxNodes = 500, int maxChoices = 5000)
    {
        this.request = request ?? RequestAsync;
        this.maxNodes = maxNodes;
        this.maxChoices = maxChoices;
    }

    // This digest binds page indexes to CIDs, not to downloaded media contents.
    public static string PageSetHash(IEnumerable<Page> pages) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", pages.Select(p => p.cid))))).ToLowerInvariant();

    public async Task<List<Page>> ResolveAsync(Page root, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(90));
        var token = budget.Token;
        try
        {
            var playerQuery = $"aid={root.aid}&cid={root.cid}&wts={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            JsonElement player;
            if (!string.IsNullOrEmpty(Config.WBI))
            {
                try { player = await DataAsync("/x/player/wbi/v2?" + Parser.WbiSign(playerQuery), token); }
                catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                { player = await DataAsync($"/x/player/v2?aid={root.aid}&cid={root.cid}", token); }
            }
            else player = await DataAsync($"/x/player/v2?aid={root.aid}&cid={root.cid}", token);
            var version = PositiveId(player.GetProperty("interaction").GetProperty("graph_version"));
            var pending = new Queue<(long Cid, long Edge)>();
            var seen = new HashSet<(long Cid, long Edge)>();
            var media = new Dictionary<long, Page>();
            var rootCid = long.Parse(root.cid);
            pending.Enqueue((rootCid, 0));
            seen.Add((rootCid, 0));
            var choicesRead = 0;
            while (pending.TryDequeue(out var state))
            {
                token.ThrowIfCancellationRequested();
                var query = $"bvid={root.bvid}&edge_id={state.Edge}&graph_version={version}&wts={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var data = await DataAsync("/x/stein/edgeinfo_v2?" +
                    (string.IsNullOrEmpty(Config.WBI) ? query : Parser.WbiSign(query)), token);
                // Missing edge metadata must never be interpreted as a terminal node.
                var returnedEdge = PositiveId(data.GetProperty("edge_id"));
                if (state.Edge != 0 && returnedEdge != state.Edge) throw new JsonException();
                var title = data.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                if (!media.ContainsKey(state.Cid))
                    media.Add(state.Cid, new Page(media.Count + 1, root.aid, state.Cid.ToString(), "",
                        string.IsNullOrWhiteSpace(title) ? $"CID {state.Cid}" : title,
                        state.Cid == rootCid ? root.dur : 0, state.Cid == rootCid ? root.res : "",
                        root.pubTime, "", "", root.ownerName ?? "", root.ownerMid ?? ""));
                var edges = data.GetProperty("edges");
                if (edges.ValueKind != JsonValueKind.Object) throw new JsonException();
                if (!edges.TryGetProperty("questions", out var questions)) continue;
                if (questions.ValueKind != JsonValueKind.Array) throw new JsonException();
                foreach (var question in questions.EnumerateArray())
                foreach (var choice in question.GetProperty("choices").EnumerateArray())
                {
                    if (++choicesRead > maxChoices) throw new InteractiveVideoException("INTERACTIVE_LIMIT");
                    var next = (PositiveId(choice.GetProperty("cid")), PositiveId(choice.GetProperty("id")));
                    if (!seen.Add(next)) continue;
                    if (seen.Count > maxNodes) throw new InteractiveVideoException("INTERACTIVE_LIMIT");
                    pending.Enqueue(next);
                }
            }
            return media.Values.ToList();
        }
        catch (InteractiveVideoException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InteractiveVideoException("INTERACTIVE_TIMEOUT"); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException e)
        {
            throw new InteractiveVideoException(e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "INTERACTIVE_AUTH" : e.StatusCode == HttpStatusCode.TooManyRequests
                ? "INTERACTIVE_RATE_LIMIT" : "INTERACTIVE_HTTP_ERROR");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InteractiveVideoException("INTERACTIVE_INCOMPLETE"); }
    }

    private static long PositiveId(JsonElement value)
    {
        if (!value.TryGetInt64(out var id) || id <= 0 || id > 9007199254740991)
            throw new JsonException();
        return id;
    }

    private async Task<JsonElement> DataAsync(string path, CancellationToken token)
    {
        var text = await request("https://api.bilibili.com" + path, token);
        token.ThrowIfCancellationRequested();
        using var json = JsonDocument.Parse(text);
        var code = json.RootElement.GetProperty("code").GetInt32();
        if (code != 0) throw new InteractiveVideoException(code is -101 or -111 ? "INTERACTIVE_AUTH"
            : code is -352 or -412 or -799 ? "INTERACTIVE_RATE_LIMIT" : "INTERACTIVE_INCOMPLETE");
        var data = json.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object) throw new JsonException();
        return data.Clone();
    }

    private static async Task<string> RequestAsync(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.TryAddWithoutValidation("User-Agent", Util.HTTPUtil.UserAgent);
        message.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        message.Headers.TryAddWithoutValidation("Cookie", Config.COOKIE);
        using var response = await Util.HTTPUtil.AppHttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, timeout.Token);
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }
}
