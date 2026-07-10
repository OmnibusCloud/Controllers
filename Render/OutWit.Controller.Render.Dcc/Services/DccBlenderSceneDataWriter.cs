using System;
using System.Collections.Generic;
using System.IO;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Controller.Render.Dcc.Services;

/// <summary>
/// Accumulates the bulk mesh payload (vertex positions, triangle indices, normals, UVs, colours,
/// shape-key positions, per-face material indices) into one binary sidecar the generated Python
/// reads back with numpy + foreach_set. Emitting this data as Python literals made Blender's
/// parser and per-element RNA loops the pipeline bottleneck: a 60 MB scene produced a 129 MB
/// script that took ~20 minutes to execute against ~7 seconds of actual rendering.
/// Layout: raw little-endian float32 / int32 runs addressed by byte offset — no header, the
/// generated script carries the offsets and counts inline.
/// </summary>
internal sealed class DccBlenderSceneDataWriter
{
    #region Constants

    public const string FILE_NAME = "scene-data.bin";

    #endregion

    #region Fields

    private readonly MemoryStream m_stream = new();
    private readonly BinaryWriter m_writer;

    #endregion

    #region Constructors

    public DccBlenderSceneDataWriter()
    {
        m_writer = new BinaryWriter(m_stream);
    }

    #endregion

    #region Functions

    public long AppendVectors3(IReadOnlyList<DccVector3Data> vectors)
    {
        var offset = m_stream.Length;
        foreach (var vector in vectors)
        {
            WriteFloat(vector.X);
            WriteFloat(vector.Y);
            WriteFloat(vector.Z);
        }

        return offset;
    }

    public long AppendVectors2(IReadOnlyList<DccVector2Data> vectors)
    {
        var offset = m_stream.Length;
        foreach (var vector in vectors)
        {
            WriteFloat(vector.X);
            WriteFloat(vector.Y);
        }

        return offset;
    }

    public long AppendColors(IReadOnlyList<DccColorData> colors)
    {
        var offset = m_stream.Length;
        foreach (var color in colors)
        {
            WriteFloat(color.R);
            WriteFloat(color.G);
            WriteFloat(color.B);
            WriteFloat(color.A);
        }

        return offset;
    }

    public long AppendInts(IReadOnlyList<int> values)
    {
        var offset = m_stream.Length;
        foreach (var value in values)
            m_writer.Write(value);

        return offset;
    }

    public byte[] ToArray()
    {
        m_writer.Flush();
        return m_stream.ToArray();
    }

    private void WriteFloat(double value)
    {
        // Same backstop as the text formatter: a NaN/Infinity would otherwise surface only as
        // corrupt geometry deep inside Blender.
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                $"Render.BuildBlendFromDccScene cannot emit non-finite value '{value}' into the scene data payload.");
        }

        m_writer.Write((float)value);
    }

    #endregion
}
