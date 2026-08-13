using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Extracts the response set from a finished solve's result files, on the
/// node, right after ccx exits. Automatic responses come from whatever the
/// files carry (temperatures, displacements, stresses, eigenfrequencies,
/// buckling factors, reaction-force sums); probes evaluate aggregate ×
/// quantity × named node set on top. Numbers, not files, travel back —
/// the artifacts stay behind as blobs.
/// </summary>
public static class CcxResponseExtractor
{
    #region Constants

    private const string DATASET_TEMPERATURE = "NDTEMP";

    private const string DATASET_DISPLACEMENT = "DISP";

    private const string DATASET_STRESS = "STRESS";

    public const string QUANTITY_TEMPERATURE = "temp";

    public const string QUANTITY_VON_MISES = "von_mises";

    public const string QUANTITY_DISPLACEMENT_MAGNITUDE = "disp_magnitude";

    #endregion

    #region Functions

    /// <summary>
    /// Extracts the automatic response set of the analysis plus the requested
    /// probes. Missing files, alien content or unresolvable probes shrink the
    /// row — extraction never throws over a finished solve.
    /// </summary>
    /// <param name="deckPath">Path of the solved deck (probe sets resolve against it); null skips probes.</param>
    /// <param name="frdPath">Path of the .frd result file; null when the solve produced none.</param>
    /// <param name="datPath">Path of the .dat result file; null when the solve produced none.</param>
    /// <param name="request">Requested probes; null = automatic set only.</param>
    /// <returns>The extracted response row.</returns>
    public static CcxResponseRowData Extract(string? deckPath, string? frdPath, string? datPath, CcxExtractionRequestData? request)
    {
        var row = new CcxResponseRowData();

        var blocks = frdPath != null ? Guard(() => FrdResultReader.Read(frdPath), []) : [];
        Guard(() => AddTemperatureResponses(row, blocks));
        Guard(() => AddDisplacementResponses(row, blocks));
        Guard(() => AddStressResponses(row, blocks));

        if (datPath != null)
            Guard(() => AddDatResponses(row, DatResultReader.Read(datPath)));

        if (request?.Probes.Count > 0 && deckPath != null && blocks.Count > 0)
            AddProbes(row, blocks, Guard(() => DeckNodeSetReader.Read(deckPath), new Dictionary<string, HashSet<int>>()), request.Probes);

        return row;
    }

    // The readers tolerate malformed LINES, but a truncated block can still
    // blow up the value math (a node row cut mid-write yields fewer numbers
    // than the block's component list promises). One damaged block must
    // empty ITS response family only — the row keeps what the intact blocks
    // yielded. This is where the "never throws over a finished solve"
    // contract is enforced, not hoped for.
    private static void Guard(Action extraction)
    {
        try
        {
            extraction();
        }
        catch
        {
        }
    }

