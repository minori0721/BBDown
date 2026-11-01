using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using BBDown;
using Xunit;

public class Aria2RpcTests
{
    [Fact]
    public async Task CompletionWaitsForOwnTransferAndKeepsSharedEngineAlive()
    {
        using var server = new RpcServer();
        var config = server.WriteConfig();
        try
        {
            var reports = new List<long>();
            await new BBDownAria2Rpc(config).DownloadAsync("https://example.invalid/video", "/tmp/test-media.mp4", (bytes, _) => reports.Add(bytes));
            Assert.Equal(new long[] { 4, 8 }, reports);
            Assert.NotNull(server.LastAdd);
            Assert.StartsWith("123456789abc", server.LastAdd!["gid"]!.GetValue<string>());
            Assert.Equal("true", server.LastAdd["continue"]!.GetValue<string>());
            Assert.Equal("test-media.mp4", server.LastAdd["out"]!.GetValue<string>());
            Assert.DoesNotContain("aria2.shutdown", server.Methods);
            Assert.DoesNotContain("aria2.forceRemove", server.Methods);
        }
        finally { File.Delete(config); }
    }

    [Theory]
    [InlineData("error")]
    [InlineData("removed")]
    [InlineData("unrecognized")]
    public async Task TransferFailureIsNotReportedAsSuccessfulCompletion(string status)
    {
        using var server = new RpcServer { Status = status };
        var config = server.WriteConfig();
        try
        {
            await Assert.ThrowsAsync<IOException>(() => new BBDownAria2Rpc(config).DownloadAsync("https://example.invalid/video", "/tmp/test-media.mp4", (_, _) => {}));
            Assert.Contains("aria2.forceRemove", server.Methods);
            Assert.DoesNotContain("aria2.shutdown", server.Methods);
        }
        finally { File.Delete(config); }
    }

    [Fact]
    public void PublicEndpointIsRejectedBeforeSendingCredentials()
    {
        var config = Path.GetTempFileName();
        try
        {
            File.WriteAllText(config, "{\"endpoint\":\"https://example.invalid/jsonrpc\"}");
            Assert.Throws<InvalidDataException>(() => new BBDownAria2Rpc(config));
        }
        finally { File.Delete(config); }
    }

    private sealed class RpcServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private int polls;
        public string? Status { get; init; }
        public JsonObject? LastAdd { get; private set; }
        public List<string> Methods { get; } = new();
        private readonly string endpoint;

        public RpcServer()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            endpoint = $"http://127.0.0.1:{port}/jsonrpc";
            listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
            _ = Serve();
        }

        public string WriteConfig()
        {
            var file = Path.GetTempFileName();
            var data = new JsonObject { ["endpoint"] = endpoint, ["secret"] = new string('a', 64), ["gidPrefix"] = "123456789abc", ["options"] = new JsonObject { ["continue"] = "true" } };
            File.WriteAllText(file, data.ToJsonString()); return file;
        }

        private async Task Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                using var reader = new StreamReader(context.Request.InputStream);
                var request = JsonNode.Parse(await reader.ReadToEndAsync())!;
                var method = request["method"]!.GetValue<string>(); Methods.Add(method);
                JsonNode? result;
                switch (method)
                {
                    case "aria2.addUri": LastAdd = (JsonObject)request["params"]![2]!.DeepClone(); result = JsonValue.Create(LastAdd["gid"]!.GetValue<string>()); break;
                    case "aria2.tellStatus":
                        polls++;
                        result = new JsonObject { ["status"] = Status ?? (polls == 1 ? "active" : "complete"), ["completedLength"] = polls == 1 ? "4" : "8", ["totalLength"] = "8", ["errorCode"] = "3" }; break;
                    default: result = JsonValue.Create("OK"); break;
                }
                var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "bbdown", ["result"] = result };
                var body = Encoding.UTF8.GetBytes(response.ToJsonString());
                context.Response.ContentType = "application/json"; context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body); context.Response.Close();
            }
        }
        public void Dispose() { listener.Close(); }
    }
}
