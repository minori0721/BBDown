using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Tests;

public class AppQualityFallbackTests
{
    [Fact]
    public void LowAppResultTriggersAWebComparison()
    {
        var app = Result(Video("32", "HEVC", 500));
        var priorities = new Dictionary<string, int> { ["8K 超高清"] = 0 };

        Assert.True(AppQualityFallback.ShouldProbeWeb(app, priorities));
        Assert.Equal(127, AppQualityFallback.ResolveRequestedQualityId(priorities));
    }

    [Fact]
    public void RequestedQualityTriggersComparisonButSatisfiedQualityDoesNot()
    {
        var priorities = new Dictionary<string, int> { ["4K 超清"] = 0 };

        Assert.True(AppQualityFallback.ShouldProbeWeb(Result(Video("80", "HEVC", 2_000)), priorities));
        Assert.False(AppQualityFallback.ShouldProbeWeb(Result(Video("120", "HEVC", 5_000)), priorities));
    }

    [Fact]
    public void HigherWebVideoIsMergedWithoutReplacingAppAudio()
    {
        var app = Result(Video("32", "HEVC", 500));
        app.AudioTracks.Add(new Audio
        {
            id = "30251",
            dfn = "30251",
            baseUrl = "https://audio/flac",
            codecs = "FLAC",
            bandwith = 0,
            dur = 0
        });
        var web = Result(
            Video("120", "HEVC", 5_000, "3840x2160"),
            Video("120", "AVC", 6_000, "3840x2160")
        );

        var result = AppQualityFallback.MergeHigherWebVideo(app, web);

        Assert.True(result.Applied);
        Assert.Equal(32, result.AppHighestQuality);
        Assert.Equal(120, result.WebHighestQuality);
        Assert.Equal(3, app.VideoTracks.Count);
        Assert.Single(app.AudioTracks);
        Assert.Equal("FLAC", app.AudioTracks[0].codecs);
    }

    [Fact]
    public void EqualOrLowerWebResultLeavesAppStreamsUntouched()
    {
        var app = Result(Video("80", "HEVC", 2_000));
        var original = app.VideoTracks[0];

        var result = AppQualityFallback.MergeHigherWebVideo(app, Result(Video("32", "AVC", 500)));

        Assert.False(result.Applied);
        Assert.Single(app.VideoTracks);
        Assert.Same(original, app.VideoTracks[0]);
    }

    private static ParsedResult Result(params Video[] videos)
    {
        return new ParsedResult
        {
            WebJsonString = "{}",
            VideoTracks = videos.ToList()
        };
    }

    private static Video Video(string quality, string codec, long bandwidth, string? resolution = null)
    {
        return new Video
        {
            id = quality,
            dfn = Config.qualitys[quality],
            baseUrl = $"https://video/{quality}/{codec}",
            codecs = codec,
            bandwith = bandwidth,
            res = resolution
        };
    }
}
