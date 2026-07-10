using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderPythonFormatter
{
    #region Functions

    public static string FormatDouble(double value)
    {
        // Backstop: NaN/Infinity would render verbatim into the generated script and only
        // surface as a NameError deep inside Blender. The contract validator rejects the
        // known non-finite fields up front; this guards everything else.
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                $"Render.BuildBlendFromDccScene cannot emit non-finite value '{value}' into the generated Python script.");
        }

        return value.ToString("0.0###############", CultureInfo.InvariantCulture);
    }

    public static string ToPythonBool(bool value)
    {
        return value ? "True" : "False";
    }

    public static string ToPythonStringLiteral(string value)
    {
        return $"'{value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", "\\n")}'";
    }

    public static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var symbol in value)
        {
            builder.Append(char.IsLetterOrDigit(symbol) ? char.ToLowerInvariant(symbol) : '_');
        }

        return builder.ToString().Trim('_');
    }

    public static string BuildTranslationTuple(DccTransformData transform)
    {
        return $"({FormatDouble(transform.Translation.X)}, {FormatDouble(transform.Translation.Y)}, {FormatDouble(transform.Translation.Z)})";
    }

    public static string BuildQuaternionTuple(DccTransformData transform)
    {
        return $"({FormatDouble(transform.Rotation.W)}, {FormatDouble(transform.Rotation.X)}, {FormatDouble(transform.Rotation.Y)}, {FormatDouble(transform.Rotation.Z)})";
    }

    public static string BuildScaleTuple(DccTransformData transform)
    {
        return $"({FormatDouble(transform.Scale.X)}, {FormatDouble(transform.Scale.Y)}, {FormatDouble(transform.Scale.Z)})";
    }

    public static string GetBlenderInterpolationMode(DccKeyframeInterpolationMode interpolationMode)
    {
        return interpolationMode switch
        {
            DccKeyframeInterpolationMode.Bezier => "BEZIER",
            DccKeyframeInterpolationMode.Linear => "LINEAR",
            DccKeyframeInterpolationMode.Constant => "CONSTANT",
            _ => throw new ArgumentOutOfRangeException(nameof(interpolationMode), interpolationMode, null)
        };
    }

    public static string GetBlenderLightType(DccLightKind lightKind)
    {
        return lightKind switch
        {
            DccLightKind.Point => "POINT",
            DccLightKind.Sun => "SUN",
            DccLightKind.Spot => "SPOT",
            DccLightKind.Area => "AREA",
            _ => throw new ArgumentOutOfRangeException(nameof(lightKind), lightKind, null)
        };
    }

    #endregion
}
