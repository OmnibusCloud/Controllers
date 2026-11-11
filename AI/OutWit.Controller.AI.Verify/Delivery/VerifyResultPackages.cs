using System.Collections;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The host's side of result collection: Grid.ForEach returns references, and the result
/// batches behind them are read from the blobs the nodes uploaded. One package per execution,
/// in no particular order; a package executed twice comes back twice.
/// </summary>
public static class VerifyResultPackages
{
    #region Functions

    /// <summary>Reads the result batches of every package in a Grid.ForEach result.</summary>
    /// <param name="packages">The value of the result collection variable.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The result batches of all executions.</returns>
    /// <exception cref="InvalidDataException">A blob is not the package its reference names.</exception>
    public static async Task<List<VerifyResultBatchData>> LoadAsync(IEnumerable packages, IWitBlobService blobs)
    {
        var batches = new List<VerifyResultBatchData>();

        foreach (var package in packages.OfType<VerifyResultPackageData>())
        {
            if (package.ResultsBlobId == Guid.Empty)
                throw new InvalidDataException("A result package names no blob.");

            var bytes = await File.ReadAllBytesAsync(await blobs.GetLocalPathAsync(package.ResultsBlobId));
            VerifyPackageCodec.EnsureHash(bytes, package.Sha256, "result package");

            batches.AddRange(VerifyPackageCodec.ReadResults(bytes));
        }

        return batches;
    }

    #endregion
}