    private static T Guard<T>(Func<T> extraction, T fallback)
    {
        try
        {
            return extraction();
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddTemperatureResponses(CcxResponseRowData row, List<FrdResultBlock> blocks)
    {
        var temperatures = blocks.Where(block => block.Name == DATASET_TEMPERATURE).ToList();
        if (temperatures.Count == 0)
            return;

        var final = TemperatureField(temperatures[^1]);
        Add(row, "max_temp", final.Values.Max());
        Add(row, "min_temp", final.Values.Min());

        // A transient run carries one block per increment — the peak over the
        // whole history is a response of its own (time-to-threshold territory).
        if (temperatures.Count > 1)
            Add(row, "max_temp_any_step", temperatures.SelectMany(block => TemperatureField(block).Values).Max());
    }

    private static void AddDisplacementResponses(CcxResponseRowData row, List<FrdResultBlock> blocks)
    {
        var displacement = blocks.LastOrDefault(block => block.Name == DATASET_DISPLACEMENT && block.Components.Count >= 3);
        if (displacement == null)
            return;

        Add(row, "max_disp_magnitude", displacement.Values.Values.Max(Magnitude));
        Add(row, "max_disp_x", displacement.Values.Values.Max(values => System.Math.Abs(values[0])));
        Add(row, "max_disp_y", displacement.Values.Values.Max(values => System.Math.Abs(values[1])));
        Add(row, "max_disp_z", displacement.Values.Values.Max(values => System.Math.Abs(values[2])));
    }

    private static void AddStressResponses(CcxResponseRowData row, List<FrdResultBlock> blocks)
    {
        var stress = blocks.LastOrDefault(block => block.Name == DATASET_STRESS && block.Components.Count >= 6);
        if (stress == null)
            return;

        Add(row, "max_von_mises", stress.Values.Values.Max(VonMises));
        Add(row, "min_von_mises", stress.Values.Values.Min(VonMises));
    }

    private static void AddDatResponses(CcxResponseRowData row, DatResultFile dat)
    {
        for (var i = 0; i < dat.Eigenfrequencies.Count; i++)
            Add(row, $"eigenfrequency_{i + 1}", dat.Eigenfrequencies[i]);

        for (var i = 0; i < dat.BucklingFactors.Count; i++)
            Add(row, $"buckling_factor_{i + 1}", dat.BucklingFactors[i]);

        foreach (var forces in dat.ForceSums)
        {
            var set = forces.SetName.ToLowerInvariant();
            Add(row, $"rf_sum_x@{set}", forces.Fx);
            Add(row, $"rf_sum_y@{set}", forces.Fy);
            Add(row, $"rf_sum_z@{set}", forces.Fz);
        }
    }

    private static void AddProbes(
        CcxResponseRowData row,
        List<FrdResultBlock> blocks,
        Dictionary<string, HashSet<int>> sets,
        List<CcxProbeData> probes)
    {
        foreach (var probe in probes)
            Guard(() => AddProbe(row, blocks, sets, probe));
    }

    private static void AddProbe(
        CcxResponseRowData row,
        List<FrdResultBlock> blocks,
        Dictionary<string, HashSet<int>> sets,
        CcxProbeData probe)
    {
        if (!sets.TryGetValue(probe.SetName, out var nodes) || nodes.Count == 0)
            return;

        var field = ProbeField(blocks, probe.Quantity);
        if (field == null)
            return;

        var samples = nodes
            .Where(field.ContainsKey)
            .Select(node => field[node])
            .ToList();

        if (samples.Count == 0)
            return;

        var value = probe.Aggregate switch
        {
            CcxProbeAggregate.Min => samples.Min(),
            CcxProbeAggregate.Avg => samples.Average(),
            _ => samples.Max()
        };

        var aggregate = probe.Aggregate.ToString().ToLowerInvariant();
        Add(row, $"{aggregate}_{probe.Quantity.ToLowerInvariant()}@{probe.SetName.ToLowerInvariant()}", value);
    }

    private static Dictionary<int, double>? ProbeField(List<FrdResultBlock> blocks, string quantity)
    {
        if (quantity.Equals(QUANTITY_TEMPERATURE, StringComparison.OrdinalIgnoreCase)
            || quantity.Equals(DATASET_TEMPERATURE, StringComparison.OrdinalIgnoreCase))
        {
            var block = blocks.LastOrDefault(candidate => candidate.Name == DATASET_TEMPERATURE);
            return block == null ? null : TemperatureField(block);
        }

        if (quantity.Equals(QUANTITY_VON_MISES, StringComparison.OrdinalIgnoreCase))
        {
            var block = blocks.LastOrDefault(candidate => candidate.Name == DATASET_STRESS && candidate.Components.Count >= 6);
            return block?.Values.ToDictionary(pair => pair.Key, pair => VonMises(pair.Value));
        }

        if (quantity.Equals(QUANTITY_DISPLACEMENT_MAGNITUDE, StringComparison.OrdinalIgnoreCase))
        {
            var block = blocks.LastOrDefault(candidate => candidate.Name == DATASET_DISPLACEMENT && candidate.Components.Count >= 3);
            return block?.Values.ToDictionary(pair => pair.Key, pair => Magnitude(pair.Value));
        }

        return null;
    }

    private static Dictionary<int, double> TemperatureField(FrdResultBlock block)
    {
        return block.Values.ToDictionary(pair => pair.Key, pair => pair.Value[0]);
    }

    private static double Magnitude(double[] components)
    {
        return System.Math.Sqrt(
            components[0] * components[0]
            + components[1] * components[1]
            + components[2] * components[2]);
    }

    private static double VonMises(double[] stress)
    {
        var (sxx, syy, szz, sxy, syz, szx) = (stress[0], stress[1], stress[2], stress[3], stress[4], stress[5]);
        return System.Math.Sqrt(0.5 * (
            (sxx - syy) * (sxx - syy)
            + (syy - szz) * (syy - szz)
            + (szz - sxx) * (szz - sxx)
            + 6.0 * (sxy * sxy + syz * syz + szx * szx)));
    }

    private static void Add(CcxResponseRowData row, string name, double value)
    {
        row.Values.Add(new CcxResponseValueData { Name = name, Value = value });
    }

    #endregion
}
