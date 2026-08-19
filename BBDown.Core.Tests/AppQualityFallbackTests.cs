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

        var decision = AppQualityFallback.DecideWebProbe(app, priorities, new Dictionary<string, byte> { ["HEVC"] = 0 });

        Assert.True(decision.ShouldProbe);
        Assert.Equal(AppWebFallbackReason.LowAppQuality, decision.Reason);
        Assert.Equal(127, AppQualityFallback.ResolveRequestedQualityId(priorities));
    }

    [Fact]
    public void RequestedQualityTriggersComparisonButSatisfiedQualityDoesNot()
    {
        var priorities = new Dictionary<string, int> { ["4K 超清"] = 0 };

        Assert.True(AppQualityFallback.DecideWebProbe(
            Result(Video("80", "HEVC", 2_000)),
            priorities,
            new Dictionary<string, byte> { ["HEVC"] = 0 }).ShouldProbe);
        Assert.False(AppQualityFallback.DecideWebProbe(
            Result(Video("120", "HEVC", 5_000)),
            priorities,
            new Dictionary<string, byte> { ["HEVC"] = 0 }).ShouldProbe);
    }

    [Fact]
    public void SameQualityAvcWithoutPreferredHevcTriggersAWebComparison()
    {
        var decision = AppQualityFallback.DecideWebProbe(
            Result(Video("120", "AVC", 5_000)),
            new Dictionary<string, int>(),
            new Dictionary<string, byte> { ["HEVC"] = 0, ["AVC"] = 1 });

        Assert.True(decision.ShouldProbe);
        Assert.Equal(AppWebFallbackReason.PreferredCodecMissingAtBestQuality, decision.Reason);
        Assert.Equal("HEVC", decision.PreferredCodec);
        Assert.Equal(0, decision.AppPreferredQuality);
    }

    [Fact]
    public void LowerPreferredCodecDoesNotTriggerAQualityDowngradeMerge()
    {
        var app = Result(Video("120", "AVC", 5_000));
        var result = AppQualityFallback.MergeWebVideo(
            app,
            Result(Video("80", "HEVC", 2_000)),
            "HEVC");

        Assert.False(result.Applied);
        Assert.Single(app.VideoTracks);
        Assert.Equal("AVC", app.VideoTracks[0].codecs);
    }

    [Fact]
    public void SameQualityPreferredCodecIsMerged()
    {
        var app = Result(Video("120", "AVC", 5_000));
        var result = AppQualityFallback.MergeWebVideo(
            app,
            Result(Video("120", "HEVC", 4_000)),
            "HEVC");

        Assert.True(result.Applied);
        Assert.Equal(2, app.VideoTracks.Count);
        Assert.Contains(app.VideoTracks, track => track.codecs == "HEVC");
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
