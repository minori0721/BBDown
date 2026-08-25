using System.Net;
using System.Net.Http.Headers;

namespace BBDown.Core;

public readonly record struct MediaSizeProbeResult(long? Bytes, string Source)
{
    public static MediaSizeProbeResult Unknown => new(null, "unknown");
}

/// <summary>
/// Reads a media object's total byte count without downloading its body.
/// This is intentionally separate from normal download requests so callers
/// can opt into exact size refinement only after a user selects a track.
/// </summary>
public static class MediaSizeProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public static Task<MediaSizeProbeResult> ProbeAsync(
        string url,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        return ProbeCoreAsync(url, client ?? Util.HTTPUtil.AppHttpClient, cancellationToken);
    }

    private static async Task<MediaSizeProbeResult> ProbeCoreAsync(
        string url,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return MediaSizeProbeResult.Unknown;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        HttpResponseMessage? headResponse = null;
        try
        {
            using var request = CreateRequest(HttpMethod.Head, uri);
            headResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var headLength = TryReadLength(headResponse);
            if (headResponse.IsSuccessStatusCode && headLength.HasValue)
            {
                return new MediaSizeProbeResult(headLength.Value, "head");
            }

            if (!ShouldTryRange(headResponse.StatusCode) && !headResponse.IsSuccessStatusCode)
            {
                return MediaSizeProbeResult.Unknown;
            }
        }
        catch (OperationCanceledException)
        {
            return MediaSizeProbeResult.Unknown;
        }
        catch (HttpRequestException)
        {
            return MediaSizeProbeResult.Unknown;
        }
        finally
        {
            headResponse?.Dispose();
        }

        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            using var rangeResponse = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (rangeResponse.StatusCode == HttpStatusCode.PartialContent)
            {
                var total = rangeResponse.Content.Headers.ContentRange?.Length;
                if (total.HasValue && total.Value >= 0)
                {
                    return new MediaSizeProbeResult(total.Value, "range");
                }
            }

            // Some CDN implementations ignore Range and return the complete
            // object headers. We still do not read the response body.
            if (rangeResponse.IsSuccessStatusCode)
            {
                var length = TryReadLength(rangeResponse);
                if (length.HasValue)
                {
                    return new MediaSizeProbeResult(length.Value, "range");
                }
            }
        }
        catch (OperationCanceledException)
        {
            return MediaSizeProbeResult.Unknown;
        }
        catch (HttpRequestException)
        {
            return MediaSizeProbeResult.Unknown;
        }

        return MediaSizeProbeResult.Unknown;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", Util.HTTPUtil.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    private static long? TryReadLength(HttpResponseMessage response)
    {
        var length = response.Content.Headers.ContentLength;
        return length.HasValue && length.Value >= 0 ? length : null;
    }

    private static bool ShouldTryRange(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Forbidden
            or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.NotImplemented;
    }
}
