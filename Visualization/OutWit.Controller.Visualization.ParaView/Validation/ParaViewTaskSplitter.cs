using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Deterministic task generation (docs 03, section 11): one task per resolved timestep of the
/// resolved view, identities from the package digest + dataset identity (reserved) + view +
/// timestep + options digest, and — per task — the minimal attachment subset: the files associated
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
    /// <returns>The tasks in render order (view order, then timestep order).</returns>
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
        var index = new SubsetIndex(scene.Attachments);

        var tasks = new List<ParaViewRenderTaskData>(report.ResolvedTimestepIndices.Count);
        var taskIndex = 0;

        foreach (var timestepIndex in report.ResolvedTimestepIndices)
        {
            var subset = index.SubsetOf(timestepIndex);
            var subsetBytes = Math.Max(0, scene.StateSize) + subset.Sum(me => Math.Max(0, me.Size));

            if (subsetBytes > ParaViewInputLimits.MAX_TASK_SUBSET_BYTES)
                throw new InvalidOperationException(
                    $"ParaView.Split: the subset of timestep {timestepIndex} is {subsetBytes} bytes, over the {ParaViewInputLimits.MAX_TASK_SUBSET_BYTES} byte per-task limit.");

            var taskOptions = (ParaViewOutputOptionsData)options.Clone();
            taskOptions.ViewId = report.ResolvedViewId;

            tasks.Add(new ParaViewRenderTaskData
            {
                TaskId = ParaViewPackageDigest.ComputeTaskId(packageDigest, DATASET_ID_V1, report.ResolvedViewId, timestepIndex, optionsDigest),
                TaskIndex = taskIndex++,
                StateBlobId = scene.StateBlobId,
                StateSha256 = scene.StateSha256,
                StateSize = scene.StateSize,
                ViewId = report.ResolvedViewId,
                TimestepIndex = timestepIndex,
                TimeValue = timestepIndex < report.TimestepValues.Count ? report.TimestepValues[timestepIndex] : null,
                Options = taskOptions,
                Attachments = subset,
                Runtime = (ParaViewRuntimeRequirementData)scene.Runtime.Clone(),
                PackageDigest = packageDigest,
                DatasetId = DATASET_ID_V1,
                SubsetBytes = subsetBytes
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
        return new SubsetIndex(attachments).SubsetOf(timestepIndex);
    }

    #endregion

    #region SubsetIndex

    /// <summary>
    /// One pass over the attachments: the always-included set and a timestep → attachments map, so a
    /// package of many attachments and many timesteps splits in O(attachments + outputs) lookups.
    /// Attachments keep their package order inside every subset.
    /// </summary>
    private sealed class SubsetIndex
    {
        #region Fields

        private readonly List<(int Order, ParaViewAttachmentRefData Attachment)> m_always = [];

        private readonly Dictionary<int, List<(int Order, ParaViewAttachmentRefData Attachment)>> m_byTimestep = new();

        #endregion

        #region Constructors

        public SubsetIndex(IEnumerable<ParaViewAttachmentRefData> attachments)
        {
            var order = 0;
            foreach (var attachment in attachments)
            {
                if (attachment.Role == ParaViewAttachmentRole.SeriesIndex || attachment.TimestepIndices.Count == 0)
                {
                    m_always.Add((order, attachment));
                }
                else
                {
                    foreach (var timestep in attachment.TimestepIndices.Distinct())
                    {
                        if (!m_byTimestep.TryGetValue(timestep, out var list))
                        {
                            list = [];
                            m_byTimestep[timestep] = list;
                        }

                        list.Add((order, attachment));
                    }
                }

                order++;
            }
        }

        #endregion

        #region Functions

        public List<ParaViewAttachmentRefData> SubsetOf(int timestepIndex)
        {
            IEnumerable<(int Order, ParaViewAttachmentRefData Attachment)> members = m_always;
            if (m_byTimestep.TryGetValue(timestepIndex, out var own))
                members = members.Concat(own);

            return members
                .OrderBy(me => me.Order)
                .Select(me => (ParaViewAttachmentRefData)me.Attachment.Clone())
                .ToList();
        }

        #endregion
    }

    #endregion
}
