using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Extracts the response set from a finished solve's result files, on the
/// node, right after ccx exits. Automatic responses come from whatever the
/// files carry (temperatures, displacements, stresses, eigenfrequencies,
/// buckling factors, reaction-force sums), per step; probes evaluate
/// aggregate × quantity × named node set on top. Numbers, not files, travel
/// back — the artifacts stay behind as blobs. The .frd is streamed, never
/// held; what cannot be read or must not be reported comes back as a note,
/// and an extraction that fails as a whole comes back without a row and with
/// an error note, never as an empty row that passes for a result.
/// </summary>
public static class CcxResponseExtractor
{
    #region Constants

    public const string QUANTITY_TEMPERATURE = "temp";

    public const string QUANTITY_VON_MISES = "von_mises";

    public const string QUANTITY_DISPLACEMENT_MAGNITUDE = "disp_magnitude";

    #endregion

    #region Functions

    /// <summary>
    /// Extracts the automatic response set of the analysis plus the requested
    /// probes. Missing files, alien content or unresolvable probes shrink the
    /// row and leave a note; a reader that throws (a file that cannot be read,
    /// memory that runs out) fails the extraction, with the reason in its notes.
    /// </summary>
    /// <param name="deckPath">Path of the solved deck (probe sets, *TRANSFORM frames and steps resolve against it); null skips them.</param>
    /// <param name="frdPath">Path of the .frd result file; null when the solve produced none.</param>
    /// <param name="datPath">Path of the .dat result file; null when the solve produced none.</param>
    /// <param name="request">Requested probes; null = automatic set only.</param>
    /// <returns>The extracted row and notes, or the failure.</returns>
    public static CcxExtraction Extract(string? deckPath, string? frdPath, string? datPath, CcxExtractionRequestData? request)
    {
        var notes = new List<CcxNote>();
        var sink = new CcxResponseSink();
        DeckFacts? facts = null;

        if (deckPath != null)
        {
            try
            {
                facts = DeckFactsReader.Read(deckPath);
                sink.DeclareStep(facts.Steps.Count);
            }
            catch (Exception e)
            {
                sink.Note(CcxNoteKind.Warning, 0, null,
                    $"the deck could not be read ({e.Message}): probes, *TRANSFORM frames and the step count are not resolved");
            }
        }

        try
        {
            if (frdPath != null)
            {
                var accumulator = new FrdResponseAccumulator(facts, facts == null ? null : deckPath, request?.Probes ?? [], sink);
                FrdResultReader.Read(frdPath, accumulator);
                accumulator.Finish();
            }

            if (datPath != null)
                DatResponseExtractor.Add(DatResultReader.Read(datPath), facts, facts == null ? null : deckPath, sink);
        }
        catch (Exception e)
        {
            sink.Emit(new CcxResponseRowData(), notes);
            return CcxExtraction.Failure($"response extraction failed: {e.GetType().Name}: {e.Message}", notes);
        }

        var row = new CcxResponseRowData();
        sink.Emit(row, notes);
        return new CcxExtraction(row, notes);
    }

    #endregion
}
