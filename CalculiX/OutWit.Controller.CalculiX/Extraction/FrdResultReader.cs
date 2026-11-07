using System.Globalization;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Streaming fixed-column reader of ccx ASCII .frd files. Nothing is held:
/// each line is parsed and handed to the visitor, so the memory a read
/// takes is the visitor's own and not the file's (a 312 MB .frd of 300 000
/// nodes and 40 temperature blocks held every block in memory before, at
/// 1.65 GB peak). Geometry nodes go to a visitor that asks for them;
/// element sections are skipped; a malformed line is ignored rather than
/// thrown on, a value that is not a finite number marks its row broken -
/// extraction is a bonus on top of a finished solve and must never turn a
/// good solve into a failure, nor a blown-up solve into a number.
/// </summary>
public static class FrdResultReader
{
    #region Constants

    private const int VALUE_WIDTH = 12;

    private const int VALUES_OFFSET = 13;

    private const int MAX_VALUES_PER_LINE = 6;

    #endregion

    #region Functions

    /// <summary>
    /// Reads every nodal result block of the file into memory - for small
    /// files and tests; the extractor streams through <see cref="Read(string, IFrdResultVisitor)"/>.
    /// </summary>
    /// <param name="path">Path of the .frd file.</param>
    /// <returns>Blocks in file order; empty when the file carries none.</returns>
    public static List<FrdResultBlock> Read(string path)
    {
        var collector = new FrdBlockCollector();
        Read(path, collector);
        return collector.Blocks;
    }

    /// <summary>
    /// Streams the file through a visitor.
    /// </summary>
    /// <param name="path">Path of the .frd file.</param>
    /// <param name="visitor">Receives nodes, blocks and rows as they are read.</param>
    public static void Read(string path, IFrdResultVisitor visitor)
    {
        Read(File.ReadLines(path), visitor);
    }

    /// <summary>
    /// Streams .frd text through a visitor.
    /// </summary>
    /// <param name="lines">The file, line by line.</param>
    /// <param name="visitor">Receives nodes, blocks and rows as they are read.</param>
    public static void Read(IEnumerable<string> lines, IFrdResultVisitor visitor)
    {
        var step = 1;
        var increment = 1;
        var mode = 0;
        var time = 0.0;
        var analysisType = ' ';
        var section = Section.None;
        FrdBlockHeader? header = null;
        var begun = false;
        double[] values = [];
        var filled = 0;
        var node = 0;
        string? broken = null;

        foreach (var line in lines)
        {
            if (line.StartsWith(" -1", StringComparison.Ordinal))
            {
                if (section == Section.Geometry)
                {
                    if (visitor.WantsGeometry && line.Length > VALUES_OFFSET && TryNode(line, out var id))
                    {
                        var point = new double[3];
                        if (ParseValues(line, point, 0, out _) == 3)
                            visitor.Node(id, point[0], point[1], point[2]);
                    }
                }
                else if (section == Section.Results && header != null && line.Length > VALUES_OFFSET && TryNode(line, out node))
                {
                    if (!begun)
                    {
                        begun = true;
                        values = new double[header.Components.Count];
                        visitor.BeginBlock(header);
                    }

                    filled = ParseValues(line, values, 0, out broken);
                    Flush(visitor, header, values, ref filled, node, ref broken);
                }
            }
            else if (line.StartsWith(" -2", StringComparison.Ordinal))
            {
                if (section == Section.Results && begun && filled > 0 && line.Length > VALUES_OFFSET)
                {
                    var read = ParseValues(line, values, filled, out var brokenHere);
                    broken ??= brokenHere;
                    filled += read;
                    Flush(visitor, header!, values, ref filled, node, ref broken);
                }
            }
            else if (line.StartsWith(" -3", StringComparison.Ordinal))
            {
                if (begun)
                    visitor.EndBlock();

                section = Section.None;
                header = null;
                begun = false;
                filled = 0;
            }
            else if (line.StartsWith(" -4", StringComparison.Ordinal) && line.Length > 5)
            {
                header = new FrdBlockHeader(Slice(line, 5, 8).Trim(), step, increment, time, analysisType, mode);
                section = Section.Results;
                begun = false;
                filled = 0;
            }
            else if (line.StartsWith(" -5", StringComparison.Ordinal) && header != null && !begun)
            {
                // Calculated components (the DISP "ALL" magnitude) carry no
                // stored column - their -5 record is flagged and skipped.
                var component = Slice(line, 5, 8).Trim();
                if (component.Length > 0 && component != "ALL")
                    header.Components.Add(component);
            }
            else if (line.StartsWith("    1PSTEP", StringComparison.Ordinal))
            {
                increment = ParseInt(line, 36);
                step = System.Math.Max(1, ParseInt(line, 48));
                mode = 0;
            }
            else if (line.StartsWith("    1PMODE", StringComparison.Ordinal))
            {
                mode = ParseInt(line, 24);
            }
            else if (line.StartsWith("  100CL", StringComparison.Ordinal) && line.Length >= 24)
            {
                double.TryParse(line.AsSpan(12, 12), NumberStyles.Float, CultureInfo.InvariantCulture, out time);
                analysisType = line.Length > 57 ? line[57] : ' ';
            }
            else if (line.StartsWith("    2C", StringComparison.Ordinal))
            {
                section = Section.Geometry;
            }
            else if (line.StartsWith("    3C", StringComparison.Ordinal))
            {
                section = Section.Elements;
            }
        }

        if (begun)
            visitor.EndBlock();
    }

    // A row is complete when every stored component has arrived; a row cut
    // short by the next -1 is dropped (the next -1 overwrites the buffer).
    private static void Flush(IFrdResultVisitor visitor, FrdBlockHeader header, double[] values, ref int filled, int node, ref string? broken)
    {
        if (filled < header.Components.Count)
            return;

        if (broken != null)
            visitor.BrokenRow(node, broken);
        else
            visitor.Row(node, values);

        filled = 0;
        broken = null;
    }

    private static bool TryNode(string line, out int node)
    {
        return int.TryParse(line.AsSpan(3, 10), NumberStyles.Integer | NumberStyles.AllowLeadingWhite, CultureInfo.InvariantCulture, out node);
    }

    // Fills the buffer from the line's 12-wide fields starting at the given
    // slot; a field that is not a finite number is reported in 'broken'
    // (the first one) and leaves its slot untouched. Returns the fields read.
    private static int ParseValues(string line, double[] buffer, int offset, out string? broken)
    {
        broken = null;
        var count = System.Math.Min((line.Length - VALUES_OFFSET) / VALUE_WIDTH, MAX_VALUES_PER_LINE);
        var read = 0;

        for (var i = 0; i < count && offset + i < buffer.Length; i++)
        {
            var field = line.AsSpan(VALUES_OFFSET + i * VALUE_WIDTH, VALUE_WIDTH);
            if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                buffer[offset + i] = value;
            else
                broken ??= field.Trim().ToString();

            read++;
        }

        return read;
    }

    private static int ParseInt(string line, int start)
    {
        if (line.Length <= start)
            return 0;

        var span = line.AsSpan(start, System.Math.Min(12, line.Length - start));
        return int.TryParse(span, NumberStyles.Integer | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string Slice(string line, int start, int length)
    {
        return line.Substring(start, System.Math.Min(length, line.Length - start));
    }

    #endregion

    #region Nested Types

    private enum Section
    {
        None = 0,
        Geometry = 1,
        Elements = 2,
        Results = 3
    }

    #endregion
}
