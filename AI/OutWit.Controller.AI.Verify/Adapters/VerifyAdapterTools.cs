using System.Collections;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

/// <summary>What the host-side adapters share: reading their collections from the job's variables, opening a taskset, and handing a document of a ledger out as a blob.</summary>
internal static class VerifyAdapterTools
{
    #region Constants

    private const int READ_BUFFER_BYTES = 128 * 1024;

    #endregion

    #region Functions

    /// <summary>A parameter of an activity that must be a variable reference.</summary>
    /// <param name="parameters">The activity's parameters.</param>
    /// <param name="index">The parameter's place.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentException">The parameter is not a variable reference.</exception>
    public static IWitReference Reference(IWitParameter[] parameters, int index, string name)
    {
        return parameters[index] as IWitReference ?? throw new ArgumentException($"Parameter '{name}' must be a variable reference.");
    }

    /// <summary>The task packages a fan-out gave out, from a collection variable.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <param name="reference">The parameter's reference.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <returns>The references, in the collection's order.</returns>
    /// <exception cref="InvalidOperationException">The variable is not a collection.</exception>
    public static List<VerifyTaskPackageData> TaskPackages(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        return Items(pool, reference, name).OfType<VerifyTaskPackageData>().ToList();
    }

    /// <summary>The result packages a fan-out returned, from a collection variable.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <param name="reference">The parameter's reference.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <returns>The references, in the order they came back.</returns>
    /// <exception cref="InvalidOperationException">The variable is not a collection.</exception>
    public static List<VerifyResultPackageData> ResultPackages(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        return Items(pool, reference, name).OfType<VerifyResultPackageData>().ToList();
    }

    /// <summary>The blob a parameter names.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <param name="reference">The parameter's reference.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <returns>The blob's id.</returns>
    /// <exception cref="InvalidOperationException">The variable names no blob.</exception>
    public static Guid Blob(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        if (!pool.TryGetValue(reference, out Guid blobId) || blobId == Guid.Empty)
            throw new InvalidOperationException($"Failed to get parameter '{name}'.");

        return blobId;
    }

    /// <summary>Opens a taskset blob's local file to be read from its start to its end, once.</summary>
    /// <param name="path">The blob's local path.</param>
    /// <returns>The stream; the caller disposes it.</returns>
    public static FileStream OpenTaskset(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, READ_BUFFER_BYTES, FileOptions.SequentialScan);
    }

    /// <summary>
    /// Stores one document of a ledger as a blob of its own. The document goes from the ledger
    /// to a file and from the file to the blob store, never through memory as a whole: a report
    /// has a record a task, and the outputs are as large as the run.
    /// </summary>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="services">The host's services: the document's file is made in a working folder of this call's own.</param>
    /// <param name="ledgerPath">The ledger's local file.</param>
    /// <param name="section">Which document of the ledger.</param>
    /// <param name="blobName">The file name the blob is stored under.</param>
    /// <returns>The blob's id.</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static async Task<Guid> UploadDocumentAsync(IWitBlobService blobs, IServiceProvider services, string ledgerPath, int section, string blobName)
    {
        using var work = new VerifyWorkFolder(services);

        var path = Path.Combine(work.Path, blobName);
        VerifyLedger.CopyTo(ledgerPath, section, path);

        return await blobs.UploadFileAsync(path);
    }

    private static IEnumerable Items(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        if (!pool.TryGetObject(reference, out var value) || value is not IEnumerable items)
            throw new InvalidOperationException($"Failed to get parameter '{name}'.");

        return items;
    }

    #endregion
}
