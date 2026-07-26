using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Numerics;
using OutWit.Controller.Simulation.Model.Parareal;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Controller.Simulation.Parareal.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

internal sealed class WitActivityAdapterPararealInit : WitActivityAdapterFunction<WitActivityPararealInit>
{
    #region Constructors

    public WitActivityAdapterPararealInit(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealInit activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Model, out Guid modelBlobId) || modelBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Model'.");

        if (!pool.TryGetValue(activity.Plan, out PararealPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (plan.ModelBlobId != modelBlobId)
            throw new InvalidOperationException("Parareal.Init: the model does not match the plan's model blob.");

        var kernel = await PararealKernelCache.GetOrCreateAsync(BlobService, plan);

        // Round 0: serial coarse sweep from u₀ (server-side, cheap on the coarse grid).
        var states = new double[plan.Slabs + 1][];
        states[0] = kernel.BuildInitialField();
        for (var slab = 0; slab < plan.Slabs; slab++)
            states[slab + 1] = kernel.CoarsePropagate(states[slab], slab);

        var scale = states.Max(state => FixedOrderNorms.MaxAbs(state));
        if (scale == 0)
            scale = 1;

        var stateBlobIds = new List<Guid>(plan.Slabs + 1);
        for (var slab = 0; slab <= plan.Slabs; slab++)
            stateBlobIds.Add(await UploadStateAsync(kernel, plan, states[slab], slab, round: 0));

        var state = new PararealStateData
        {
            Round = 0,
            CorrectionNorm = 0,
            Scale = scale,
            Eps = plan.Eps,
            StateBlobIds = stateBlobIds
        };

        if (!pool.TrySetValue(activity.ReturnReference, state))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    private async Task<Guid> UploadStateAsync(PararealKernel kernel, PararealPlanData plan, double[] values, int slab, int round)
    {
        var snapshot = new PararealStateSnapshot
        {
            SlabIndex = slab,
            Round = round,
            Time = plan.SlabBoundaries[slab],
            GlobalDims = (int[])kernel.FineProblem.Geometry.Dims.Clone(),
            Values = values
        };

        return await BlobService.UploadBytesAsync(snapshot.ToBlobBytes(), $"state_s{slab}_r{round}.owsm");
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealInit CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference model)
                throw new ArgumentException("Parameter 'Model' must be a variable reference.");

            if (parameters[1] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            return new WitActivityPararealInit
            {
                Model = model,
                Plan = plan
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
