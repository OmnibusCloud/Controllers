using System.Collections;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>What the host-side adapters share: reading their arguments from the job's variables, and handing a section of a ledger out as a blob.</summary>
internal static class SmtAdapterTools
{
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

    /// <summary>The packages of problems a fan-out gave out, from a collection variable.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <param name="reference">The parameter's reference.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <returns>The references, in the collection's order.</returns>
    /// <exception cref="InvalidOperationException">The variable is not a collection.</exception>
    public static List<SmtTaskPackageData> TaskPackages(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        return Items(pool, reference, name).OfType<SmtTaskPackageData>().ToList();
    }

    /// <summary>The result packages a fan-out returned, from a collection variable.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <param name="reference">The parameter's reference.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <returns>The references, in the order they came back.</returns>
    /// <exception cref="InvalidOperationException">The variable is not a collection.</exception>
    public static List<SmtResultPackageData> ResultPackages(IWitVariablesCollection pool, IWitReference? reference, string name)
    {
        return Items(pool, reference, name).OfType<SmtResultPackageData>().ToList();
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

    /// <summary>The options a parameter names; a new instance of the defaults when it names none.</summary>
    /// <param name="pool">The job's variables.</param>
    /// <param name="reference">The parameter's reference.</param>
    /// <returns>The options.</returns>
    public static SmtOptionsData Options(IWitVariablesCollection pool, IWitReference? reference)
    {
        return pool.TryGetValue(reference, out SmtOptionsData? options) && options != null ? options : new SmtOptionsData();
    }

    /// <summary>The options of a run that is to start: in range and of a profile this controller has.</summary>
    /// <param name="options">The options as given.</param>
    /// <exception cref="InvalidOperationException">An option is out of range, or the profile is not this controller's.</exception>
    public static void EnsureRunnable(SmtOptionsData options)
    {
        var problems = SmtOptionsRules.Check(options);
        if (problems.Count > 0)
            throw new InvalidOperationException($"The run's options are refused: {string.Join("; ", problems)}.");

        if (!SmtProfile.Is(options.Profile))
            throw new InvalidOperationException($"The run's options are refused: 'profile' names a profile this controller does not have; it has '{SmtProfile.ID}'.");
    }

    /// <summary>
    /// Stores one section of a ledger as a blob of its own. The section goes from the ledger to a
    /// file and from the file to the blob store, never through memory as a whole.
    /// </summary>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="services">The host's services: the file is made in a working folder of this call's own.</param>
    /// <param name="ledgerPath">The ledger's local file.</param>
    /// <param name="section">Which section of the ledger.</param>
    /// <param name="blobName">The file name the blob is stored under.</param>
    /// <returns>The blob's id.</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static async Task<Guid> UploadSectionAsync(IWitBlobService blobs, IServiceProvider services, string ledgerPath, int section, string blobName)
    {
        using var work = new SmtWorkFolder(services, "smt-document");

        var path = Path.Combine(work.Path, blobName);
        SmtLedger.CopyTo(ledgerPath, section, path);

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
