using System.Text;
using System.Text.RegularExpressions;
using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Adds to a deck that writes <c>OUTPUT=2D</c> the output the shell and beam
/// stress is recovered from (<see cref="CcxSectionStressPlan"/>), before the
/// solve, and takes the shell prints out of the .dat after the extraction:
/// <list type="bullet">
/// <item>beams: an <c>*EL FILE, OUTPUT=2D, SECTION FORCES</c> card without
/// labels at the end of step 1 (ccx refuses SECTION FORCES while OUTPUT=3D
/// holds, and keeps it for every later step);</item>
/// <item>shells: one surface per element edge before the first step, and one
/// <c>*SECTION PRINT</c> (SOF) per surface in every <c>*STATIC</c> step that
/// does not inherit them from the step before (print requests carry over
/// into the next step); a step of another procedure that would inherit them
/// switches them off with a <c>FREQUENCYF=0</c> print, which removes the
/// section prints in force and adds none - a frequency step would print
/// every edge for every mode.</item>
/// </list>
/// Nothing is added unless every shell and beam of the deck can be recovered
/// (<see cref="CcxSectionStressPlan.RecoversAll"/>): the von Mises columns
/// cover the whole model. Each addition leaves an info note in the variant.
/// </summary>
public static partial class CcxSectionOutput
{
    #region Constants

    [GeneratedRegex(@"^ statistics for surface set WITEDGE\d+ and time", RegexOptions.CultureInvariant)]
    private static partial Regex EdgeBlockRegex();

    [GeneratedRegex(@"[-+]?\d+\.?\d*(?:[Ee][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^\s*S T E P\s+\d+\s*$")]
    private static partial Regex StepHeaderRegex();

    [GeneratedRegex(@"^\s*INCREMENT\s+\d+\s*$")]
    private static partial Regex IncrementHeaderRegex();

    private const int EDGE_BLOCK_NUMBER_LINES = 4;

    #endregion

    #region Functions

    /// <summary>
    /// Rewrites the deck in place with the output its shell and beam stress needs.
    /// </summary>
    /// <param name="deckPath">The deck ccx will read.</param>
    /// <param name="shellPrints">True when shell edge prints were added: the .dat then wants <see cref="StripDat"/>.</param>
    /// <returns>One info note per addition; empty when the deck was left as it was.</returns>
    public static List<CcxNote> Apply(string deckPath, out bool shellPrints)
    {
        var lines = File.ReadAllLines(deckPath);
        var rewritten = Rewrite(lines, out var notes, out shellPrints);
        if (notes.Count > 0)
            File.WriteAllLines(deckPath, rewritten);

        return notes;
    }

    /// <summary>
    /// Rewrites deck text with the output its shell and beam stress needs.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <param name="notes">Receives one info note per addition.</param>
    /// <param name="shellPrints">True when shell edge prints were added.</param>
    /// <returns>The deck with the additions; the same lines when none were needed.</returns>
    public static List<string> Rewrite(IReadOnlyList<string> lines, out List<CcxNote> notes, out bool shellPrints)
    {
        notes = [];
        shellPrints = false;

        var facts = DeckFactsReader.Read(lines);
        if (!facts.OutputIs2D || !facts.HasShellOrBeamElements)
            return [.. lines];

        var structure = DeckStructureReader.Read(lines);
        var plan = CcxSectionStressPlan.For(facts, structure);
        var (firstStep, stepEnds) = StepLines(lines);
        if (!plan.RecoversAll || firstStep < 0 || stepEnds.Count == 0 || stepEnds.Count != facts.Steps.Count)
            return [.. lines];

        var insertions = new Dictionary<int, List<string>>();

        if (plan.HasBeams && !structure.SectionForces)
        {
            Insert(insertions, stepEnds[0], "*EL FILE, OUTPUT=2D, SECTION FORCES");
            notes.Add(new CcxNote
            {
                Kind = CcxNoteKind.Info,
                Text = "*EL FILE, OUTPUT=2D, SECTION FORCES was added to step 1, so the .frd STRESS block carries the section forces at the beam nodes in place of the stress (SXX and SYY the shear forces in the 1- and 2-direction, SZZ the axial force, SXY the torque, SYZ and SZX the moments about the 1- and 2-direction), from which the beam von Mises is computed"
            });
        }

        if (plan.HasShells && structure.EdgeSurfaces.Count == 0)
        {
            var surfaces = new List<string>(plan.ShellEdges.Count * 2);
            var prints = new List<string>(plan.ShellEdges.Count * 2);
            for (var k = 0; k < plan.ShellEdges.Count; k++)
            {
                var (element, face) = plan.ShellEdges[k];
                surfaces.Add($"*SURFACE, NAME={CcxSectionStressPlan.EDGE_SURFACE_PREFIX}{k + 1}, TYPE=ELEMENT");
                surfaces.Add($"{element}, S{face}");
                prints.Add($"*SECTION PRINT, SURFACE={CcxSectionStressPlan.EDGE_SURFACE_PREFIX}{k + 1}, NAME={CcxSectionStressPlan.EDGE_PRINT_PREFIX}{k + 1}");
                prints.Add("SOF");
            }

            Insert(insertions, firstStep, [.. surfaces]);

            var active = false;
            for (var i = 0; i < facts.Steps.Count; i++)
            {
                var isStatic = facts.Steps[i].Procedure == "STATIC";
                if (isStatic && !active)
                    Insert(insertions, stepEnds[i], [.. prints]);
                else if (!isStatic && active)
                    Insert(insertions, stepEnds[i],
                        $"*SECTION PRINT, SURFACE={CcxSectionStressPlan.EDGE_SURFACE_PREFIX}1, NAME={CcxSectionStressPlan.EDGE_PRINT_PREFIX}OFF{i + 1}, FREQUENCYF=0",
                        "SOF");

                active = isStatic;
            }

            shellPrints = true;
            notes.Add(new CcxNote
            {
                Kind = CcxNoteKind.Info,
                Text = $"{plan.ShellEdges.Count} *SECTION PRINT requests, one per shell element edge, were added to the deck to recover the shell von Mises from the forces and moments they print; their blocks are taken out of the .dat after the extraction"
            });
        }

        if (insertions.Count == 0)
            return [.. lines];

        var result = new List<string>(lines.Count + insertions.Values.Sum(block => block.Count));
        for (var i = 0; i < lines.Count; i++)
        {
            if (insertions.TryGetValue(i, out var block))
                result.AddRange(block);

            result.Add(lines[i]);
        }

        return result;
    }

