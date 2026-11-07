using System.Collections;
using System.Text;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Interfaces;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Families;

/// <summary>
/// CalculiX studies: a template (the base deck with baked tokens, one deck
/// materialised per variant here, on the host, by plain substitution) or a
/// deck set (every variant brings its own deck; its chunks are metadata only).
/// Each task is one whole deck for <c>Ccx.Solve</c>.
/// </summary>
internal sealed class SweepFamilyCalculiX : ISweepFamily
{
    #region Constants

    /// <summary>The bundled script of the family.</summary>
    public const string SCRIPT_NAME = "SweepCalculiX";

    #endregion

    #region ISweepFamily

    public async Task<IReadOnlyList<string>> ValidateAsync(SweepOptionsData options, IWitBlobService blobService)
    {
        var findings = new List<string>();
        var study = options.CalculiX;
        if (study == null)
        {
            findings.Add("The study carries no CalculiX block.");
            return findings;
        }

        if (study.Decks.Count > 0)
        {
            ValidateDeckSet(options, study, findings);
            return findings;
        }

        if (study.BaseDeckBlobId == Guid.Empty)
        {
            findings.Add("A CalculiX template study names no base deck.");
            return findings;
        }

        // The template is validated once, up front: a token missing from the
        // deck must refuse the sweep before any node burns a solve on it.
        var deckText = await ReadTextAsync(blobService, study.BaseDeckBlobId);
        try
        {
            SweepDeckTemplating.ValidateTemplate(deckText, options.Parameters);
        }
        catch (InvalidOperationException e)
        {
            findings.Add(e.Message);
        }

        return findings;
    }

    public async Task<IReadOnlyList<object>> MakeTasksAsync(SweepOptionsData options, IReadOnlyList<SweepVariantData> variants, IWitBlobService blobService)
    {
        var study = options.CalculiX ?? throw new InvalidOperationException("The study carries no CalculiX block.");
        var decks = study.Decks.ToDictionary(deck => deck.VariantIndex);

        // Variant decks materialise lazily, one chunk at a time - a
        // 300-variant night sweep never holds 300 deck blobs at once. The base
        // template loads only when a templated variant needs it.
        string? deckText = null;

        var tasks = new List<object>(variants.Count);
        foreach (var variant in variants)
        {
            Guid deckBlobId;
            var nodeCount = study.NodeCount;
            var elementCount = study.ElementCount;

            if (decks.TryGetValue(variant.VariantIndex, out var deck))
            {
                deckBlobId = deck.DeckBlobId;
                nodeCount = deck.NodeCount > 0 ? deck.NodeCount : nodeCount;
                elementCount = deck.ElementCount > 0 ? deck.ElementCount : elementCount;
            }
            else
            {
                deckText ??= await ReadTextAsync(blobService, study.BaseDeckBlobId);
                var variantDeck = SweepDeckTemplating.Instantiate(deckText, options.Parameters, variant.Values);
                deckBlobId = await blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(variantDeck), $"variant_{variant.VariantIndex}.inp");
            }

            tasks.Add(new CcxTaskData
            {
                VariantIndex = variant.VariantIndex,
                DeckBlobId = deckBlobId,
                NodeCount = nodeCount,
                ElementCount = elementCount,
                Threads = study.Threads,
                Extraction = study.Extraction
            });
        }

        return tasks;
    }

    public IReadOnlyList<SweepManifestRowData> ToRows(IEnumerable wave)
    {
        return wave
            .OfType<CcxResultData>()
            .Select(result => new SweepManifestRowData
            {
                VariantIndex = result.VariantIndex,
                Outcome = result.ExitCode == 0 ? SweepOutcome.Succeeded : SweepOutcome.Failed,
                CalculiX = result
            })
            .ToList();
    }

    public IReadOnlyList<SweepArtifactData> ArtifactsOf(SweepManifestRowData row)
    {
        var artifacts = new List<SweepArtifactData>();
        if (row.CalculiX?.FrdBlobId is { } frd)
            artifacts.Add(new SweepArtifactData { Kind = SweepArtifactKind.CalculiXFrd, BlobId = frd });
        if (row.CalculiX?.DatBlobId is { } dat)
            artifacts.Add(new SweepArtifactData { Kind = SweepArtifactKind.CalculiXDat, BlobId = dat });

        return artifacts;
    }

    #endregion

    #region Tools

    private static void ValidateDeckSet(SweepOptionsData options, SweepCalculiXStudyData study, List<string> findings)
    {
        // A deck set and a template are distinct modes: mixing them would
        // leave part of the table silently unsolvable.
        if (study.BaseDeckBlobId != Guid.Empty)
            findings.Add("A CalculiX study is either a deck set or a template, never both: it names a base deck and per-variant decks.");
        if (options.Parameters.Count > 0)
            findings.Add("A CalculiX deck-set study cannot also declare template parameters.");

        var variantIndices = options.Variants.Select(variant => variant.VariantIndex).ToHashSet();
        foreach (var group in study.Decks.GroupBy(deck => deck.VariantIndex))
        {
            if (group.Count() > 1)
                findings.Add($"Variant #{group.Key} has {group.Count()} decks in the deck set.");
            if (!variantIndices.Contains(group.Key))
                findings.Add($"The deck set carries a deck for variant #{group.Key}, which the variant table does not have.");
        }

        foreach (var variantIndex in variantIndices.Where(index => study.Decks.All(deck => deck.VariantIndex != index)))
            findings.Add($"Variant #{variantIndex} has no deck in the deck set.");

        foreach (var deck in study.Decks.Where(deck => deck.DeckBlobId == Guid.Empty))
            findings.Add($"The deck of variant #{deck.VariantIndex} names no blob.");
    }

    private static async Task<string> ReadTextAsync(IWitBlobService blobService, Guid blobId)
    {
        var path = await blobService.GetLocalPathAsync(blobId);
        return await File.ReadAllTextAsync(path);
    }

    #endregion

    #region Properties

    public SweepFamily Family => SweepFamily.CalculiX;

    public Type TaskType => typeof(CcxTaskData);

    public string ScriptName => SCRIPT_NAME;

    #endregion
}
