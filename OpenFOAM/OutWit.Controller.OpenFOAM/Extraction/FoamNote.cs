using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// One remark the node attaches to a variant: a value it could not report
/// and why (a probe outside the mesh, a function object that stopped writing
/// before the run ended), how a value was obtained (a reference value that
/// follows the sweep, a post step the solver's own measurement made
/// unnecessary), or what the task does without doing what was meant (ranks
/// without a parallel step, a <c>setFields</c> over a swept field). A note
/// that concerns one response column names it as the result names the value;
/// a note about a whole response or the variant as a whole names none and
/// says what it is about. Notes travel inside <c>FoamResultData.LogTail</c>
/// as note lines (<see cref="FoamNoteLog"/>): the shipped wire layout of the
/// result does not change.
/// </summary>
public sealed class FoamNote : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamNote note)
            return false;

        return Kind.Is(note.Kind)
               && Column.Is(note.Column)
               && Text.Is(note.Text);
    }

    public override FoamNote Clone()
    {
        return new FoamNote
        {
            Kind = Kind,
            Column = Column,
            Text = Text
        };
    }

    public override string ToString()
    {
        return Column == null ? $"{Kind}: {Text}" : $"{Kind} [{Column}]: {Text}";
    }

    #endregion

    #region Properties

    /// <summary>What the note means for the reading of the variant.</summary>
    public FoamNoteKind Kind { get; set; }

    /// <summary>The response column the note is about, as the result names the value; null for a note about a whole response or the variant as a whole.</summary>
    public string? Column { get; set; }

    /// <summary>The note, for the person reading the results.</summary>
    public string Text { get; set; } = string.Empty;

    #endregion
}
