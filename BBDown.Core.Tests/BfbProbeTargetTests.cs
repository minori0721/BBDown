namespace BBDown.Core.Tests;

using static BBDown.Core.Entity.Entity;

public class BfbProbeTargetTests
{
    [Theory]
    [InlineData("4K 超清", "4K")]
    [InlineData("8K 超高清", "8K")]
    [InlineData("1080P 60帧", "1080P60")]
    [InlineData("1080P 高码率", "1080P+")]
    [InlineData("1080P", "1080P")]
    public void NormalizesBilibiliQualityLabels(string actual, string requested)
    {
        Assert.Equal(requested, BfbProbeTarget.NormalizeQuality(actual));
    }

    [Theory]
    [InlineData("H.264", "AVC")]
    [InlineData("hvc1", "HEVC")]
    [InlineData("av01", "AV1")]
    public void NormalizesCodecAliases(string actual, string requested)
    {
        Assert.Equal(requested, BfbProbeTarget.NormalizeCodec(actual));
    }

    [Fact]
    public void RequiresAnExplicitTargetToTriggerExactProbe()
    {
        Assert.False(BfbProbeTarget.Matches("4K 超清", "HEVC", null, null));
        Assert.True(BfbProbeTarget.Matches("4K 超清", "HEVC", "4K", "HEVC"));
        Assert.False(BfbProbeTarget.Matches("4K 超清", "AVC", "4K", "HEVC"));
    }

    [Fact]
    public void SelectsOnlyTheFirstSortedTrackMatchingTheExactTarget()
    {
        var first = Video("120", "HEVC", "4K 超清");
        var second = Video("120", "HEVC", "4K 超清");
        var tracks = new[] { Video("120", "AVC", "4K 超清"), first, second };

        Assert.Same(first, BfbProbeTarget.SelectFirst(tracks, "4K", "HEVC"));
    }

    [Theory]
    [InlineData(120, 60, 30, 120)]
    [InlineData(0, 60, 30, 60)]
    [InlineData(0, 0, 30, 30)]
    [InlineData(0, 0, 0, 0)]
    public void ResolvesDurationFromPageThenSelectedTracks(int page, int video, int audio, int expected)
    {
        Assert.Equal(expected, BfbProbeTarget.ResolveDurationSeconds(page, video, audio));
    }

    private static Video Video(string id, string codec, string quality)
    {
        return new Video
        {
            id = id,
            dfn = quality,
            baseUrl = $"https://cdn.example/{id}/{codec}",
            codecs = codec,
            bandwith = 1000,
            dur = 60,
        };
    }
}
