using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>One task of a round with what came back under its ticket, in the order the platform handed it over.</summary>
public sealed class CorpusRoundSlot
{
    #region Constructors

    /// <summary>Creates the slot of a task.</summary>
    /// <param name="task">The task the server made for the shard in this round.</param>
    public CorpusRoundSlot(CorpusTaskData task)
    {
        Task = task;
    }

    #endregion

    #region Properties

    /// <summary>The task the server made.</summary>
    public CorpusTaskData Task { get; }

    /// <summary>The values bound to the task's ticket.</summary>
    public List<CorpusRoundValue> Values { get; } = [];

    #endregion
}
