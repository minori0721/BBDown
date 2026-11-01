using System.Diagnostics;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BBDown;

static class BBDownAria2c
{
    public static string ARIA2C = "aria2c";

    public static async Task<int> RunCommandCodeAsync(string command, string args)
    {
        using Process p = new();
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = false;
        p.StartInfo.FileName = command;
        p.StartInfo.Arguments = args;
        p.Start();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    public static async Task DownloadFileByAria2cAsync(string url, string path, string extraArgs, string? rpcConfigPath = null, DownloadTask? relatedTask = null)
    {
        if (!string.IsNullOrEmpty(rpcConfigPath))
        {
            using var progress = new ProgressBar(relatedTask);
            var clock = Stopwatch.StartNew();
            var lastAt = 0L;
            var lastBytes = 0L;
            await new BBDownAria2Rpc(rpcConfigPath).DownloadAsync(url, path,
                (downloaded, total) => {
                    progress.Report((double)downloaded / total, downloaded);
                    var elapsed = clock.ElapsedMilliseconds - lastAt;
                    if (Console.IsOutputRedirected && (elapsed >= 1000 || downloaded == total))
                    {
                        var speed = elapsed > 0 ? (downloaded - lastBytes) * 1000.0 / elapsed : 0;
                        Console.WriteLine($"下载: {BBDownUtil.FormatFileSize(downloaded)}/{BBDownUtil.FormatFileSize(total)} ({downloaded * 100.0 / total:0.0}%) - {BBDownUtil.FormatFileSize(speed)}/s");
                        lastAt = clock.ElapsedMilliseconds;
                        lastBytes = downloaded;
                    }
                });
            return;
        }
        var headerArgs = "";
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
            headerArgs += " --header=\"Referer: https://www.bilibili.com\"";
        headerArgs += " --header=\"User-Agent: Mozilla/5.0\"";
        headerArgs += $" --header=\"Cookie: {Core.Config.COOKIE}\"";
        var code = await RunCommandCodeAsync(ARIA2C, $" --auto-file-renaming=false --download-result=hide --allow-overwrite=true --console-log-level=warn -x16 -s16 -j16 -k5M {headerArgs} {extraArgs} \"{url}\" -d \"{Path.GetDirectoryName(path)}\" -o \"{Path.GetFileName(path)}\"");
        if (code != 0) throw new IOException($"aria2 transfer failed (code {code})");
    }
}
