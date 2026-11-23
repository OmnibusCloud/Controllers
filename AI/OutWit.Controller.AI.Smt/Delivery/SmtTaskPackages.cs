using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// The host's side of a package of problems: the problems are written to a package file
/// (<see cref="SmtTaskPackageWriter"/>), the file is stored as a blob, and what goes to a node is
/// the reference. Every reference carries a ticket of its own (<see cref="SmtTickets"/>), the
/// version of the controller the job runs on, the digest of the problems it holds, and the run's
/// options. How a problem set is cut into packages is the host's: this makes one package.
/// </summary>
internal static class SmtTaskPackages
{
    #region Functions

    /// <summary>
    /// Writes problems as a package file and stores it as a blob.
    /// </summary>
    /// <param name="problems">The package's problems, in order: 1 to <see cref="SmtPackageCodec.MAX_PACKAGE_PROBLEMS"/>.</param>
    /// <param name="options">The run's options, resolved.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="folder">A folder of the caller's own, where the file is made and deleted once stored.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentException">There are no problems, or more than a node takes.</exception>
    public static async Task<SmtTaskPackageData> PublishAsync(IReadOnlyList<SmtProblemData> problems, SmtOptionsData options, IWitBlobService blobs, string folder)
    {
        if (problems.Count < 1 || problems.Count > SmtPackageCodec.MAX_PACKAGE_PROBLEMS)
            throw new ArgumentException($"a package holds 1 to {SmtPackageCodec.MAX_PACKAGE_PROBLEMS} problems", nameof(problems));

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}-{SmtPackageCodec.PROBLEMS_FILE_NAME}");
        try
        {
            var package = Write(problems, options, path);
            package.ProblemsBlobId = await blobs.UploadFileAsync(path);
            return package;
        }
        finally
        {
            SmtPackageRunner.TryDelete(path);
        }
    }

    /// <summary>
    /// Writes problems as a package file and makes its reference, without a blob yet.
    /// </summary>
    /// <param name="problems">The package's problems, in order.</param>
    /// <param name="options">The run's options, resolved.</param>
    /// <param name="path">The file to write (made new).</param>
    /// <returns>The reference, every member but the blob's id set.</returns>
    public static SmtTaskPackageData Write(IReadOnlyList<SmtProblemData> problems, SmtOptionsData options, string path)
    {
        string digest;
        using (var writer = new SmtTaskPackageWriter(path))
        {
            foreach (var problem in problems)
                writer.Write(problem);

            writer.Complete();
            digest = writer.IndexDigest;
        }

        return new SmtTaskPackageData
        {
            Sha256 = SmtPackageCodec.HashFile(path),
            SizeBytes = new FileInfo(path).Length,
            ProblemCount = problems.Count,
            Ticket = SmtTickets.New(),
            RequiredControllerVersion = ControllerBuildInfo.VERSION,
            IndexDigest = digest,
            Options = options.Clone()
        };
    }

    #endregion
}
