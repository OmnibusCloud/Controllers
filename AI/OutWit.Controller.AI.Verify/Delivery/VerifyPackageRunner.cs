using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// What a node does with one task package, between the download and the upload. First it checks
/// everything it can know before it works: the package was made by this controller version, has
/// a ticket and is within what a node takes, the sandbox starts, and every batch's runtime is
/// here with the pinned bytes. If any of that fails the package is refused whole
/// (<see cref="VerifyBatchRefusedException"/>) and goes to another node; nothing was run, nothing
/// is lost, and nothing is reported as a verdict. Then the batches run, and the results of each
/// are written to the package file as soon as the batch is done: a node holds the results of one
/// batch in memory, never of a package.
/// </summary>
public static class VerifyPackageRunner
{
    #region Constants

    private const string RESULTS_EXTENSION = ".results";

    #endregion

    #region Functions

    /// <summary>
    /// Checks a package's reference before anything is downloaded.
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <exception cref="VerifyBatchRefusedException">This node does not take the package; the message is fixed words.</exception>
    public static void EnsureAcceptable(VerifyTaskPackageData package)
    {
        // One controller version a job: a node on another one would run other code against the
        // same tasks, and its results could not be compared with anyone's.
        if (!string.Equals(package.RequiredControllerVersion, ControllerBuildInfo.VERSION, StringComparison.Ordinal))
            throw new VerifyBatchRefusedException($"this node runs Verify {ControllerBuildInfo.VERSION}; the package was made by {ShownVersion(package.RequiredControllerVersion)}", null);

        if (package.TasksBlobId == Guid.Empty)
            throw new VerifyBatchRefusedException("the package names no task blob", null);

        if (!VerifyTickets.IsWellFormed(package.Ticket))
            throw new VerifyBatchRefusedException("the package has no ticket", null);

        if (package.TaskCount < 1 || package.TaskCount > VerifyPackagePlanner.MAX_PACKAGE_TASKS)
            throw new VerifyBatchRefusedException($"the package names {package.TaskCount} tasks; a node takes from 1 to {VerifyPackagePlanner.MAX_PACKAGE_TASKS}", null);

        if (package.SizeBytes > VerifyPackageCodec.MAX_TASKS_IMAGE_BYTES)
            throw new VerifyBatchRefusedException("the task package is larger than a node takes", null);
    }

    /// <summary>
    /// Checks the batches of a package against this node, then runs them and writes their
    /// results to a package file in the sandbox's own folder.
    /// </summary>
    /// <param name="package">The reference: its ticket goes into every batch of the results.</param>
    /// <param name="batches">The package's task batches.</param>
    /// <param name="sandbox">Gives the node's sandbox; asked once, before any batch runs.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="taskDone">Called after each task.</param>
    /// <param name="logger">Receives what failed on the node for a task without a verdict.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The package file, complete, and what it holds; the caller uploads and deletes it.</returns>
    /// <exception cref="VerifyBatchRefusedException">This node cannot run the package; nothing of it was run.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static VerifyWrittenResults Run(VerifyTaskPackageData package, IReadOnlyList<VerifyTaskBatchData> batches, Func<VerifyWasmSandbox> sandbox,
        string? runtimesRoot, Action? taskDone, ILogger logger, CancellationToken cancellationToken)
    {
        var node = Prepare(batches, sandbox, runtimesRoot);

        Directory.CreateDirectory(node.IoDirectory);
        var path = Path.Combine(node.IoDirectory, $"{Guid.NewGuid():N}{RESULTS_EXTENSION}");
        try
        {
            using var writer = new VerifyResultPackageWriter(path);

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = VerifyBatchExecutor.Execute(node, runtimesRoot, batch, (_, _) => taskDone?.Invoke(), cancellationToken,
                    (index, fault) => logger.LogWarning(fault, "Verify: task {Index} has no verdict on this node", index));

                // How this node ran the batch stays on the node: with a memory cap and a degree
                // of the sender's choosing, the degree gives the node's cores and free memory, and
                // the batch's wall time what else it was doing.
                if (result.Diagnostics != null)
                {
                    result.Diagnostics.Degree = 0;
                    result.Diagnostics.WallMs = 0;
                }

                result.Ticket = package.Ticket;
                writer.Write(result);
            }

            writer.Complete();
            return new VerifyWrittenResults(path, writer.Results, writer.Batches);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    /// <summary>
    /// Everything this node can know about a package's batches before it runs one.
    /// </summary>
    /// <param name="batches">The package's task batches.</param>
    /// <param name="sandbox">Gives the node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <returns>The sandbox the batches will run in.</returns>
    /// <exception cref="VerifyBatchRefusedException">This node cannot run the package.</exception>
    public static VerifyWasmSandbox Prepare(IReadOnlyList<VerifyTaskBatchData> batches, Func<VerifyWasmSandbox> sandbox, string? runtimesRoot)
    {
        VerifyWasmSandbox node;
        try
        {
            node = sandbox();
        }
        catch (InvalidOperationException exception)
        {
            // The host's own guards say why in fixed words (a binding or an engine library that is
            // not the one this controller is built for): no path is in them.
            throw new VerifyBatchRefusedException($"the sandbox cannot start on this node: {exception.Message}", null);
        }

        foreach (var batch in batches)
        {
            VerifyBatchExecutor.EnsureRunnable(runtimesRoot, batch);

            if (VerifyPackagePlanner.OutputBytes(batch) > VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES)
                throw new VerifyBatchRefusedException("a batch may keep more output than a node takes", null);
        }

        return node;
    }

    /// <summary>Deletes a package file; best-effort.</summary>
    /// <param name="path">The file, or null.</param>
    public static void TryDelete(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the sandbox's folder goes as a whole when the client sweeps it
        }
    }

    private static string ShownVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
            return "a controller that names no version";

        var shown = version.Length > 32 ? version[..32] : version;
        return "Verify " + string.Concat(shown.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+' ? character : '?'));
    }

    #endregion
}
