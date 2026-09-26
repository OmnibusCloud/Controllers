using System.Collections;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.Sweep.Interfaces;
using OutWit.Controller.Sweep.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Families;

/// <summary>
/// OpenFOAM studies: one case, every variant its token values. Substitution
/// happens on the node over the case's unchanged base files, so a chunk here
/// is metadata only - the same case for every task and each variant's values
/// - and the base files travel once per node. The study is checked with the
/// OpenFOAM model's own rules (the node runs the same ones) and the case's
/// templated files are read once to prove every token has a place and every
/// place a token.
/// </summary>
internal sealed class SweepFamilyOpenFOAM : ISweepFamily
{
    #region Constants

    /// <summary>The bundled script of the family.</summary>
    public const string SCRIPT_NAME = "SweepOpenFOAM";

    #endregion

    #region ISweepFamily

    public async Task<IReadOnlyList<string>> ValidateAsync(SweepOptionsData options, IWitBlobService blobService)
    {
        var findings = new List<string>();
        var data = options.OpenFOAM;
        if (data == null)
        {
            findings.Add("The study carries no OpenFOAM block.");
            return findings;
        }

        findings.AddRange(FoamCaseRules.Validate(data));
        if (findings.Count > 0)
            return findings;

        // Every declared token has a place in a templated file and every
        // place a declared token - otherwise every variant would be refused on
        // its node, one after another. Only the templated files are read:
        // meshes and fields that carry no token never leave the blob store.
        var templated = new List<(string RelativePath, string Text)>();
        foreach (var file in data.BaseFiles.Where(file => file.Templated))
        {
            var path = await blobService.GetLocalPathAsync(file.BlobId);
            templated.Add((file.RelativePath, FoamCaseText.FromBytes(await File.ReadAllBytesAsync(path))));
        }

        findings.AddRange(FoamTemplating.CheckCoverage(options.Parameters.Select(parameter => parameter.Token).ToList(), templated));
        return findings;
    }

    public Task<IReadOnlyList<object>> MakeTasksAsync(SweepOptionsData options, IReadOnlyList<SweepVariantData> variants, IWitBlobService blobService)
    {
        var data = options.OpenFOAM ?? throw new InvalidOperationException("The study carries no OpenFOAM block.");

        IReadOnlyList<object> tasks = variants
            .Select(variant => (object)new FoamTaskData
            {
                VariantIndex = variant.VariantIndex,
                Case = data,
                Substitutions = options.Parameters
                    .Select((parameter, index) => new FoamTokenValueData { Token = parameter.Token, Value = variant.Values[index] })
                    .ToList()
            })
            .ToList();

        return Task.FromResult(tasks);
    }

    public IReadOnlyList<SweepManifestRowData> ToRows(IEnumerable wave)
    {
        return wave
            .OfType<FoamResultData>()
            .Select(result => new SweepManifestRowData
            {
                VariantIndex = result.VariantIndex,
                Outcome = OutcomeOf(result),
                OpenFOAM = result
            })
            .ToList();
    }

    public IReadOnlyList<SweepArtifactData> ArtifactsOf(SweepManifestRowData row)
    {
        if (row.OpenFOAM?.ArtifactBlobId is not { } blobId)
            return [];

        return [new SweepArtifactData { Kind = SweepArtifactKind.OpenFOAMCase, BlobId = blobId, Bytes = row.OpenFOAM.ArtifactBytes }];
    }

    /// <summary>
    /// The sweep's verdict on an OpenFOAM run: refused when the node named
    /// reasons before anything ran, succeeded when every step exited cleanly,
    /// failed otherwise. Convergence is a fact of the result, not a verdict -
    /// a run that stopped at its end time unconverged finished cleanly.
    /// </summary>
    /// <param name="result">The node's result.</param>
    /// <returns>The outcome.</returns>
    public static SweepOutcome OutcomeOf(FoamResultData result)
    {
        if (result.Rejections.Count > 0)
            return SweepOutcome.Refused;

        return result.ExitCode == 0 && result.FailedStep == null ? SweepOutcome.Succeeded : SweepOutcome.Failed;
    }

    #endregion

    #region Properties

    public SweepFamily Family => SweepFamily.OpenFOAM;

    public Type TaskType => typeof(FoamTaskData);

    public string ScriptName => SCRIPT_NAME;

    #endregion
}
