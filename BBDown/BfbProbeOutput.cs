using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BBDown;

internal sealed class BfbProbeTrackOutput
{
    public string bilibiliQuality { get; init; } = "";
    public string codec { get; init; } = "";
    public string? resolution { get; init; }
    public string? frameRate { get; init; }
    public long bitrateKbps { get; init; }
    public string sizeSource { get; init; } = "unknown";
    public long? estimatedBytes { get; init; }
}

internal sealed class BfbProbeAudioOutput
{
    public string codec { get; init; } = "";
    public long bitrateKbps { get; init; }
    public string sizeSource { get; init; } = "unknown";
    public long? estimatedBytes { get; init; }
}

internal sealed class BfbProbePageOutput
{
    public int version { get; init; }
    public string bvid { get; init; } = "";
    public string cid { get; init; } = "";
    public int pageIndex { get; init; }
    public string pageTitle { get; init; } = "";
    public long publishedAt { get; init; }
    public int durationSeconds { get; init; }
    public string api { get; init; } = "";
    public BfbProbeAudioOutput? selectedAudio { get; init; }
    public List<BfbProbeTrackOutput> tracks { get; init; } = [];
}

[JsonSerializable(typeof(BfbProbePageOutput))]
internal partial class BfbProbeJsonContext : JsonSerializerContext
{
}
