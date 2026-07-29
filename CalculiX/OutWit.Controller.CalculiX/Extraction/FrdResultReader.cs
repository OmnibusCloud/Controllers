using System.Globalization;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Tolerant fixed-column reader of ccx ASCII .frd nodal result blocks.
/// Geometry and element sections are skipped; malformed lines are ignored
/// rather than thrown on — extraction is a bonus on top of a finished solve
/// and must never turn a good solve into a failure.
/// </summary>
public static class FrdResultReader
{
    #region Constants

    private const int VALUE_WIDTH = 12;

    private const int VALUES_OFFSET = 13;

    #endregion

    #region Functions

    /// <summary>
    /// Reads every nodal result block of the file.
    /// </summary>
    /// <param name="path">Path of the .frd file.</param>
    /// <returns>Blocks in file order; empty when the file carries none.</returns>
    public static List<FrdResultBlock> Read(string path)
    {
        var blocks = new List<FrdResultBlock>();
        FrdResultBlock? current = null;
        var stepTime = 0.0;
        var lastNode = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("  100CL", StringComparison.Ordinal) && line.Length >= 24)
            {
                double.TryParse(line.AsSpan(12, 12), NumberStyles.Float, CultureInfo.InvariantCulture, out stepTime);
            }
            else if (line.StartsWith(" -4", StringComparison.Ordinal) && line.Length > 5)
            {
                current = new FrdResultBlock(Slice(line, 5, 8).Trim(), stepTime);
                blocks.Add(current);
            }
            else if (line.StartsWith(" -5", StringComparison.Ordinal) && current != null)
            {
                // Calculated components (the DISP "ALL" magnitude) carry no
                // stored column — their -5 record is flagged and skipped.
                var component = Slice(line, 5, 8).Trim();
                if (component.Length > 0 && component != "ALL")
                    current.Components.Add(component);
            }
            else if (line.StartsWith(" -1", StringComparison.Ordinal) && current != null && line.Length > VALUES_OFFSET)
            {
                if (int.TryParse(line.AsSpan(3, 10), NumberStyles.Integer | NumberStyles.AllowLeadingWhite, CultureInfo.InvariantCulture, out var node))
                {
                    current.Values[node] = ParseValues(line);
                    lastNode = node;
                }
            }
            else if (line.StartsWith(" -2", StringComparison.Ordinal) && current != null && line.Length > VALUES_OFFSET
                     && current.Values.TryGetValue(lastNode, out var head))
            {
                current.Values[lastNode] = [.. head, .. ParseValues(line)];
            }
            else if (line.StartsWith(" -3", StringComparison.Ordinal))
            {
                current = null;
            }
        }

        // Geometry (2C) sections also use -1 lines; they are excluded by the
        // reader keying everything on an open -4 dataset. A block that ended
        // up with no components (a 2C leak or a corrupt header) is dropped.
        blocks.RemoveAll(block => block.Components.Count == 0 || block.Values.Count == 0);
        return blocks;
    }

    private static double[] ParseValues(string line)
    {
        var count = (line.Length - VALUES_OFFSET) / VALUE_WIDTH;
        var values = new double[count];

        for (var i = 0; i < count; i++)
        {
            double.TryParse(
                line.AsSpan(VALUES_OFFSET + i * VALUE_WIDTH, VALUE_WIDTH),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out values[i]);
        }

        return values;
    }

    private static string Slice(string line, int start, int length)
    {
        return line.Substring(start, System.Math.Min(length, line.Length - start));
    }

    #endregion
}
