using System.Net;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Tests;

public class InteractiveVideoResolverTests
{
    private static Page Root() => new(1, "925056794", "170981193", "", "Root", 12, "1920x1080", 1);
    private static string Player() => "{\"code\":0,\"data\":{\"interaction\":{\"graph_version\":189446}}}";
    private static string Edge(int edge, params (int Id, long Cid)[] choices) => JsonSerializer.Serialize(new
    {
        code = 0, data = new { edge_id = edge, title = "Node", edges = new
        { questions = choices.Length == 0 ? Array.Empty<object>() : new object[] { new { choices = choices.Select(c => new { id = c.Id, cid = c.Cid }) } } } }
    });

    [Fact]
    public async Task TraversesDeepBranchesAndCyclesWithoutDeduplicatingNodesByCid()
    {
        var calls = new List<string>();
        var resolver = new InteractiveVideoResolver((url, _) =>
        {
            calls.Add(url);
            if (url.Contains("/player/")) return Task.FromResult(Player());
            var result = url.Contains("edge_id=0&") ? Edge(1, (2, 200), (3, 200))
                : url.Contains("edge_id=2&") ? Edge(2, (4, 300))
                : url.Contains("edge_id=3&") ? Edge(3, (5, 400))
                : url.Contains("edge_id=4&") ? Edge(4, (2, 200)) : Edge(5);
            return Task.FromResult(result);
        });
        var pages = await resolver.ResolveAsync(Root());
        Assert.Equal(new[] { "170981193", "200", "300", "400" }, pages.Select(p => p.cid));
        Assert.Equal(new[] { 1, 2, 3, 4 }, pages.Select(p => p.index));
        Assert.Equal(6, calls.Count);
        Assert.DoesNotContain(calls, url => url.Contains("player.so"));
        Assert.Equal(0, pages[1].dur);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(100, 1)]
    public async Task LimitsFailClosed(int nodes, int choices)
    {
        var resolver = new InteractiveVideoResolver((url, _) => Task.FromResult(url.Contains("/player/")
            ? Player() : Edge(1, (2, 200), (3, 300))), nodes, choices);
        var error = await Assert.ThrowsAsync<InteractiveVideoException>(() => resolver.ResolveAsync(Root()));
        Assert.Equal("INTERACTIVE_LIMIT", error.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"code\":0,\"data\":{\"edge_id\":1}}")]
    [InlineData("{\"code\":0,\"data\":{\"edge_id\":1,\"edges\":{\"questions\":null}}}")]
    [InlineData("{\"code\":-404}")]
    [InlineData("<html>error</html>")]
    public async Task DoesNotReturnPartialInventoryOnInvalidGraph(string graph)
    {
        var resolver = new InteractiveVideoResolver((url, _) => Task.FromResult(url.Contains("/player/") ? Player() : graph));
        var error = await Assert.ThrowsAsync<InteractiveVideoException>(() => resolver.ResolveAsync(Root()));
        Assert.Equal("INTERACTIVE_INCOMPLETE", error.Code);
    }

    [Theory]
    [InlineData(401, "INTERACTIVE_AUTH")]
    [InlineData(429, "INTERACTIVE_RATE_LIMIT")]
    [InlineData(500, "INTERACTIVE_HTTP_ERROR")]
    public async Task HttpFailuresHaveSafeCodes(int status, string expected)
    {
        var resolver = new InteractiveVideoResolver((_, _) => throw new HttpRequestException("secret", null, (HttpStatusCode)status));
        var error = await Assert.ThrowsAsync<InteractiveVideoException>(() => resolver.ResolveAsync(Root()));
        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var resolver = new InteractiveVideoResolver((_, token) => Task.FromCanceled<string>(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(Root(), cancel.Token));
    }

    [Fact]
    public void DigestBindsOrder()
    {
        var a = Root();
        var b = Root(); b.cid = "200";
        Assert.Equal(64, InteractiveVideoResolver.PageSetHash([a]).Length);
        Assert.NotEqual(InteractiveVideoResolver.PageSetHash([a, b]), InteractiveVideoResolver.PageSetHash([b, a]));
    }

    [Theory]
    [InlineData(404, true)]
    [InlineData(405, true)]
    [InlineData(501, true)]
    [InlineData(403, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    public async Task OnlyUnsupportedWbiEndpointFallsBack(int status, bool fallback)
    {
        var previous = Config.WBI;
        Config.WBI = "test-key";
        var paths = new List<string>();
        try
        {
            var resolver = new InteractiveVideoResolver((url, _) =>
            {
                paths.Add(url);
                if (url.Contains("/wbi/")) throw new HttpRequestException("secret", null, (HttpStatusCode)status);
                return Task.FromResult(url.Contains("/player/") ? Player() : Edge(1));
            });
            if (fallback) Assert.Single(await resolver.ResolveAsync(Root()));
            else await Assert.ThrowsAsync<InteractiveVideoException>(() => resolver.ResolveAsync(Root()));
            Assert.Equal(fallback, paths.Any(p => p.Contains("/x/player/v2?")));
            Assert.Equal(fallback ? 3 : 1, paths.Count);
        }
        finally { Config.WBI = previous; }
    }

    [Fact]
    public async Task RejectsAnEdgeResponseForAnotherRequestedBranch()
    {
        var resolver = new InteractiveVideoResolver((url, _) => Task.FromResult(url.Contains("/player/") ? Player()
            : url.Contains("edge_id=0&") ? Edge(1, (2, 200)) : Edge(99)));
        var error = await Assert.ThrowsAsync<InteractiveVideoException>(() => resolver.ResolveAsync(Root()));
        Assert.Equal("INTERACTIVE_INCOMPLETE", error.Code);
    }
}
