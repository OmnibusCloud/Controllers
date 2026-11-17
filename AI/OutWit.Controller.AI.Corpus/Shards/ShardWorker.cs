using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The thread a shard is processed on: one of its own for the whole pass, below normal priority
/// (a worker is often a desktop: the minutes of CPU a shard takes run below its owner's
/// foreground work), and made the way the wasm host makes the threads that can hold guest code
/// (<see cref="WasmHostGuestThread.Create"/>). The PDF sandbox runs a guest on the calling thread
/// when that thread can hold it, and on a thread of normal priority made for the call otherwise:
/// on this one the extractor, the heaviest work of a WARC shard, runs below normal priority too,
/// and no thread is made per PDF.
/// </summary>
internal static class ShardWorker
{
    #region Constants

    private const string THREAD_NAME = "corpus-shard";

    #endregion

    #region Functions

    /// <summary>Runs a shard's pass on a thread of its own.</summary>
    /// <typeparam name="T">What the pass returns.</typeparam>
    /// <param name="work">The pass.</param>
    /// <returns>What the pass returned; its exception, when it threw one.</returns>
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = WasmHostGuestThread.Create(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (OperationCanceledException exception)
            {
                completion.SetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        thread.IsBackground = true;
        thread.Name = THREAD_NAME;
        thread.Priority = ThreadPriority.BelowNormal;
        thread.Start();

        return completion.Task;
    }

    #endregion

    #region Properties

    /// <summary>Whether the current thread is one a shard runs on (below normal priority): what a test of the PDF stage's priority asks.</summary>
    public static bool IsCurrent => Thread.CurrentThread.Name == THREAD_NAME;

    #endregion
}
