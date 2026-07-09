using System.IO.Compression;
using MemoryPack;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Dcc.Activities;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Adapters;

internal sealed class WitActivityAdapterRenderUnzipDccScene : WitActivityAdapterFunction<WitActivityRenderUnzipDccScene>
{
    #region Constructors

    public WitActivityAdapterRenderUnzipDccScene(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityRenderUnzipDccScene CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"Render.UnzipDccScene expects 1 parameter, got {parameters.Length}");

        return new WitActivityRenderUnzipDccScene
        {
            Packed = parameters[0]
        };
    }

    protected override Task Process(
        WitActivityRenderUnzipDccScene activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Packed, out IReadOnlyList<byte>? packed) || packed == null || packed.Count == 0)
            throw new InvalidOperationException("Failed to get ByteCollection parameter 'packed' for Render.UnzipDccScene.");

        var scene = Unzip(packed);

        if (!pool.TrySetValue(activity.ReturnReference, scene))
        {
            throw new InvalidOperationException(
                $"Failed to set return value '{activity.ReturnReference}' for Render.UnzipDccScene.");
        }

        return Task.CompletedTask;
    }

    internal static DccSceneData Unzip(IReadOnlyList<byte> packed)
    {
        var buffer = packed as byte[] ?? [.. packed];

        using var input = new MemoryStream(buffer, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);

        return MemoryPackSerializer.Deserialize<DccSceneData>(output.ToArray())
               ?? throw new InvalidOperationException(
                   "Render.UnzipDccScene payload decompressed but did not deserialize to a DccScene.");
    }

    #endregion
}
