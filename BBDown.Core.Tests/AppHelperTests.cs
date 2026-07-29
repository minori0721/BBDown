using System.Text.Json;
using Google.Protobuf;
using BBDown.Core.Protobuf.PlayerUnite;

namespace BBDown.Core.Tests;

public class AppHelperTests
{
    [Fact]
    public void PlayerUniteRequestUsesTheModernVodContract()
    {
        var request = AppHelper.BuildPlayerUniteRequest(123, 456, CodeType.Code265);

        Assert.Equal(123, request.Vod.Aid);
        Assert.Equal(456, request.Vod.Cid);
        Assert.Equal((ulong)127, request.Vod.Qn);
        Assert.Equal(4048, request.Vod.Fnval);
        Assert.Equal(2, request.Vod.ForceHost);
        Assert.True(request.Vod.Fourk);
        Assert.Equal(CodeType.Code265, request.Vod.PreferCodecType);

        var selectedQuality = AppHelper.BuildPlayerUniteRequest(123, 456, CodeType.Code265, 80);
        Assert.Equal((ulong)80, selectedQuality.Vod.Qn);
    }

    [Theory]
    [InlineData("HEVC", CodeType.Code265)]
    [InlineData("AVC", CodeType.Code264)]
    [InlineData("AV1", CodeType.Codeav1)]
    [InlineData("FLAC", CodeType.Code265)]
    public void CodecRequestsPreferTheConfiguredCodecAndCoverAllFormats(string encoding, CodeType expectedFirst)
    {
        var codecs = AppHelper.GetPlayerUniteCodeTypes(encoding);

        Assert.Equal(expectedFirst, codecs[0]);
        Assert.Equal(3, codecs.Count);
        Assert.Equal(3, codecs.Distinct().Count());
        Assert.Contains(CodeType.Code264, codecs);
        Assert.Contains(CodeType.Code265, codecs);
        Assert.Contains(CodeType.Codeav1, codecs);
    }

    [Fact]
    public void PlayerUniteRepliesMergeVideoCodecsAndDeduplicateAudio()
    {
        var hevc = ReplyWithVideo(120, 12, 3840, 2160, "60", 6_000_000, "https://video/hevc");
        var avc = ReplyWithVideo(120, 7, 3840, 2160, "60", 7_000_000, "https://video/avc");
        var low = ReplyWithVideo(32, 12, 852, 480, "30", 800_000, "https://video/480");
        foreach (var reply in new[] { hevc, avc, low })
        {
            reply.VodInfo.Timelength = 12_345;
            reply.VodInfo.DashAudio.Add(new DashItem
            {
                Id = 30280,
                BaseUrl = "https://audio/m4a",
                Bandwidth = 192_000
            });
        }
        hevc.VodInfo.Dolby = new DolbyItem();
        hevc.VodInfo.Dolby.Audio.Add(new DashItem
        {
            Id = 30250,
            BaseUrl = "https://audio/dolby",
            Bandwidth = 448_000
        });
        hevc.VodInfo.LossLessItem = new LossLessItem
        {
            Audio = new DashItem
            {
                Id = 30251,
                BaseUrl = "https://audio/flac",
                Bandwidth = 1_200_000
            }
        };

        using var json = JsonDocument.Parse(AppHelper.ConvertPlayerUniteToDashJson([hevc, avc, low]));
        var dash = json.RootElement.GetProperty("data").GetProperty("dash");
        var videos = dash.GetProperty("video").EnumerateArray().ToList();
        var audios = dash.GetProperty("audio").EnumerateArray().ToList();

        Assert.Equal(3, videos.Count);
        Assert.Equal(3, audios.Count);
        Assert.Contains(videos, item => item.GetProperty("id").GetUInt32() == 120
            && item.GetProperty("codecid").GetUInt32() == 12
            && item.GetProperty("width").GetInt32() == 3840
            && item.GetProperty("frame_rate").GetString() == "60");
        Assert.Contains(audios, item => item.GetProperty("codecs").GetString() == "M4A");
        Assert.Contains(audios, item => item.GetProperty("codecs").GetString() == "E-AC-3");
        Assert.Contains(audios, item => item.GetProperty("codecs").GetString() == "FLAC");
    }

