using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Parareal;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Utils;

/// <summary>
/// Per-process kernel cache for server-side adapters (Slice/Init/Correct run
/// every round): factorizations are built once per (model, plan-parameters)
/// key and reused. LRU-bounded and poison-evicting via SimulationComputeCache.
/// </summary>
internal static class PararealKernelCache
{
    #region Constants

    private const int MAX_CACHED_KERNELS = 16;

    #endregion

    #region Fields

    private static readonly SimulationComputeCache<string, PararealKernel> m_kernels = new(MAX_CACHED_KERNELS);

    #endregion

    #region Functions

    public static async Task<PararealKernel> GetOrCreateAsync(
        IWitBlobService blobService,
        Guid modelBlobId,
        int slabs,
        int coarsening,
        double totalTime,
        int fineStepsPerSlab)
    {
        var key = $"{modelBlobId:N}:{slabs}:{coarsening}:{totalTime:R}:{fineStepsPerSlab}";
        var modelPath = await blobService.GetLocalPathAsync(modelBlobId);

        return m_kernels.GetOrCreate(key, () =>
            new PararealKernel(
                SimulationModelDefinition.FromBlobBytes(File.ReadAllBytes(modelPath)),
                slabs,
                coarsening,
                totalTime,
                fineStepsPerSlab));
    }

    public static Task<PararealKernel> GetOrCreateAsync(IWitBlobService blobService, PararealPlanData plan)
    {
        return GetOrCreateAsync(
            blobService,
            plan.ModelBlobId,
            plan.Slabs,
            plan.Coarsening,
            plan.SlabBoundaries[^1],
            plan.FineStepsPerSlab);
    }

    #endregion
}
