namespace OutWit.Controller.Render.Model;

/// <summary>
/// Video container + codec PRESET for the RenderVideo path. A single preset enum (instead of two
/// independent container/codec axes) keeps invalid combinations unrepresentable on the wire.
/// <see cref="Default"/> = the legacy behaviour (MP4/H.264), so payloads from older clients that
/// do not send the field are encoded exactly as before.
/// APPEND-ONLY: values travel as MemoryPack ints — never reorder or remove members.
/// </summary>
public enum VideoFormat
{
    Default,
    Mp4H264,
    Mp4H265,
    WebMVp9
}
