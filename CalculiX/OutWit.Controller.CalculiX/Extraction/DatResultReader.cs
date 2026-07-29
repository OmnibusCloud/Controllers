using System.Globalization;
using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Tolerant reader of the ccx .dat blocks the response set needs:
/// eigenvalue output (frequency in cycles), buckling factors, and
/// "forces (fx,fy,fz) for set …" reaction-force blocks, summed per set.
/// </summary>
public static partial class DatResultReader
{
    #region Functions

    /// <summary>
    /// Reads the supported blocks of the file.
    /// </summary>
    /// <param name="path">Path of the .dat file.</param>
    /// <returns>The extracted content; empty for an empty or alien file.</returns>
    public static DatResultFile Read(string path)
    {
        var result = new DatResultFile();
        var mode = Section.None;
        DatForceSum? forces = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Contains("E I G E N V A L U E   O U T P U T", StringComparison.Ordinal))
            {
                mode = Section.Eigenvalues;
                continue;
            }

            if (line.Contains("B U C K L I N G   F A C T O R   O U T P U T", StringComparison.Ordinal))
            {
                mode = Section.Buckling;
                continue;
            }

            var forcesHeader = ForcesHeaderRegex().Match(line);
            if (forcesHeader.Success)
            {
                mode = Section.Forces;
                forces = new DatForceSum(forcesHeader.Groups[1].Value);
                result.ForceSums.Add(forces);
                continue;
            }

            var numbers = NumberRegex().Matches(line);
            switch (mode)
            {
                // MODE NO | EIGENVALUE | REAL PART (rad) | CYCLES | IMAGINARY:
                // the frequency the row reports is the fourth column.
                case Section.Eigenvalues when numbers.Count >= 4 && IsModeRow(line):
                    result.Eigenfrequencies.Add(Parse(numbers[3].Value));
                    break;

                case Section.Buckling when numbers.Count >= 2 && IsModeRow(line):
                    result.BucklingFactors.Add(Parse(numbers[1].Value));
                    break;

                case Section.Forces when forces != null && numbers.Count >= 4 && IsModeRow(line):
                    forces.Fx += Parse(numbers[1].Value);
                    forces.Fy += Parse(numbers[2].Value);
                    forces.Fz += Parse(numbers[3].Value);
                    break;

                default:
                    // A non-numeric line inside forces ends the block (the
                    // .dat interleaves headers between steps).
                    if (mode == Section.Forces && line.Trim().Length > 0 && numbers.Count == 0)
                        mode = Section.None;
                    break;
            }
        }

        return result;
    }

    private static bool IsModeRow(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
    }

    private static double Parse(string token)
    {
        return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"forces \(fx,fy,fz\) for set (\S+) and time")]
    private static partial Regex ForcesHeaderRegex();

    [GeneratedRegex(@"[-+]?\d+\.?\d*(?:[Ee][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    #endregion

    #region Nested Types

    private enum Section
    {
        None = 0,
        Eigenvalues = 1,
        Buckling = 2,
        Forces = 3
    }

    #endregion
}
