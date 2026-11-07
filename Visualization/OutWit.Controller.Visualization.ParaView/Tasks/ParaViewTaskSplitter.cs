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
    /// Splits a validated package into batches (FrameBatch, docs 03, section 27, item 2): the tasks
    /// of <see cref="Split"/> grouped into consecutive chunks sized by <see cref="ParaViewChunkPolicy"/>,
    /// each chunk carrying the union of its outputs' attachment subsets.
    /// </summary>
    /// <param name="scene">The package reference.</param>
    /// <param name="report">A valid validation report for the same package and options.</param>
    /// <param name="options">The output options.</param>
    /// <returns>The batches in render order; the tasks inside keep their global task index.</returns>
    /// <exception cref="InvalidOperationException">The report is invalid or a limit is exceeded.</exception>
    public static IReadOnlyList<ParaViewRenderTaskBatchData> SplitBatched(
        ParaViewSceneRefData scene,
        ParaViewValidationReportData report,
        ParaViewOutputOptionsData options)
    {
        var tasks = Split(scene, report, options);
        return Chunk(scene, tasks, ParaViewChunkPolicy.ComputeChunkSize(tasks.Count));
    }

    /// <summary>
    /// Groups tasks (in their render order) into consecutive chunks of at most
    /// <paramref name="chunkSize"/> outputs; a chunk also closes early when the next output's
    /// attachments would push the chunk's union over the per-task byte limit, so a batch never
    /// materializes more than a single task is allowed to. Each chunk hoists the shared state, options
    /// and runtime, carries the union of its members' subsets in package order, and lists its members
    /// with EMPTY attachment lists.
    /// </summary>
    /// <param name="scene">The package reference (state size, attachment order).</param>
    /// <param name="tasks">The tasks of <see cref="Split"/>, with their subsets.</param>
    /// <param name="chunkSize">Maximum outputs per chunk (at least 1).</param>
    /// <returns>The batches.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chunk size is below 1.</exception>
    public static IReadOnlyList<ParaViewRenderTaskBatchData> Chunk(
        ParaViewSceneRefData scene,
        IReadOnlyList<ParaViewRenderTaskData> tasks,
        int chunkSize)
    {
        if (chunkSize < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "a chunk holds at least one output");

        var packageOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < scene.Attachments.Count; i++)
            packageOrder.TryAdd(scene.Attachments[i].LogicalPath, i);

        var stateBytes = Math.Max(0, scene.StateSize);
        var batches = new List<ParaViewRenderTaskBatchData>();
        var members = new List<ParaViewRenderTaskData>();
        var union = new Dictionary<string, ParaViewAttachmentRefData>(StringComparer.Ordinal);
        var unionBytes = 0L;

        void Flush()
        {
            if (members.Count == 0)
                return;

            var first = members[0];
            batches.Add(new ParaViewRenderTaskBatchData
            {
                BatchIndex = batches.Count,
                StateBlobId = first.StateBlobId,
                StateSha256 = first.StateSha256,
                StateSize = first.StateSize,
                Options = (ParaViewOutputOptionsData)first.Options.Clone(),
                Attachments = [.. union.Values
                    .OrderBy(me => packageOrder.TryGetValue(me.LogicalPath, out var position) ? position : int.MaxValue)
                    .ThenBy(me => me.LogicalPath, StringComparer.Ordinal)
                    .Select(me => (ParaViewAttachmentRefData)me.Clone())],
                Runtime = (ParaViewRuntimeRequirementData)first.Runtime.Clone(),
                PackageDigest = first.PackageDigest,
                DatasetId = first.DatasetId,
                SubsetBytes = stateBytes + unionBytes,
                Tasks = [.. members]
            });

            members = [];
            union = new Dictionary<string, ParaViewAttachmentRefData>(StringComparer.Ordinal);
            unionBytes = 0;
        }

        foreach (var task in tasks)
        {
            var additions = task.Attachments.Where(me => !union.ContainsKey(me.LogicalPath)).ToList();
            var additionalBytes = additions.Sum(me => Math.Max(0, me.Size));

            if (members.Count > 0
                && (members.Count >= chunkSize || ParaViewChunkPolicy.ExceedsSubsetLimit(stateBytes + unionBytes, additionalBytes)))
            {
                Flush();
                additions = [.. task.Attachments];
                additionalBytes = additions.Sum(me => Math.Max(0, me.Size));
            }

            foreach (var attachment in additions)
                union[attachment.LogicalPath] = attachment;
            unionBytes += additionalBytes;

            var member = (ParaViewRenderTaskData)task.Clone();
            member.Attachments = [];
            members.Add(member);
        }

        Flush();
        return batches;
    }

    /// <summary>
    /// The one-output batch of a single task — what ParaView.RenderFrame runs through the same
    /// node pipeline as a chunk.
    /// </summary>
    /// <param name="task">The task with its attachment subset.</param>
    /// <returns>A batch holding exactly this task.</returns>
    public static ParaViewRenderTaskBatchData BatchOf(ParaViewRenderTaskData task)
    {
        var member = (ParaViewRenderTaskData)task.Clone();
        member.Attachments = [];

        return new ParaViewRenderTaskBatchData
        {
            BatchIndex = task.TaskIndex,
            StateBlobId = task.StateBlobId,
            StateSha256 = task.StateSha256,
            StateSize = task.StateSize,
            Options = (ParaViewOutputOptionsData)task.Options.Clone(),
            Attachments = [.. task.Attachments.Select(me => (ParaViewAttachmentRefData)me.Clone())],
            Runtime = (ParaViewRuntimeRequirementData)task.Runtime.Clone(),
            PackageDigest = task.PackageDigest,
            DatasetId = task.DatasetId,
            SubsetBytes = task.SubsetBytes,
            Tasks = [member]
        };
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
