using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// Why a problem, a term beside it, or a row of a problem set was not taken: where, which
/// command, a fixed code and a sentence a person can act on. It never carries the problem's
/// text or the row: of them it holds a command name and, in the reason, the name of a field or
/// at most one token cut to 64 characters.
/// <para>
/// In a problem set <see cref="Row"/> is the line of the set, <see cref="File"/> the row's id
/// and <see cref="Line"/> the line inside the problem's text (0 when the refusal is about the
/// row itself).
/// </para>
/// <para>
/// Published into the platform's job document vocabulary as <c>smt.refusal@1</c>, as a part of the
/// preflight's document.
/// </para>
/// </summary>
[JobDocumentContract("smt.refusal@1")]
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtRefusalData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtRefusalData other)
            return false;

        return File.Is(other.File)
               && Line.Is(other.Line)
               && Command.Is(other.Command)
               && Code.Is(other.Code)
               && Reason.Is(other.Reason)
               && Row.Is(other.Row);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtRefusalData Clone()
    {
        return new SmtRefusalData { File = File, Line = Line, Command = Command, Code = Code, Reason = Reason, Row = Row };
    }

    #endregion

    #region Properties

    /// <summary>
    /// What the problem is called: in a problem set the row's id (empty when the row has no
    /// readable id), elsewhere the file the problem came from as whoever read it names it, or empty.
    /// </summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string File { get; set; } = "";

    /// <summary>
    /// The line of the problem's text, or of a term's, the refusal is about (1-based): where the
    /// token or the command it names starts; 1 when it is about the text as a whole. 0 when the
    /// refusal is about a row of a problem set and not about a text in it.
    /// </summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int Line { get; set; }

    /// <summary>The name of the command the refusal is about, or empty: never more of the text than that.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public string Command { get; set; } = "";

    /// <summary>The fixed code of the refusal (<c>command_not_allowed</c>, <c>syntax_error</c>, ...).</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public string Code { get; set; } = "";

    /// <summary>What is wrong, in a sentence a person can act on.</summary>
    [MemoryPackOrder(4)]
    public string Reason { get; set; } = "";

    /// <summary>The line of the problem set the refusal belongs to (1-based); 0 outside a problem set.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public int Row { get; set; }

    #endregion
}
