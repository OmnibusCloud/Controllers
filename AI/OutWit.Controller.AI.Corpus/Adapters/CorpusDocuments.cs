using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// Writes a job's document on the server without holding it: the document goes to a file of the
/// activity's own scope as it is made, the file is uploaded as the blob, and the scope is removed.
/// A report of a hundred thousand shards is a few hundred megabytes; nothing of that size is a
/// byte array on the way.
/// </summary>
internal static class CorpusDocuments
{
    #region Constants

    private const int BUFFER_BYTES = 1 << 16;

    #endregion

    #region Functions

    /// <summary>Writes a document to a file and uploads it.</summary>
    /// <param name="blobs">Receives the document.</param>
    /// <param name="storage">Gives the scope the file is written in.</param>
    /// <param name="fileName">The blob's file name.</param>
    /// <param name="write">Writes the document to the stream it is given.</param>
    /// <returns>The blob's id.</returns>
    public static async Task<Guid> UploadAsync(IWitBlobService blobs, IWitTempStorage storage, string fileName, Action<Stream> write)
    {
        var scope = storage.CreateScope("corpus-document");
        try
        {
            var path = Path.Combine(scope, fileName);

            // The writing reads the run's blobs as it goes: off the caller's thread.
            await Task.Run(() =>
            {
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_BYTES);
                write(file);
            });

            return await blobs.UploadFileAsync(path);
        }
        finally
        {
            storage.DeleteScope(scope);
        }
    }

    #endregion
}
