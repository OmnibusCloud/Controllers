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

    // Gzip expands ~1000:1 at the format's limit — an uncapped decompression turns a 2 MB
    // crafted payload into multi-GB heap pressure that OOM-kills the worker (and MemoryPack
    // then multiplies the deserialized object graph several times over). The cap gives ~6x
    // headroom over the heaviest verified scene (~150 MB raw); genuinely larger scenes belong
    // on the blob-referenced large-scene path, not an inline job parameter (a MemoryStream
    // hard-fails at 2 GB regardless).
    internal const long MAX_DECOMPRESSED_SCENE_BYTES = 1024L * 1024 * 1024;

    internal static DccSceneData Unzip(IReadOnlyList<byte> packed)
    {
        var buffer = packed as byte[] ?? [.. packed];

        using var input = new MemoryStream(buffer, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + read > MAX_DECOMPRESSED_SCENE_BYTES)
            {
                throw new InvalidOperationException(
                    $"Render.UnzipDccScene payload exceeds the {MAX_DECOMPRESSED_SCENE_BYTES / (1024 * 1024)} MB decompressed scene limit.");
            }

            output.Write(chunk, 0, read);
        }

        // Deserialize straight from the stream's buffer — ToArray() doubled the peak allocation
        // of large scenes for nothing.
        return MemoryPackSerializer.Deserialize<DccSceneData>(
                   new ReadOnlySpan<byte>(output.GetBuffer(), 0, (int)output.Length))
               ?? throw new InvalidOperationException(
                   "Render.UnzipDccScene payload decompressed but did not deserialize to a DccScene.");
    }

    #endregion
}
