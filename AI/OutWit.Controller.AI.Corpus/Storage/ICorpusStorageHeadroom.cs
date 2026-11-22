namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// Where the controller learns how much room the deployment gives one job in its blob store.
/// The engine's interfaces say nothing about storage; the host implementation
/// (<see cref="CorpusStorageHeadroomHost"/>) asks the server's blob service and the
/// operator's environment, a test supplies its own.
/// </summary>
public interface ICorpusStorageHeadroom
{
    /// <summary>Asks for the deployment's limit.</summary>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>The limit; <see cref="CorpusStorageLimit.NONE"/> when nothing is stated. Never throws for a host that cannot say.</returns>
    Task<CorpusStorageLimit> GetAsync(CancellationToken cancellationToken = default);
}
