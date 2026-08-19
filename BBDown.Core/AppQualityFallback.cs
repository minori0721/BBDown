using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core;

public readonly record struct AppVideoFallbackResult(
    bool Applied,
    int AppHighestQuality,
    int WebHighestQuality);

public enum AppWebFallbackReason
{
    None,
    LowAppQuality,
    RequestedQualityMissing,
    PreferredCodecMissingAtBestQuality,
}

public readonly record struct AppWebFallbackDecision(
    bool ShouldProbe,
    AppWebFallbackReason Reason,
    int AppHighestQuality,
    int AppPreferredQuality,
    int RequestedQuality,
    string? PreferredCodec);

public static class AppQualityFallback
{
    public static int ResolveRequestedQualityId(IReadOnlyDictionary<string, int> priorities)
    {
        foreach (var priority in priorities.OrderBy(pair => pair.Value))
        {
            var ids = Config.qualitys
                .Where(pair => pair.Value.Equals(priority.Key, StringComparison.OrdinalIgnoreCase))
                .Select(pair => int.TryParse(pair.Key, out var value) ? value : 0)
                .Where(value => value > 0)
                .ToList();
            if (ids.Count > 0) return ids.Max();
        }
        return 0;
    }

    public static int HighestQuality(IEnumerable<Video> tracks)
    {
        return tracks
            .Select(track => int.TryParse(track.id, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    public static int HighestQuality(IEnumerable<Video> tracks, string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec)) return 0;
        return HighestQuality(tracks.Where(track => track.codecs.Equals(codec, StringComparison.OrdinalIgnoreCase)));
    }

    public static string? ResolvePreferredVideoCodec(IReadOnlyDictionary<string, byte> priorities)
    {
        return priorities
            .Where(pair => pair.Key.Equals("AVC", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("HEVC", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("AV1", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Value)
            .Select(pair => pair.Key.ToUpperInvariant())
            .FirstOrDefault();
    }

    public static AppWebFallbackDecision DecideWebProbe(
        ParsedResult appResult,
        IReadOnlyDictionary<string, int> dfnPriorities,
        IReadOnlyDictionary<string, byte> encodingPriorities)
    {
        var appHighest = HighestQuality(appResult.VideoTracks);
        var requested = ResolveRequestedQualityId(dfnPriorities);
        var preferredCodec = ResolvePreferredVideoCodec(encodingPriorities);
        var appPreferred = HighestQuality(appResult.VideoTracks, preferredCodec);
        if (appHighest <= 0)
        {
            return new AppWebFallbackDecision(false, AppWebFallbackReason.None, appHighest, appPreferred, requested, preferredCodec);
        }
        if (appHighest <= 32)
        {
            return new AppWebFallbackDecision(true, AppWebFallbackReason.LowAppQuality, appHighest, appPreferred, requested, preferredCodec);
        }
        if (requested > 0 && appHighest < requested)
        {
            return new AppWebFallbackDecision(true, AppWebFallbackReason.RequestedQualityMissing, appHighest, appPreferred, requested, preferredCodec);
        }
        if (preferredCodec is not null && appPreferred < appHighest)
        {
            return new AppWebFallbackDecision(true, AppWebFallbackReason.PreferredCodecMissingAtBestQuality, appHighest, appPreferred, requested, preferredCodec);
        }
        return new AppWebFallbackDecision(false, AppWebFallbackReason.None, appHighest, appPreferred, requested, preferredCodec);
    }

    public static AppVideoFallbackResult MergeHigherWebVideo(ParsedResult appResult, ParsedResult webResult)
    {
        return MergeWebVideo(appResult, webResult, null);
    }

    public static AppVideoFallbackResult MergeWebVideo(
        ParsedResult appResult,
        ParsedResult webResult,
        string? preferredCodec)
    {
        var appHighest = HighestQuality(appResult.VideoTracks);
        var eligible = webResult.VideoTracks
            .Where(track =>
            {
                var quality = ParseQuality(track);
                return quality > appHighest
                    || (quality == appHighest
                        && preferredCodec is not null
                        && track.codecs.Equals(preferredCodec, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
        var webHighest = HighestQuality(eligible);
        if (eligible.Count == 0)
        {
            return new AppVideoFallbackResult(false, appHighest, webHighest);
        }

        appResult.VideoTracks = appResult.VideoTracks
            .Concat(eligible)
            .GroupBy(track => (track.id, track.codecs))
            .Select(group => group
                .OrderByDescending(track => !string.IsNullOrWhiteSpace(track.res))
                .ThenByDescending(track => track.bandwith)
                .ThenBy(track => track.baseUrl, StringComparer.Ordinal)
                .First())
            .ToList();
        return new AppVideoFallbackResult(true, appHighest, webHighest);
    }

    public static List<Video> SortVideoTracks(
        IEnumerable<Video> videoTracks,
        IReadOnlyDictionary<string, int> dfnPriority,
        IReadOnlyDictionary<string, byte> encodingPriority,
        bool encodingFirst,
        bool videoAscending)
    {
        return encodingFirst
            ? videoTracks
                .OrderBy(video => encodingPriority.GetValueOrDefault(video.codecs, (byte)100))
                .ThenBy(video => dfnPriority.GetValueOrDefault(video.dfn, 100))
                .ThenByDescending(video => ParseQuality(video))
                .ThenBy(video => videoAscending ? video.bandwith : -video.bandwith)
                .ToList()
            : videoTracks
                .OrderBy(video => dfnPriority.GetValueOrDefault(video.dfn, 100))
                .ThenBy(video => encodingPriority.GetValueOrDefault(video.codecs, (byte)100))
                .ThenByDescending(video => ParseQuality(video))
                .ThenBy(video => videoAscending ? video.bandwith : -video.bandwith)
                .ToList();
    }

    private static int ParseQuality(Video video)
    {
        return int.TryParse(video.id, out var value) ? value : 0;
    }
}
