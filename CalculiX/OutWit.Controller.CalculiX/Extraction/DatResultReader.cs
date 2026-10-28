using System.Globalization;
using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Tolerant reader of the ccx .dat blocks the response set needs, kept per
/// step (ccx heads every step with "S T E P n"): eigenvalue tables in each
/// of ccx's four layouts (plain, cyclic symmetry with a nodal-diameter
/// column, complex without an eigenvalue column, and both), buckling
/// factors, and "forces (fx,fy,fz) for set …" blocks with their rows, the
/// local-frame mark of each row and the global total of <c>TOTALS=YES</c>.
/// A block ends at the first non-blank line after its rows that is not a
/// row itself: ccx follows the eigenvalue table with its participation
/// factors and effective modal masses, and a forces block with the next
/// print (displacements, stresses) — rows of the same shape that must not
/// be read as more frequencies or more reaction forces. The forces ccx
/// prints per mode after an eigenvalue solve ("E I G E N V A L U E
/// N U M B E R n") are mode shapes, not reactions, and are counted, not read.
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
        return Read(File.ReadLines(path));
    }

    /// <summary>
    /// Reads the supported blocks of .dat text.
    /// </summary>
    /// <param name="lines">The file, line by line.</param>
    /// <returns>The extracted content; empty for empty or alien text.</returns>
    public static DatResultFile Read(IEnumerable<string> lines)
    {
        var result = new DatResultFile();
        var step = result.Step(1);
        var mode = Section.None;
        var hasRows = false;
        var modePrints = false;
        var layout = EigenvalueLayout.Unknown;
        DatForceBlock? forces = null;

        foreach (var line in lines)
        {
            var stepHeader = StepHeaderRegex().Match(line);
            if (stepHeader.Success)
            {
                step = result.Step(int.Parse(stepHeader.Groups[1].Value, CultureInfo.InvariantCulture));
                mode = Section.None;
                modePrints = false;
                continue;
            }

            if (line.Contains("E I G E N V A L U E   O U T P U T", StringComparison.Ordinal))
            {
                mode = Section.EigenvalueHeader;
                hasRows = false;
                continue;
            }

            if (line.Contains("E I G E N V A L U E    N U M B E R", StringComparison.Ordinal))
            {
                mode = Section.None;
                modePrints = true;
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
                hasRows = false;
                if (modePrints)
                {
                    mode = Section.Skipped;
                    step.ModeShapeForceBlocks++;
                    continue;
                }

                mode = Section.Forces;
                forces = new DatForceBlock(forcesHeader.Groups[1].Value, ParseTime(forcesHeader.Groups[2].Value));
                step.AddForces(forces);
                continue;
            }

            var totalsHeader = TotalsHeaderRegex().Match(line);
            if (totalsHeader.Success)
            {
                hasRows = false;
                mode = Section.Totals;
                if (modePrints)
                {
                    mode = Section.Skipped;
                    continue;
                }

                // TOTALS=ONLY prints the total without a rows block before it.
                if (forces == null || !forces.SetName.Equals(totalsHeader.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                {
                    forces = new DatForceBlock(totalsHeader.Groups[1].Value, ParseTime(totalsHeader.Groups[2].Value));
                    step.AddForces(forces);
                }

                continue;
            }

            var numbers = NumberRegex().Matches(line);
            switch (mode)
            {
                // The first non-blank line after the title names the columns;
                // the frequency in cycles sits after MODE NO, the nodal
                // diameter before it (cyclic symmetry) and the eigenvalue
                // after it (real eigenproblems) - two columns that come and go.
                case Section.EigenvalueHeader when line.Trim().Length > 0:
                    layout = ReadLayout(line);
                    mode = layout == EigenvalueLayout.Unknown ? Section.None : Section.Eigenvalues;
                    if (layout == EigenvalueLayout.Unknown)
                        step.UnknownEigenvalueTables++;
                    break;

                case Section.Eigenvalues when IsRow(line) && numbers.Count > CyclesIndex(layout):
                    step.Eigenfrequencies.Add(new DatEigenfrequency(
                        (int)Parse(numbers[HasNodalDiameter(layout) ? 1 : 0].Value),
                        HasNodalDiameter(layout) ? (int)Parse(numbers[0].Value) : null,
                        Parse(numbers[CyclesIndex(layout)].Value)));
                    hasRows = true;
                    break;

                case Section.Buckling when numbers.Count >= 2 && IsRow(line):
                    step.BucklingFactors.Add(Parse(numbers[1].Value));
                    hasRows = true;
                    break;

                case Section.Forces when forces != null && numbers.Count >= 4 && IsRow(line):
                    forces.Rows.Add(new DatForceRow(
                        (int)Parse(numbers[0].Value),
                        Parse(numbers[1].Value),
                        Parse(numbers[2].Value),
                        Parse(numbers[3].Value),
                        line.TrimEnd().EndsWith('L')));
                    hasRows = true;
                    break;

                // The total row has no node id and may open with a minus sign.
                case Section.Totals when forces != null && numbers.Count >= 3 && line.Trim().Length > 0:
                    forces.Total = [Parse(numbers[0].Value), Parse(numbers[1].Value), Parse(numbers[2].Value)];
                    mode = Section.None;
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

    private static EigenvalueLayout ReadLayout(string header)
    {
        if (!header.Contains("MODE NO", StringComparison.Ordinal))
            return EigenvalueLayout.Unknown;

        var nodal = header.Contains("NODAL", StringComparison.Ordinal);
        var eigenvalue = header.Contains("EIGENVALUE", StringComparison.Ordinal);
        return (nodal, eigenvalue) switch
        {
            (false, true) => EigenvalueLayout.Plain,
            (true, true) => EigenvalueLayout.CyclicSymmetry,
            (false, false) => EigenvalueLayout.Complex,
            (true, false) => EigenvalueLayout.CyclicSymmetryComplex
        };
    }

    private static bool HasNodalDiameter(EigenvalueLayout layout)
    {
        return layout is EigenvalueLayout.CyclicSymmetry or EigenvalueLayout.CyclicSymmetryComplex;
    }

    // Column of the frequency in cycles among the row's numbers (writeev.f,
    // writeevcs.f, writeevcomplex.f, writeevcscomplex.f): mode number and the
    // real part in rad/time always precede it; the nodal diameter and the
    // eigenvalue each add one column when their layout has them.
    private static int CyclesIndex(EigenvalueLayout layout)
    {
        return layout switch
        {
            EigenvalueLayout.Plain => 3,
            EigenvalueLayout.CyclicSymmetry => 4,
            EigenvalueLayout.Complex => 2,
            EigenvalueLayout.CyclicSymmetryComplex => 3,
            _ => int.MaxValue
        };
    }

    private static bool IsRow(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
    }

    private static double Parse(string token)
    {
        return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static double ParseTime(string token)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ? time : 0.0;
    }

    [GeneratedRegex(@"^\s*S T E P\s+(\d+)")]
    private static partial Regex StepHeaderRegex();

    [GeneratedRegex(@"^ forces \(fx,fy,fz\) for set (\S+) and time\s+(\S+)")]
    private static partial Regex ForcesHeaderRegex();

    [GeneratedRegex(@"^ total force \(fx,fy,fz\) for set (\S+) and time\s+(\S+)")]
    private static partial Regex TotalsHeaderRegex();

    [GeneratedRegex(@"[-+]?\d+\.?\d*(?:[Ee][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    #endregion

    #region Nested Types

    private enum Section
    {
        None = 0,
        EigenvalueHeader = 1,
        Eigenvalues = 2,
        Buckling = 3,
        Forces = 4,
        Totals = 5,
        Skipped = 6
    }

    private enum EigenvalueLayout
    {
        Unknown = 0,
        Plain = 1,
        CyclicSymmetry = 2,
        Complex = 3,
        CyclicSymmetryComplex = 4
    }

    #endregion
}