    [Fact]
    public async Task PlayerUniteRequestKeepsSuccessfulCodecWhenAnotherRequestFails()
    {
        var requested = new List<CodeType>();
        var json = await AppHelper.RequestPlayerUniteAsync(123, 456, "HEVC", "", (_, body, _) =>
        {
            var request = PlayViewUniteReq.Parser.ParseFrom(AppHelper.ReadMessage(body));
            requested.Add(request.Vod.PreferCodecType);
            if (request.Vod.PreferCodecType == CodeType.Code264)
            {
                throw new HttpRequestException("simulated codec failure");
            }

            var reply = request.Vod.PreferCodecType == CodeType.Code265
                ? ReplyWithVideo(120, 12, 3840, 2160, "60", 6_000_000, "https://video/hevc")
                : new PlayViewUniteReply { VodInfo = new VodInfo() };
            return Task.FromResult(AppHelper.PackMessage(reply.ToByteArray()));
        });

        using var document = JsonDocument.Parse(json);
        var videos = document.RootElement.GetProperty("data").GetProperty("dash").GetProperty("video").EnumerateArray().ToList();
        Assert.Equal(3, requested.Count);
        Assert.Contains(CodeType.Code264, requested);
        Assert.Contains(CodeType.Code265, requested);
        Assert.Contains(CodeType.Codeav1, requested);
        Assert.Single(videos);
        Assert.Equal((uint)120, videos[0].GetProperty("id").GetUInt32());
    }

    [Fact]
    public async Task PlayerUniteRequestSignalsWhenAllCodecRepliesAreEmpty()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppHelper.RequestPlayerUniteAsync(123, 456, "HEVC", "", (_, _, _) =>
            {
                var reply = new PlayViewUniteReply { VodInfo = new VodInfo() };
                return Task.FromResult(AppHelper.PackMessage(reply.ToByteArray()));
            }));

        Assert.Contains("BFB_SIGNAL:APP_NO_VIDEO_INFO", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredBuvidIsValidatedAndFallbackIsStableForTheProcess()
    {
        var previous = Config.APP_BUVID;
        try
        {
            Config.APP_BUVID = "XY0123456789abcdef0123456789abcdef012";
            Assert.True(AppHelper.IsValidBuvid(Config.APP_BUVID));
            Assert.Equal(Config.APP_BUVID, AppHelper.ResolveBuvid());

            Config.APP_BUVID = "invalid";
            var first = AppHelper.ResolveBuvid();
            Assert.True(AppHelper.IsValidBuvid(first));
            Assert.Equal(first, AppHelper.ResolveBuvid());
        }
        finally
        {
            Config.APP_BUVID = previous;
        }
    }

    [Fact]
    public void AppRequestDiagnosticsRedactCredentialsAndDeviceIdentity()
    {
        var headers = AppHelper.RedactHeadersForLog(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["authorization"] = "identify_v1 token",
            ["x-bili-metadata-bin"] = "metadata",
            ["x-bili-device-bin"] = "device",
            ["buvid"] = "XY0123456789abcdef0123456789abcdef012",
            ["grpc-encoding"] = "gzip"
        });

        Assert.Equal("[REDACTED]", headers["authorization"]);
        Assert.Equal("[REDACTED]", headers["x-bili-metadata-bin"]);
        Assert.Equal("[REDACTED]", headers["x-bili-device-bin"]);
        Assert.Equal("[REDACTED]", headers["buvid"]);
        Assert.Equal("gzip", headers["grpc-encoding"]);
    }

    private static PlayViewUniteReply ReplyWithVideo(
        uint quality,
        uint codec,
        int width,
        int height,
        string frameRate,
        uint bandwidth,
        string url)
    {
        var reply = new PlayViewUniteReply { VodInfo = new VodInfo() };
        reply.VodInfo.StreamList.Add(new BBDown.Core.Protobuf.PlayerUnite.Stream
        {
            StreamInfo = new StreamInfo { Quality = quality },
            DashVideo = new DashVideo
            {
                BaseUrl = url,
                Codecid = codec,
                Width = width,
                Height = height,
                FrameRate = frameRate,
                Bandwidth = bandwidth
            }
        });
        return reply;
    }
}
