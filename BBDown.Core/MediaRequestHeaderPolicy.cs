namespace BBDown.Core;

public static class MediaRequestHeaderPolicy
{
    // Keep size probes in step with the actual media download request.
    // Includes both platform=android and platform=android_tv_yst URLs.
    // Bilibili APP media URLs reject a Bilibili Referer on some CDNs.
    public static bool SendReferer(string url) =>
        !url.Contains("platform=android", StringComparison.Ordinal);
}
