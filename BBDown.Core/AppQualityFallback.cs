using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core;

public readonly record struct AppVideoFallbackResult(
    bool Applied,
    int AppHighestQuality,
    int WebHighestQuality);

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

    public static bool ShouldProbeWeb(ParsedResult appResult, IReadOnlyDictionary<string, int> priorities)
    {
        var appHighest = HighestQuality(appResult.VideoTracks);
        if (appHighest <= 0) return false;
        if (appHighest <= 32) return true;
        var requested = ResolveRequestedQualityId(priorities);
        return requested > 0 && appHighest < requested;
    }

    public static AppVideoFallbackResult MergeHigherWebVideo(ParsedResult appResult, ParsedResult webResult)
    {
        var appHighest = HighestQuality(appResult.VideoTracks);
        var webHighest = HighestQuality(webResult.VideoTracks);
        if (webHighest <= appHighest)
        {
            return new AppVideoFallbackResult(false, appHighest, webHighest);
        }

        appResult.VideoTracks = appResult.VideoTracks
            .Concat(webResult.VideoTracks)
            .GroupBy(track => (track.id, track.codecs))
            .Select(group => group
                .OrderByDescending(track => !string.IsNullOrWhiteSpace(track.res))
                .ThenByDescending(track => track.bandwith)
                .ThenBy(track => track.baseUrl, StringComparer.Ordinal)
                .First())
            .ToList();
        return new AppVideoFallbackResult(true, appHighest, webHighest);
    }
}
