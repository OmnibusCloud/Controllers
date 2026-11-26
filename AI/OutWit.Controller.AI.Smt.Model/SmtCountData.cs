using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// One count of a summary: a name (a logic, a refusal code, a query, a stripped command, a
/// licence) and how many times it was met. A list of these is a summary's table, in the order
/// the summary gives it.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtCountData : ModelBase
{
    #region Functions

    /// <summary>Makes a count.</summary>
    /// <param name="name">What is counted.</param>
    /// <param name="count">How many times.</param>
    /// <returns>The count.</returns>
    public static SmtCountData Of(string name, int count)
    {
        return new SmtCountData { Name = name, Count = count };
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtCountData other)
            return false;

        return Name.Is(other.Name)
               && Count.Is(other.Count);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtCountData Clone()
    {
        return new SmtCountData { Name = Name, Count = Count };
    }

    #endregion

    #region Properties

    /// <summary>What is counted.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string Name { get; set; } = "";

    /// <summary>How many times it was met.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int Count { get; set; }

    #endregion
}
