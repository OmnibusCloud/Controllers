using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Activities;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityParaViewValidate"/>: downloads the state (only the state),
/// runs <see cref="ParaViewPackageValidator"/> and returns the report. An invalid package is a
/// completed activity with an invalid report — the job fails at ParaView.Split, and a validate-only
/// job returns the findings to the initiator.
/// </summary>
internal sealed class WitActivityAdapterParaViewValidate : WitActivityAdapterFunction<WitActivityParaViewValidate>
{
    #region Constructors

    public WitActivityAdapterParaViewValidate(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityParaViewValidate CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"ParaView.Validate expects 2 parameters (ParaViewSceneRef, ParaViewOutputOptions), got {parameters.Length}");

        return new WitActivityParaViewValidate
        {
            Scene = parameters[0],
            Options = parameters[1]
        };
    }

    protected override async Task Process(
        WitActivityParaViewValidate activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out ParaViewSceneRefData? scene) || scene == null)
            throw new InvalidOperationException("Failed to get ParaViewSceneRef parameter 'scene'");

        if (!pool.TryGetValue(activity.Options, out ParaViewOutputOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get ParaViewOutputOptions parameter 'options'");

        ProcessingManager.ThrowIfCancellationRequested(status.JobId);

        var statePath = scene.StateBlobId == Guid.Empty
            ? string.Empty
            : await BlobService.GetLocalPathAsync(scene.StateBlobId);

        var validator = new ParaViewPackageValidator(ParaViewProxyAllowlist.Bundled, ParaViewRuntimeInfo.BundledReaderVersion());

        var report = validator.Validate(scene, options, statePath);

        if (report.IsValid)
            Logger.LogInformation("ParaView.Validate: package {Digest} is valid — view '{View}', {Frames} output(s), {Attachments} attachment(s), {Bytes} bytes, {Warnings} warning(s), {Fallbacks} fallback(s)",
                report.PackageDigest[..12], report.ResolvedViewId, report.ResolvedTimestepIndices.Count, report.AttachmentCount, report.TotalAttachmentBytes, report.Warnings.Count, report.Fallbacks.Count);
        else
            Logger.LogWarning("ParaView.Validate: package rejected with {Count} error(s): {Errors}",
                report.Errors.Count, string.Join(" | ", report.Errors.Take(8)));

        if (!pool.TrySetValue(activity.ReturnReference, report))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.Validate.");
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
