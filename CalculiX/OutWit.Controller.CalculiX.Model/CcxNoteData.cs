using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// How much a note changes the reading of the variant.
/// </summary>
public enum CcxNoteKind
{
    /// <summary>The value stands; the note explains how it was obtained.</summary>
    Info = 0,

    /// <summary>A column is missing or the deck was altered before the solve; the note says why.</summary>
    Warning = 1,

    /// <summary>Extraction failed: the response row is not to be trusted as a result.</summary>
    Error = 2
}

/// <summary>
/// One remark the node attaches to a variant: what it changed before the
/// solve (a solver the bundled ccx cannot run), what it could not extract and
/// why (a column refused, a value that was not a number), or how a value was
/// obtained (reaction forces rotated out of a local frame). A note that
/// concerns one response column names it; a note about the variant as a whole
/// names none.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class CcxNoteData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxNoteData note)
            return false;

        return Kind.Is(note.Kind)
               && Column.Is(note.Column)
               && Text.Is(note.Text);
    }

    public override CcxNoteData Clone()
    {
        return new CcxNoteData
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
    [MemoryPackOrder(0)]
    public CcxNoteKind Kind { get; set; }

    /// <summary>The response column the note is about; null for a note about the variant as a whole.</summary>
    [MemoryPackOrder(1)]
    public string? Column { get; set; }

    /// <summary>The note, for the person reading the results.</summary>
    [MemoryPackOrder(2)]
    public string Text { get; set; } = string.Empty;

    #endregion
}