    /// <summary>
    /// Takes the blocks of the controller's edge prints out of a .dat, in
    /// place, with the step and increment headers ccx wrote for them alone:
    /// a deck that printed nothing else gets its empty .dat back.
    /// </summary>
    /// <param name="datPath">The .dat of the solve.</param>
    /// <returns>The number of blocks removed.</returns>
    public static int StripDat(string datPath)
    {
        var newline = DetectNewline(datPath);
        var stripped = datPath + ".strip";
        var removed = 0;

        using (var reader = new StreamReader(datPath))
        using (var writer = new StreamWriter(stripped, false, new UTF8Encoding(false)))
        {
            writer.NewLine = newline;

            // Blank lines and headers wait until something of the deck's own
            // follows them; a header followed by nothing but edge prints goes.
            var pending = new List<string>();
            var skipping = 0;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                if (skipping > 0)
                {
                    if (NumberRegex().Matches(line).Count >= 3)
                        skipping--;

                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    pending.Add(line);
                    continue;
                }

                if (EdgeBlockRegex().IsMatch(line))
                {
                    // ccx writes three blank lines before a block.
                    for (var blank = 0; blank < 3 && pending.Count > 0 && pending[^1].Trim().Length == 0; blank++)
                        pending.RemoveAt(pending.Count - 1);

                    skipping = EDGE_BLOCK_NUMBER_LINES;
                    removed++;
                    continue;
                }

                if (StepHeaderRegex().IsMatch(line))
                {
                    // An earlier step's headers with nothing after them go;
                    // the blank lines before them stay.
                    var earlier = pending.FindIndex(held => held.Trim().Length > 0);
                    if (earlier >= 0)
                        pending.RemoveRange(earlier, pending.Count - earlier);

                    pending.Add(line);
                    continue;
                }

                if (IncrementHeaderRegex().IsMatch(line))
                {
                    var earlier = pending.FindLastIndex(IncrementHeaderRegex().IsMatch);
                    if (earlier >= 0)
                        pending.RemoveRange(earlier, pending.Count - earlier);

                    pending.Add(line);
                    continue;
                }

                foreach (var held in pending)
                    writer.WriteLine(held);

                pending.Clear();
                writer.WriteLine(line);
            }

            if (pending.All(held => held.Trim().Length == 0))
            {
                foreach (var held in pending)
                    writer.WriteLine(held);
            }
        }

        // Nothing of the deck's own: the .dat is as empty as ccx leaves it
        // for a deck without prints.
        if (new FileInfo(stripped).Length > 0 && File.ReadLines(stripped).All(held => held.Trim().Length == 0))
            File.WriteAllText(stripped, string.Empty);

        File.Move(stripped, datPath, true);
        return removed;
    }

    private static (int FirstStep, List<int> StepEnds) StepLines(IReadOnlyList<string> lines)
    {
        var firstStep = -1;
        var stepEnds = new List<int>();
        var open = false;

        foreach (var line in DeckCardReader.Read(lines))
        {
            if (!line.IsKeyword)
                continue;

            if (line.Card.Is("STEP"))
            {
                if (firstStep < 0)
                    firstStep = line.Card.LineIndex;

                open = true;
            }
            else if (line.Card.Is("ENDSTEP") && open)
            {
                stepEnds.Add(line.Card.LineIndex);
                open = false;
            }
        }

        return (firstStep, stepEnds);
    }

    private static void Insert(Dictionary<int, List<string>> insertions, int index, params string[] block)
    {
        if (!insertions.TryGetValue(index, out var lines))
            insertions[index] = lines = [];

        lines.AddRange(block);
    }

    private static string DetectNewline(string path)
    {
        using var stream = File.OpenRead(path);
        int previous = -1, current;
        while ((current = stream.ReadByte()) >= 0)
        {
            if (current == '\n')
                return previous == '\r' ? "\r\n" : "\n";

            previous = current;
        }

        return Environment.NewLine;
    }

    #endregion
}
