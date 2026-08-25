namespace BBDown.Core.Tests;

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
}
