using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tasks;

/// <summary>
/// Deterministic task generation (docs 03, section 11): one task per resolved timestep of the
/// resolved view — or per orbit output when the options carry a turntable (section 27) —
/// identities from the package digest + dataset identity (reserved) + view + timestep + options
/// digest (+ orbit position for turntable outputs), and — per task — the minimal attachment subset: the files associated
/// with the task's timestep plus every attachment not associated with any timestep and every series
/// index. Limits are enforced before a large collection is allocated.
/// </summary>
public static class ParaViewTaskSplitter
{
    #region Constants

    /// <summary>Dataset identity component of version-1 task identities (reserved, empty).</summary>
    public const string DATASET_ID_V1 = "";

    #endregion

    #region Functions

    /// <summary>
    /// Splits a validated package into tasks.
    /// </summary>
    /// <param name="scene">The package reference.</param>
    /// <param name="report">A valid validation report for the same package and options.</param>
    /// <param name="options">The output options.</param>
    /// <returns>The tasks in render order (view order, then timestep order, then orbit order).</returns>
    /// <exception cref="InvalidOperationException">The report is invalid or a limit is exceeded.</exception>
    public static IReadOnlyList<ParaViewRenderTaskData> Split(
        ParaViewSceneRefData scene,
        ParaViewValidationReportData report,
        ParaViewOutputOptionsData options)
    {
        if (!report.IsValid)
            throw new InvalidOperationException(
                $"ParaView.Split refuses an invalid package: {string.Join("; ", report.Errors)}");

        if (string.IsNullOrEmpty(report.ResolvedViewId))
            throw new InvalidOperationException("ParaView.Split: the validation report resolves no view.");

        if (report.ResolvedTimestepIndices.Count == 0)
            throw new InvalidOperationException("ParaView.Split: the validation report resolves no timesteps.");

        if (report.ResolvedTimestepIndices.Count > ParaViewInputLimits.MAX_OUTPUTS)
            throw new InvalidOperationException(
                $"ParaView.Split: {report.ResolvedTimestepIndices.Count} outputs exceed the {ParaViewInputLimits.MAX_OUTPUTS} per job limit.");

        var packageDigest = string.IsNullOrEmpty(report.PackageDigest)
            ? ParaViewPackageDigest.ComputePackageDigest(scene)
            : report.PackageDigest;
        var optionsDigest = ParaViewPackageDigest.ComputeOptionsDigest(options, report.ResolvedViewId);
        var index = new ParaViewAttachmentSubsetIndex(scene.Attachments);

        var plan = ParaViewTurntableResolver.Resolve(report.ResolvedTimestepIndices, options.Turntable);
        if (plan.Count > ParaViewInputLimits.MAX_OUTPUTS)
            throw new InvalidOperationException(
                $"ParaView.Split: {plan.Count} outputs exceed the {ParaViewInputLimits.MAX_OUTPUTS} per job limit.");

        var tasks = new List<ParaViewRenderTaskData>(plan.Count);
        var taskIndex = 0;
        var subsets = new Dictionary<int, List<ParaViewAttachmentRefData>>();

        foreach (var step in plan)
        {
            var timestepIndex = step.TimestepIndex;
            if (!subsets.TryGetValue(timestepIndex, out var subset))
                subsets[timestepIndex] = subset = index.SubsetOf(timestepIndex);
            var subsetBytes = Math.Max(0, scene.StateSize) + subset.Sum(me => Math.Max(0, me.Size));

            if (subsetBytes > ParaViewInputLimits.MAX_TASK_SUBSET_BYTES)
                throw new InvalidOperationException(
                    $"ParaView.Split: the subset of timestep {timestepIndex} is {subsetBytes} bytes, over the {ParaViewInputLimits.MAX_TASK_SUBSET_BYTES} byte per-task limit.");

            var taskOptions = (ParaViewOutputOptionsData)options.Clone();
            taskOptions.ViewId = report.ResolvedViewId;

            tasks.Add(new ParaViewRenderTaskData
            {
                TaskId = options.Turntable == null
                    ? ParaViewPackageDigest.ComputeTaskId(packageDigest, DATASET_ID_V1, report.ResolvedViewId, timestepIndex, optionsDigest)
                    : ParaViewPackageDigest.ComputeTaskId(packageDigest, DATASET_ID_V1, report.ResolvedViewId, timestepIndex, optionsDigest, step, ParaViewCameraAxes.WireToken(options.Turntable.Axis), options.Turntable.TimeMode),
                TaskIndex = taskIndex++,
                StateBlobId = scene.StateBlobId,
                StateSha256 = scene.StateSha256,
                StateSize = scene.StateSize,
                ViewId = report.ResolvedViewId,
                TimestepIndex = timestepIndex,
                TimeValue = timestepIndex < report.TimestepValues.Count ? report.TimestepValues[timestepIndex] : null,
                Options = taskOptions,
                Attachments = [.. subset.Select(me => (ParaViewAttachmentRefData)me.Clone())],
                Runtime = (ParaViewRuntimeRequirementData)scene.Runtime.Clone(),
                PackageDigest = packageDigest,
                DatasetId = DATASET_ID_V1,
                SubsetBytes = subsetBytes,
                OrbitIndex = step.OrbitIndex,
                AzimuthDegrees = step.AzimuthDegrees,
                ElevationDegrees = step.ElevationDegrees,
                DollyFactor = step.DollyFactor
            });
        }

        return tasks;
    }

    /// <summary>
    /// The minimal attachment subset of one timestep: every attachment with no timestep association
    /// (static inputs, series indexes, auxiliary files, fallback groups) plus the attachments
    /// associated with the timestep. Order follows the package's attachment order.
    /// </summary>
    /// <param name="attachments">All package attachments.</param>
    /// <param name="timestepIndex">The task's timestep index.</param>
    /// <returns>Cloned attachment references.</returns>
    public static List<ParaViewAttachmentRefData> ComputeSubset(IEnumerable<ParaViewAttachmentRefData> attachments, int timestepIndex)
    {
        return new ParaViewAttachmentSubsetIndex(attachments).SubsetOf(timestepIndex);
    }

    #endregion
}
