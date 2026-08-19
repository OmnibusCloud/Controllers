using System.Text;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;

namespace OutWit.Controller.Visualization.ParaView.Tests.Utils;

/// <summary>
/// Builds a complete on-disk package for a test: writes the state and every attachment into a
/// directory, registers them in the test blob service, and produces the matching
/// <see cref="ParaViewSceneRefData"/> with correct digests, sizes and series metadata.
/// </summary>
internal sealed class ParaViewPackageBuilder
{
    #region Fields

    private readonly string m_directory;

    private readonly ParaViewTestBlobService m_blobService;

    private readonly List<ParaViewAttachmentRefData> m_attachments = [];

    private readonly Dictionary<string, Guid> m_blobsByPath = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    public ParaViewPackageBuilder(string directory, ParaViewTestBlobService blobService)
    {
        m_directory = directory;
        m_blobService = blobService;
        Directory.CreateDirectory(directory);
    }

    #endregion

    #region Functions

    /// <summary>Adds a file with synthetic text content at a logical path.</summary>
    public ParaViewPackageBuilder AddFile(
        string logicalPath,
        string content,
        ParaViewAttachmentRole role = ParaViewAttachmentRole.ReaderInput,
        string seriesGroup = "",
        int[]? timestepIndices = null,
        int seriesOrdinal = 0)
    {
        return AddFile(logicalPath, new UTF8Encoding(false).GetBytes(content), role, seriesGroup, timestepIndices, seriesOrdinal);
    }

    /// <summary>Adds a file with binary content at a logical path.</summary>
    public ParaViewPackageBuilder AddFile(
        string logicalPath,
        byte[] content,
        ParaViewAttachmentRole role,
        string seriesGroup,
        int[]? timestepIndices,
        int seriesOrdinal)
    {
        // Files are stored flat by ordinal so a logical path that is a directory prefix of another
        // (a deliberately invalid package) can still be registered as a blob.
        var path = Path.Combine(m_directory, "files", $"{m_attachments.Count:D4}_{Path.GetFileName(logicalPath)}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);

        var blobId = m_blobService.RegisterExistingFile(path);
        m_blobsByPath[logicalPath] = blobId;
        m_attachments.Add(new ParaViewAttachmentRefData
        {
            BlobId = blobId,
            LogicalPath = logicalPath,
            Sha256 = ParaViewPackageDigest.HashFile(path),
            Size = new FileInfo(path).Length,
            Role = role,
            SeriesGroup = seriesGroup,
            TimestepIndices = timestepIndices == null ? [] : [.. timestepIndices],
            SeriesOrdinal = seriesOrdinal
        });
        return this;
    }

    /// <summary>Writes the state and produces the scene reference.</summary>
    public ParaViewSceneRefData BuildScene(string stateXml, int major = ParaViewRuntimeInfo.RUNTIME_MAJOR, int minor = ParaViewRuntimeInfo.RUNTIME_MINOR, IEnumerable<double>? timestepValues = null, IEnumerable<ParaViewPluginRequirementData>? plugins = null)
    {
        var statePath = Path.Combine(m_directory, "state.pvsm");
        File.WriteAllText(statePath, stateXml, new UTF8Encoding(false));

        var stateBlobId = m_blobService.RegisterExistingFile(statePath);
        StateBlobId = stateBlobId;

        return new ParaViewSceneRefData
        {
            StateBlobId = stateBlobId,
            StateSha256 = ParaViewPackageDigest.HashFile(statePath),
            StateSize = new FileInfo(statePath).Length,
            Attachments = m_attachments.Select(me => (ParaViewAttachmentRefData)me.Clone()).ToList(),
            Runtime = new ParaViewRuntimeRequirementData
            {
                ParaViewMajor = major,
                ParaViewMinor = minor,
                ParaViewPatch = ParaViewRuntimeInfo.RUNTIME_PATCH,
                ProducerPluginVersion = "0.0.0-test",
                ProducerPlatform = "test",
                Plugins = plugins == null ? [] : [.. plugins]
            },
            TimestepValues = timestepValues == null ? [] : [.. timestepValues]
        };
    }

    public Guid BlobOf(string logicalPath)
    {
        return m_blobsByPath[logicalPath];
    }

    #endregion

    #region Properties

    public Guid StateBlobId { get; private set; }

    public IReadOnlyList<ParaViewAttachmentRefData> Attachments => m_attachments;

    #endregion
}
