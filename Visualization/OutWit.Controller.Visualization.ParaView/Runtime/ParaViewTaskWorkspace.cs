using System.Security.Cryptography;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The isolated, task-unique directory tree one render runs in, and the materialization of a
/// task's state and attachment subset into it: every file lands at its logical path under the
/// package root (traversal-guarded), is hashed while copied and rejected on a digest or size
/// mismatch, and nothing outside the subset is ever requested from blob storage. Deleted whole
/// when the task ends, success or not — a retry never sees partial output.
/// </summary>
public sealed class ParaViewTaskWorkspace : IDisposable
{
    #region Constants

    private const string ROOT_LABEL = "witcloud_paraview";

    private const string PACKAGE_DIRECTORY = "package";

    private const string OUTPUT_DIRECTORY = "out";

    private const string HOME_DIRECTORY = "home";

    private const string TEMP_DIRECTORY = "tmp";

    private const string RUNNER_DIRECTORY = "runner";

    private const string PLUGINS_DIRECTORY = "plugins";

    private const string STATE_FILE_NAME = "state.pvsm";

    private const int COPY_BUFFER_BYTES = 1024 * 1024;

    #endregion

    #region Constructors

    private ParaViewTaskWorkspace(string root)
    {
        Root = root;
        PackageRoot = Path.Combine(root, PACKAGE_DIRECTORY);
        OutputDirectory = Path.Combine(root, OUTPUT_DIRECTORY);
        HomeDirectory = Path.Combine(root, HOME_DIRECTORY);
        TempDirectory = Path.Combine(root, TEMP_DIRECTORY);
        RunnerDirectory = Path.Combine(root, RUNNER_DIRECTORY);
        PluginsDirectory = Path.Combine(root, PLUGINS_DIRECTORY);
        StatePath = Path.Combine(root, STATE_FILE_NAME);
        TaskFilePath = Path.Combine(root, ParaViewRunnerTask.FILE_NAME);
        StatusFilePath = Path.Combine(root, ParaViewRunnerStatus.FILE_NAME);
        ComposeTaskFilePath = Path.Combine(root, ParaViewComposeTask.FILE_NAME);
        ComposeStatusFilePath = Path.Combine(root, ParaViewComposeStatus.FILE_NAME);

        foreach (var directory in new[] { PackageRoot, OutputDirectory, HomeDirectory, TempDirectory, RunnerDirectory, PluginsDirectory })
            Directory.CreateDirectory(directory);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a fresh workspace for one task under the node's temp storage.
    /// </summary>
    /// <param name="tempStorage">Node temp storage.</param>
    /// <param name="jobId">Job identifier.</param>
    /// <param name="taskIndex">Task ordinal.</param>
    /// <returns>The workspace.</returns>
    public static ParaViewTaskWorkspace Create(IWitTempStorage tempStorage, Guid jobId, int taskIndex)
    {
        // Short unique suffix: deep logical paths sit below this root and Windows path length is finite.
        var root = Path.Combine(tempStorage.RootPath, ROOT_LABEL, jobId.ToString("N"), $"task_{taskIndex:D6}_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(root);
        return new ParaViewTaskWorkspace(root);
    }

    /// <summary>
    /// Materializes the task's state and attachment subset.
    /// </summary>
    /// <param name="blobService">Blob storage.</param>
    /// <param name="task">The task.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The number of attachments materialized.</returns>
    /// <exception cref="InvalidOperationException">A digest, size or path rule is violated.</exception>
    public async Task<int> MaterializeAsync(IWitBlobService blobService, ParaViewRenderTaskData task, CancellationToken cancellationToken)
    {
        var stateSource = await blobService.GetLocalPathAsync(task.StateBlobId);
        await CopyVerifiedAsync(stateSource, StatePath, task.StateSha256, task.StateSize, "state", cancellationToken);

        var materialized = 0;
        foreach (var attachment in task.Attachments)
        {
            await MaterializeAttachmentAsync(blobService, attachment, cancellationToken);
            materialized++;
        }

        return materialized;
    }

    /// <summary>
    /// Materializes one attachment at its logical path under the package root: traversal-guarded,
    /// hashed while copied, rejected on a declared digest or size mismatch. An attachment declared
    /// without a digest (a data scene the composer stamps) is copied and its actual digest returned.
    /// </summary>
    /// <param name="blobService">Blob storage.</param>
    /// <param name="attachment">The attachment.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The materialized path with the digest and size the copy actually had.</returns>
    /// <exception cref="InvalidOperationException">A digest, size or path rule is violated.</exception>
    public async Task<ParaViewMaterializedAttachment> MaterializeAttachmentAsync(IWitBlobService blobService, ParaViewAttachmentRefData attachment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ParaViewLogicalPath.ResolveUnder(PackageRoot, attachment.LogicalPath);
        if (File.Exists(target))
            throw new InvalidOperationException($"attachment '{attachment.LogicalPath}' would overwrite an already materialized file");

        var directory = Path.GetDirectoryName(target)
                        ?? throw new InvalidOperationException($"attachment '{attachment.LogicalPath}' has no parent directory");
        Directory.CreateDirectory(directory);

        var source = await blobService.GetLocalPathAsync(attachment.BlobId);
        var (sha256, size) = await CopyVerifiedAsync(source, target, attachment.Sha256, attachment.Size, attachment.LogicalPath, cancellationToken);
        return new ParaViewMaterializedAttachment(attachment.LogicalPath, target, sha256, size);
    }

    /// <summary>
    /// Writes an embedded controller resource into the workspace.
    /// </summary>
    /// <param name="resourceName">Embedded resource name.</param>
    /// <param name="directory">Target directory (runner or plugins).</param>
    /// <param name="fileName">Target file name.</param>
    /// <returns>The written path.</returns>
    /// <exception cref="InvalidOperationException">The resource is absent from this controller build.</exception>
    public string WriteEmbedded(string resourceName, string directory, string fileName)
    {
        var text = ParaViewRuntimeInfo.ReadEmbeddedText(resourceName)
                   ?? throw new InvalidOperationException($"this ParaView controller build carries no '{resourceName}'");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// The output path of the task inside the output directory.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>Absolute output path.</returns>
    public string OutputPathFor(ParaViewRenderTaskData task)
    {
        // A camera move renders several outputs of one timestep: the orbit position keeps their
        // file (and blob) names distinct.
        var name = task.Options.Turntable == null
            ? $"frame_{task.TimestepIndex:D6}"
            : $"frame_{task.TimestepIndex:D6}_{task.OrbitIndex:D4}";
        return Path.Combine(OutputDirectory, $"{name}.{ParaViewImageFormats.Extension(task.Options.Format)}");
    }

    #endregion

    #region Tools

    private static async Task<(string Sha256, long Size)> CopyVerifiedAsync(string source, string target, string expectedSha256, long expectedSize, string label, CancellationToken cancellationToken)
    {
        var sourceInfo = new FileInfo(source);
        if (!sourceInfo.Exists)
            throw new InvalidOperationException($"{label}: blob storage resolved no local file");

        if (expectedSize > 0 && sourceInfo.Length != expectedSize)
            throw new InvalidOperationException($"{label}: declared {expectedSize} bytes, blob holds {sourceInfo.Length}");

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, COPY_BUFFER_BYTES, useAsync: true))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, COPY_BUFFER_BYTES, useAsync: true))
        {
            var buffer = new byte[COPY_BUFFER_BYTES];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        var actual = Convert.ToHexStringLower(hasher.GetHashAndReset());
        if (ParaViewPackageDigest.IsSha256Hex(expectedSha256) && !string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new InvalidOperationException($"{label}: content digest mismatch (declared {expectedSha256}, got {actual})");
        }

        return (actual, sourceInfo.Length);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Deletes the whole workspace (best effort; a node temp sweep reclaims anything still locked).
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best effort: the runner may still hold a handle for a moment; the temp sweep reclaims it.
        }
    }

    #endregion

    #region Properties

    /// <summary>Task-unique root directory.</summary>
    public string Root { get; }

    /// <summary>Package root every logical path resolves under.</summary>
    public string PackageRoot { get; }

    /// <summary>Directory holding exactly the task's output.</summary>
    public string OutputDirectory { get; }

    /// <summary>Task-private HOME.</summary>
    public string HomeDirectory { get; }

    /// <summary>Task-private TEMP.</summary>
    public string TempDirectory { get; }

    /// <summary>Directory the runner script is written to.</summary>
    public string RunnerDirectory { get; }

    /// <summary>The only plugin directory the runner consults.</summary>
    public string PluginsDirectory { get; }

    /// <summary>Materialized state path.</summary>
    public string StatePath { get; }

    /// <summary>Task file path.</summary>
    public string TaskFilePath { get; }

    /// <summary>Status file path.</summary>
    public string StatusFilePath { get; }

    /// <summary>Compose task file path (ParaView.Compose).</summary>
    public string ComposeTaskFilePath { get; }

    /// <summary>Compose status file path (ParaView.Compose).</summary>
    public string ComposeStatusFilePath { get; }

    #endregion
}
