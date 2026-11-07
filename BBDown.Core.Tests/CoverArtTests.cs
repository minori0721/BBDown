using BBDown;

namespace BBDown.Core.Tests;

public class CoverArtTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "bbdown-cover-test-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0, 0, 0, 0, 0, 0, 0xff, 0xd9];
    public CoverArtTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    public sealed class FfmpegFactAttribute : FactAttribute
    {
        public FfmpegFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BBDOWN_TEST_FFMPEG")))
                Skip = "Set BBDOWN_TEST_FFMPEG to run the real media integration test";
        }
    }

    [FfmpegFact]
    public async Task Real_decoder_rejects_corrupt_cover_and_video_mux_can_continue_without_cover()
    {
        var previous = BBDownMuxer.FFMPEG;
        BBDownMuxer.FFMPEG = Environment.GetEnvironmentVariable("BBDOWN_TEST_FFMPEG")!;
        var cover = Path.Combine(root, "cover.jpg");
        var video = Path.Combine(root, "video.mp4");
        var output = Path.Combine(root, "result.mp4");
        try
        {
            RunFfmpeg("-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=64x64:d=0.1", "-frames:v", "1", cover);
            Assert.True(BBDownMuxer.IsReadableCover(cover));
            await File.WriteAllBytesAsync(cover, Jpeg); // signature alone cannot prove decodability
            Assert.False(BBDownMuxer.IsReadableCover(cover));
            RunFfmpeg("-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=64x64:d=0.1", "-c:v", "mpeg4", video);
            bool ready = await BBDownCoverArt.EnsureAsync(cover, "unused", (_, _) => throw new IOException("offline"), BBDownMuxer.IsReadableCover);
            Assert.False(ready);
            Assert.Equal(0, BBDownMuxer.MuxAV(false, "BVTEST", video, "", [], output, pic: ready ? cover : ""));
            Assert.True(new FileInfo(output).Length > 0);
            var webp = Path.Combine(root, "cover.webp");
            RunFfmpeg("-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=64x64:d=0.1", "-frames:v", "1", webp);
            Assert.True(BBDownMuxer.IsReadableCover(webp));
            Assert.Equal(0, BBDownMuxer.MuxAV(false, "BVTEST", video, "", [], Path.Combine(root, "webp-result.mp4"), pic: webp));
            Assert.NotEqual(0, BBDownMuxer.MuxAV(false, "BVTEST", Path.Combine(root, "missing.mp4"), "", [], Path.Combine(root, "failed.mp4")));
            Assert.True(File.Exists(video));
        }
        finally { BBDownMuxer.FFMPEG = previous; }
    }

    private static void RunFfmpeg(params string[] args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new(BBDownMuxer.FFMPEG) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start(); var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(10_000));
        Assert.True(process.ExitCode == 0, errors.GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Empty_existing_cover_is_replaced_only_after_successful_download()
    {
        var target = Path.Combine(root, "cover.jpg");
        await File.WriteAllBytesAsync(target, []);
        int calls = 0;
        Assert.True(await BBDownCoverArt.EnsureAsync(target, "https://example.test/cover", async (_, tmp) => {
            calls++;
            Assert.NotEqual(target, tmp);
            Assert.Equal(0, new FileInfo(target).Length);
            await File.WriteAllBytesAsync(tmp, Jpeg);
        }, BBDownCoverArt.HasImageSignature));
        Assert.Equal(1, calls);
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(target));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public async Task Valid_cover_is_reused_without_a_network_request()
    {
        var target = Path.Combine(root, "cover.jpg");
        await File.WriteAllBytesAsync(target, Jpeg);
        Assert.True(await BBDownCoverArt.EnsureAsync(target, "unused", (_, _) => throw new Exception("Unexpected download"), BBDownCoverArt.HasImageSignature));
    }

    [Fact]
    public async Task Failed_optional_cover_preserves_the_existing_file_and_allows_video_to_continue()
    {
        var target = Path.Combine(root, "cover.jpg");
        await File.WriteAllTextAsync(target, "invalid original");
        Assert.False(await BBDownCoverArt.EnsureAsync(target, "https://example.test/cover", async (_, tmp) => {
            await File.WriteAllTextAsync(tmp, "<html>server error</html>");
        }, BBDownCoverArt.HasImageSignature));
        Assert.Equal("invalid original", await File.ReadAllTextAsync(target));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public async Task Cover_only_reports_failure_and_cancellation_is_never_downgraded()
    {
        var target = Path.Combine(root, "cover.jpg");
        await Assert.ThrowsAsync<InvalidDataException>(() => BBDownCoverArt.EnsureAsync(target, "https://example.test/cover",
            async (_, tmp) => await File.WriteAllBytesAsync(tmp, []), BBDownCoverArt.HasImageSignature, required: true));
        await Assert.ThrowsAsync<OperationCanceledException>(() => BBDownCoverArt.EnsureAsync(target, "unused",
            (_, _) => throw new OperationCanceledException(), BBDownCoverArt.HasImageSignature));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>error</html>")]
    [InlineData("RIFF0000WEBPbad")]
    public void Non_image_responses_are_rejected(string contents)
    {
        var target = Path.Combine(root, "cover.jpg");
        File.WriteAllText(target, contents);
        Assert.False(BBDownCoverArt.HasImageSignature(target));
    }
}
