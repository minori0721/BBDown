using System.Text.RegularExpressions;

namespace BBDown.Core;

public static class BfbProbeTarget
{
    public static bool Matches(string actualQuality, string actualCodec, string? requestedQuality, string? requestedEncoding)
    {
        if (string.IsNullOrWhiteSpace(requestedQuality) && string.IsNullOrWhiteSpace(requestedEncoding))
        {
            return false;
        }

        var qualityMatches = string.IsNullOrWhiteSpace(requestedQuality)
            || NormalizeQuality(actualQuality) == NormalizeQuality(requestedQuality);
        var encodingMatches = string.IsNullOrWhiteSpace(requestedEncoding)
            || NormalizeCodec(actualCodec) == NormalizeCodec(requestedEncoding);
        return qualityMatches && encodingMatches;
    }

    public static string NormalizeQuality(string value)
    {
        var label = Regex.Replace(value.Trim(), @"\s+", " ");
        if (label.Length == 0) return string.Empty;
        if (Regex.IsMatch(label, "杜比视界|dolby\\s*vision", RegexOptions.IgnoreCase)) return "杜比视界";
        if (Regex.IsMatch(label, @"\bHDR\b|HDR\s*真彩", RegexOptions.IgnoreCase)) return "HDR";
        if (Regex.IsMatch(label, @"(?:^|\s)8K(?:\s|$)|4320P", RegexOptions.IgnoreCase)) return "8K";
        if (Regex.IsMatch(label, @"(?:^|\s)4K(?:\s|$)|2160P", RegexOptions.IgnoreCase)) return "4K";
        if (Regex.IsMatch(label, @"1080P.*(?:60|高帧率)|(?:60|高帧率).*1080P", RegexOptions.IgnoreCase)) return "1080P60";
        if (Regex.IsMatch(label, @"1080P\+|1080P.*高码率", RegexOptions.IgnoreCase)) return "1080P+";
        if (Regex.IsMatch(label, "1080P", RegexOptions.IgnoreCase)) return "1080P";
        if (Regex.IsMatch(label, @"720P.*(?:60|高帧率)|(?:60|高帧率).*720P", RegexOptions.IgnoreCase)) return "720P60";
        if (Regex.IsMatch(label, "720P", RegexOptions.IgnoreCase)) return "720P";
        if (Regex.IsMatch(label, "480P", RegexOptions.IgnoreCase)) return "480P";
        if (Regex.IsMatch(label, "360P", RegexOptions.IgnoreCase)) return "360P";
        return label.Replace(" ", string.Empty).ToUpperInvariant();
    }

    public static string NormalizeCodec(string value)
    {
        var normalized = value.Trim().ToUpperInvariant()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(".", string.Empty);
        return normalized switch
        {
            "H264" or "AVC1" or "AVC" => "AVC",
            "H265" or "HEVC" or "HVC1" or "HEV1" => "HEVC",
            "AV01" or "AV1" => "AV1",
            _ => normalized,
        };
    }
}
