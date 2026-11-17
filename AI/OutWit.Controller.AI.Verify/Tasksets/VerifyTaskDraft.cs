using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// What a taskset line has given so far while it is read, and how many characters of text:
/// the count is what holds a task to the text it may have before a string is made of more.
/// </summary>
internal sealed class VerifyTaskDraft
{
    #region Functions

    /// <summary>Counts characters of the task's text.</summary>
    /// <param name="characters">The characters.</param>
    public void Count(long characters)
    {
        Characters += characters;
    }

    #endregion

    #region Properties

    /// <summary>The task's index, when the line names one.</summary>
    public int? Index { get; set; }

    /// <summary>The runtime id.</summary>
    public string? Runtime { get; set; }

    /// <summary>The entry, when the line names one.</summary>
    public string? Entry { get; set; }

    /// <summary>The sources, in the line's order.</summary>
    public List<VerifySourceFileData>? Sources { get; set; }

    /// <summary>The arguments.</summary>
    public List<string>? Args { get; set; }

    /// <summary>The standard input of a run without a suite.</summary>
    public string? Stdin { get; set; }

    /// <summary>The seed.</summary>
    public long? Seed { get; set; }

    /// <summary>The cases of the suite.</summary>
    public List<VerifySuiteCaseData>? Suite { get; set; }

    /// <summary>The limits.</summary>
    public VerifyLimitsData? Limits { get; set; }

    /// <summary>A checker's expected stdout.</summary>
    public string? ExpectedStdout { get; set; }

    /// <summary>A checker's expected exit code.</summary>
    public int? ExpectedExit { get; set; }

    /// <summary>The characters of text read so far.</summary>
    public long Characters { get; private set; }

    /// <summary>Whether the line holds more text than a task may have.</summary>
    public bool Over => Characters > VerifyPackagePlanner.MAX_PACKAGE_BYTES;

    #endregion
}
