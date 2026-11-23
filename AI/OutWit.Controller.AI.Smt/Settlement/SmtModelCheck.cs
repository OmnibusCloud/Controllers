using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>What the host's evaluator said of a row's models: the class, in the word a row writes, and the reason when the class is not validated.</summary>
/// <param name="Class">One of <see cref="SmtRunWords.VALIDATIONS"/>.</param>
/// <param name="Reason">The evaluator's reason code (or <see cref="SmtRunWords.OUTPUT_EXCEEDED"/>); empty for validated and for no check.</param>
/// <param name="Detail">The evaluator's detail: for a failed model, which model of an enumeration and which assertion; empty otherwise.</param>
internal sealed record SmtModelCheck(string Class, string Reason, string Detail)
{
    #region Constants

    /// <summary>A row with no model to check.</summary>
    public static readonly SmtModelCheck NONE = new(SmtRunWords.NOT_CHECKED, "", "");

    #endregion

    #region Properties

    /// <summary>Whether the evaluator found an assertion false under a model with nothing guessed.</summary>
    public bool IsFailed => Class == SmtRunWords.FAILED;

    #endregion
}
