using System.Net;
using System.Net.Http.Headers;

namespace BBDown.Core.Tests;

public class MediaSizeProbeTests
{
    [Fact]
    public async Task UsesHeadContentLengthWithoutReadingTheBody()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            response.Content.Headers.ContentLength = 1234;
            return response;
        });

        var result = await MediaSizeProbe.ProbeAsync("https://cdn.example/video", new HttpClient(handler));

        Assert.Equal(1234L, result.Bytes);
        Assert.Equal("head", result.Source);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Head, request.Method);
        Assert.Null(request.Range);
    }

    [Fact]
    public async Task FallsBackToOneByteRangeWhenHeadIsRejected()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(new byte[] { 0 })
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 9876);
            return response;
        });

        var result = await MediaSizeProbe.ProbeAsync("https://cdn.example/video", new HttpClient(handler));

        Assert.Equal(9876L, result.Bytes);
        Assert.Equal("range", result.Source);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("bytes=0-0", handler.Requests[1].Range);
    }

    [Fact]
    public async Task DoesNotTreatPartialContentLengthAsTheCompleteObjectSize()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(new byte[] { 0 })
            };
            response.Content.Headers.ContentLength = 1;
            return response;
        });

        var result = await MediaSizeProbe.ProbeAsync("https://cdn.example/video", new HttpClient(handler));

        Assert.Null(result.Bytes);
        Assert.Equal("unknown", result.Source);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UsesContentLengthWhenTheServerIgnoresRangeWithOk()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            response.Content.Headers.ContentLength = 4321;
            return response;
        });

        var result = await MediaSizeProbe.ProbeAsync("https://cdn.example/video", new HttpClient(handler));

        Assert.Equal(4321L, result.Bytes);
        Assert.Equal("range", result.Source);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task DoesNotTreatRemoteErrorsAsAFileSize(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode));

        var result = await MediaSizeProbe.ProbeAsync("https://cdn.example/video", new HttpClient(handler));

        Assert.Null(result.Bytes);
        Assert.Equal("unknown", result.Source);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RejectsNonHttpUrlsWithoutMakingARequest()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await MediaSizeProbe.ProbeAsync("file:///tmp/video.mp4", new HttpClient(handler));

        Assert.Null(result.Bytes);
        Assert.Equal("unknown", result.Source);
        Assert.Empty(handler.Requests);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<RequestInfo, HttpResponseMessage> responder;

        public List<RequestInfo> Requests { get; } = [];

        public StubHandler(Func<RequestInfo, HttpResponseMessage> responder)
        {
            this.responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var info = new RequestInfo(request.Method, request.Headers.Range?.ToString());
            Requests.Add(info);
            return Task.FromResult(responder(info));
        }
    }

    private sealed record RequestInfo(HttpMethod Method, string? Range);
}
