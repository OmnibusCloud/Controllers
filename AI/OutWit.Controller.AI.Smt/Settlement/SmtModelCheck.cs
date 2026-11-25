using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>What the host's evaluator said of a row's models and of the values that came with them: the class, in the word a row writes, and the reason when the class is not validated.</summary>
/// <param name="Class">One of <see cref="SmtRunWords.VALIDATIONS"/>.</param>
/// <param name="Reason">The evaluator's reason code (or <see cref="SmtRunWords.OUTPUT_EXCEEDED"/>); empty for validated and for no check.</param>
/// <param name="Detail">The evaluator's detail: for a failed class, which model of an enumeration, and which assertion or which of the reported values (the reason says which of the two); empty otherwise.</param>
internal sealed record SmtModelCheck(string Class, string Reason, string Detail)
{
    #region Constants

    /// <summary>A row with no model to check.</summary>
    public static readonly SmtModelCheck NONE = new(SmtRunWords.NOT_CHECKED, "", "");

    #endregion

    #region Properties

    /// <summary>Whether the evaluator found an assertion false under a model, or a reported value another than its term's, with nothing guessed.</summary>
    public bool IsFailed => Class == SmtRunWords.FAILED;

    #endregion
}
