using System.Globalization;
using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Tolerant reader of the ccx .dat blocks the response set needs:
/// eigenvalue output (frequency in cycles), buckling factors, and
/// "forces (fx,fy,fz) for set …" reaction-force blocks, summed per set.
/// A block ends at the first non-blank line after its rows that is not a
/// row itself: ccx follows the eigenvalue table with its participation
/// factors and effective modal masses, and a forces block with the next
/// print (displacements, stresses) — rows of the same shape that must not
/// be read as more frequencies or more reaction forces.
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
        var hasRows = false;
        DatForceSum? forces = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.Contains("E I G E N V A L U E   O U T P U T", StringComparison.Ordinal))
            {
                mode = Section.Eigenvalues;
                hasRows = false;
                continue;
            }

            if (line.Contains("B U C K L I N G   F A C T O R   O U T P U T", StringComparison.Ordinal))
            {
                mode = Section.Buckling;
                hasRows = false;
                continue;
            }

            var forcesHeader = ForcesHeaderRegex().Match(line);
            if (forcesHeader.Success)
            {
                mode = Section.Forces;
                hasRows = false;
                forces = new DatForceSum(forcesHeader.Groups[1].Value);

                // ccx prints one forces block per increment/step (the header
                // carries the time for exactly that reason) — the response is
                // the FINAL state, so a later block for the same set REPLACES
                // the earlier one; appending would emit duplicate response
                // names carrying partial-load values.
                var earlier = result.ForceSums.FindIndex(
                    sum => sum.SetName.Equals(forces.SetName, StringComparison.OrdinalIgnoreCase));
                if (earlier >= 0)
                    result.ForceSums[earlier] = forces;
                else
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
                    hasRows = true;
                    break;

                case Section.Buckling when numbers.Count >= 2 && IsModeRow(line):
                    result.BucklingFactors.Add(Parse(numbers[1].Value));
                    hasRows = true;
                    break;

                case Section.Forces when forces != null && numbers.Count >= 4 && IsModeRow(line):
                    forces.Fx += Parse(numbers[1].Value);
                    forces.Fy += Parse(numbers[2].Value);
                    forces.Fz += Parse(numbers[3].Value);
                    hasRows = true;
                    break;

                default:
                    // Past its rows, the first line that is not a row ends the
                    // block: the next table's header ("P A R T I C I P A T I O N
                    // F A C T O R S", "displacements (vx,vy,vz) for set …",
                    // "INCREMENT 2"). Before the rows, the block's own column
                    // headers are skipped.
                    if (hasRows && line.Trim().Length > 0)
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
