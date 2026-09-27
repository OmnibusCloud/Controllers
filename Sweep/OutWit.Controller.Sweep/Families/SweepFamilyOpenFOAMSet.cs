using System.Collections;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.Sweep.Interfaces;
using OutWit.Controller.Sweep.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Families;

/// <summary>
/// OpenFOAM case sets: every variant its own ready case - a tree and a recipe
/// of its own, no tokens - and the study's OpenFOAM block what the cases share
/// (threads, responses, artifacts, the work-estimate fallbacks). The node
/// runs each task's case as it runs a study's, so a chunk here is metadata
/// only and the results, rows and artifacts are the OpenFOAM family's. Each
/// case is checked with the OpenFOAM model's own rules under its name, before
/// any node sees a task.
/// </summary>
internal sealed class SweepFamilyOpenFOAMSet : ISweepFamily
{
    #region Constants

    /// <summary>The bundled script of the case-set form.</summary>
    public const string SCRIPT_NAME = "SweepOpenFOAMSet";

    #endregion

    #region Fields

    private readonly SweepOpenFOAMSetData m_caseSet;

    private readonly SweepFamilyOpenFOAM m_family = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the case-set form over the submitted cases.
    /// </summary>
    /// <param name="caseSet">The cases, one per variant.</param>
    public SweepFamilyOpenFOAMSet(SweepOpenFOAMSetData caseSet)
    {
        m_caseSet = caseSet;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The case a node runs for one case of the set: the case's tree, recipe
    /// and size, the study block's threads, responses, artifacts and budget.
    /// Every member of <see cref="FoamCaseData"/> is placed here, from one or
    /// the other.
    /// </summary>
    /// <param name="block">The study's OpenFOAM block.</param>
    /// <param name="item">One case of the set.</param>
    /// <returns>The case as the task carries it.</returns>
    public static FoamCaseData CaseOf(FoamCaseData block, SweepOpenFOAMCaseData item)
    {
        return new FoamCaseData
        {
            BaseFiles = item.BaseFiles,
            Recipe = item.Recipe,
            Threads = block.Threads,
            Extraction = block.Extraction,
            ArtifactPolicy = block.ArtifactPolicy,
            CellCount = item.CellCount > 0 ? item.CellCount : block.CellCount,
            SolverClass = string.IsNullOrEmpty(item.SolverClass) ? block.SolverClass : item.SolverClass,
            TimeBudgetSeconds = block.TimeBudgetSeconds
        };
    }

    #endregion

    #region ISweepFamily

    public Task<IReadOnlyList<string>> ValidateAsync(SweepOptionsData options, IWitBlobService blobService)
    {
        var findings = new List<string>();
        var block = options.OpenFOAM;
        if (block == null)
        {
            findings.Add("The study carries no OpenFOAM block.");
            return Task.FromResult<IReadOnlyList<string>>(findings);
        }

        // The set and the block are distinct halves: a tree or a recipe in
        // the block would be a second case no variant runs.
        if (block.BaseFiles.Count > 0)
            findings.Add("A case set carries every case's files in the set; the study's OpenFOAM block names none.");
        if (block.Recipe != null)
            findings.Add("A case set carries every case's recipe in the set; the study's OpenFOAM block names none.");
        if (options.Parameters.Count > 0)
            findings.Add("A case set declares no parameters: every case runs as it is.");

        ValidateCoverage(options, findings);

        foreach (var item in m_caseSet.Cases)
        {
            var name = NameOf(item);
            foreach (var file in item.BaseFiles.Where(file => file.Templated))
                findings.Add($"{name}: {file.RelativePath} is marked templated, but a case set substitutes nothing.");

            findings.AddRange(FoamCaseRules.Validate(CaseOf(block, item)).Select(finding => $"{name}: {finding}"));
        }

        findings.AddRange(FoamTemplating.CheckResponseCoverage([], block.Extraction));
        return Task.FromResult<IReadOnlyList<string>>(findings);
    }

    public Task<IReadOnlyList<object>> MakeTasksAsync(SweepOptionsData options, IReadOnlyList<SweepVariantData> variants, IWitBlobService blobService)
    {
        var block = options.OpenFOAM ?? throw new InvalidOperationException("The study carries no OpenFOAM block.");
        var cases = m_caseSet.Cases.ToDictionary(item => item.VariantIndex);

        IReadOnlyList<object> tasks = variants
            .Select(variant => (object)new FoamTaskData
            {
                VariantIndex = variant.VariantIndex,
                Case = cases.TryGetValue(variant.VariantIndex, out var item)
                    ? CaseOf(block, item)
                    : throw new InvalidOperationException($"Variant #{variant.VariantIndex} has no case in the case set."),
                Substitutions = []
            })
            .ToList();

        return Task.FromResult(tasks);
    }

    public IReadOnlyList<SweepManifestRowData> ToRows(IEnumerable wave)
    {
        return m_family.ToRows(wave);
    }

    public IReadOnlyList<SweepArtifactData> ArtifactsOf(SweepManifestRowData row)
    {
        return m_family.ArtifactsOf(row);
    }

    #endregion

    #region Tools

    // Every variant exactly one case, every case a variant of the table.
    private void ValidateCoverage(SweepOptionsData options, List<string> findings)
    {
        var variantIndices = options.Variants.Select(variant => variant.VariantIndex).ToHashSet();
        foreach (var group in m_caseSet.Cases.GroupBy(item => item.VariantIndex))
        {
            if (group.Count() > 1)
                findings.Add($"Variant #{group.Key} has {group.Count()} cases in the case set.");
            if (!variantIndices.Contains(group.Key))
                findings.Add($"The case set carries a case for variant #{group.Key}, which the variant table does not have.");
        }

        foreach (var variantIndex in variantIndices.Where(index => m_caseSet.Cases.All(item => item.VariantIndex != index)))
            findings.Add($"Variant #{variantIndex} has no case in the case set.");
    }

    private static string NameOf(SweepOpenFOAMCaseData item)
    {
        return string.IsNullOrWhiteSpace(item.Name) ? $"Case #{item.VariantIndex}" : $"Case '{item.Name}'";
    }

    #endregion

    #region Properties

    public SweepFamily Family => SweepFamily.OpenFOAM;

    public Type TaskType => typeof(FoamTaskData);

    public string ScriptName => SCRIPT_NAME;

    #endregion
}
