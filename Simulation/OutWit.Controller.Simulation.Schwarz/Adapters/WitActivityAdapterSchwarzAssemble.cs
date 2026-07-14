using System.Collections;
using System.Linq;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzAssemble : WitActivityAdapterFunction<WitActivitySchwarzAssemble>
{
    #region Constructors

    public WitActivityAdapterSchwarzAssemble(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzAssemble activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SchwarzPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetObject(activity.Wave, out var waveObject) || waveObject is not IEnumerable waveEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Wave'.");

        var wave = waveEnumerable.OfType<SchwarzResultData>().OrderBy(result => result.SubdomainIndex).ToList();
        if (wave.Count != plan.Parts)
            throw new InvalidOperationException($"Schwarz.Assemble expected {plan.Parts} wave results, got {wave.Count}.");

        for (var i = 0; i < wave.Count; i++)
        {
            // Count alone would let a duplicate+missing pair leave a region zeroed.
            if (wave[i].SubdomainIndex != i)
                throw new InvalidOperationException($"Schwarz.Assemble: wave is not a permutation of subdomains (expected index {i}, got {wave[i].SubdomainIndex}).");
        }

        double[]? globalField = null;
        int[]? globalDims = null;

        foreach (var result in wave)
        {
            if (result.FieldBlobId == null)
                throw new InvalidOperationException($"Schwarz.Assemble: subdomain {result.SubdomainIndex} carries no field slice — Assemble consumes the final emit wave only.");

            var slicePath = await BlobService.GetLocalPathAsync(result.FieldBlobId.Value);
            var slice = SimulationFieldSlice.FromBlobBytes(await File.ReadAllBytesAsync(slicePath));

            globalDims ??= slice.GlobalDims;
            globalField ??= new double[globalDims[0] * globalDims[1] * globalDims[2]];

            slice.PasteInto(globalField);
        }

        var assembled = new SimulationFieldSlice
        {
            PartIndex = SimulationFieldSlice.FULL_FIELD_PART_INDEX,
            GlobalDims = globalDims!,
            Lo = [0, 0, 0],
            Hi = (int[])globalDims!.Clone(),
            Values = globalField!
        };

        var fieldBlobId = await BlobService.UploadBytesAsync(assembled.ToBlobBytes(), "field.owsm");

        if (!pool.TrySetValue(activity.ReturnReference, fieldBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzAssemble CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference wave)
                throw new ArgumentException("Parameter 'Wave' must be a variable reference.");

            return new WitActivitySchwarzAssemble
            {
                Plan = plan,
                Wave = wave
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
