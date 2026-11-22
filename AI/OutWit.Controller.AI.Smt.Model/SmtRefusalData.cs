using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// Why a problem, or a term beside it, was not taken: where, which command, a fixed code and a
/// sentence a person can act on. It never carries the problem's text: of the text it holds a
/// command name and, in the reason, at most one token cut to 64 characters.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order: append new members at the END only.
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
               && Reason.Is(other.Reason);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtRefusalData Clone()
    {
        return new SmtRefusalData { File = File, Line = Line, Command = Command, Code = Code, Reason = Reason };
    }

    #endregion

    #region Properties

    /// <summary>The file the problem came from, as whoever read the file names it; empty when the text had no file.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string File { get; set; } = "";

    /// <summary>The line the refusal is about (1-based): where the token or the command it names starts; 1 when it is about the text as a whole.</summary>
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

    #endregion
}
