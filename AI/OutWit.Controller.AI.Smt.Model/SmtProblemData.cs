using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// One accepted problem of a problem set, as it travels to a node: the problem's canonical text
/// and what is asked of it. Every text here was written by the one writer of s-expressions, so
/// a node that sanitizes it again gets the same problem and the same terms.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order: append new members at the END only.
public sealed partial class SmtProblemData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtProblemData other)
            return false;

        return Index.Is(other.Index)
               && Id.Is(other.Id)
               && Query.Is(other.Query)
               && Text.Is(other.Text)
               && Hash.Is(other.Hash)
               && Values.Is(other.Values)
               && Project.Is(other.Project)
               && MaxModels.Is(other.MaxModels)
               && Goal.Is(other.Goal)
               && Expected.Is(other.Expected)
               && Tags.Is(other.Tags);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtProblemData Clone()
    {
        return new SmtProblemData
        {
            Index = Index,
            Id = Id,
            Query = Query,
            Text = Text,
            Hash = Hash,
            Values = [.. Values],
            Project = [.. Project],
            MaxModels = MaxModels,
            Goal = Goal,
            Expected = Expected,
            Tags = Tags
        };
    }

    #endregion

    #region Properties

    /// <summary>The ordinal of the problem's row among the rows of its set, from 0: accepted and refused rows are counted, blank lines are not.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int Index { get; set; }

    /// <summary>The row's id: the name the lab gave the problem, once in its set.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public string Id { get; set; } = "";

    /// <summary>What is asked of the problem.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public SmtQuery Query { get; set; }

    /// <summary>The problem's canonical text (<see cref="SmtProblem.Canonical"/>).</summary>
    [MemoryPackOrder(3)]
    public string Text { get; set; } = "";

    /// <summary>The SHA-256 of the canonical text in lower-case hexadecimal: the name of the problem.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public string Hash { get; set; } = "";

    /// <summary>The terms whose values are asked of a <see cref="SmtQuery.Check"/>, each as its canonical text; empty for a model as a whole and for the other queries.</summary>
    [MemoryPackOrder(5)]
    public List<string> Values { get; set; } = [];

    /// <summary>The constants the models of an <see cref="SmtQuery.Enumerate"/> are told apart by, each as the problem declares it; empty for the other queries.</summary>
    [MemoryPackOrder(6)]
    public List<string> Project { get; set; } = [];

    /// <summary>The most models an <see cref="SmtQuery.Enumerate"/> looks for; 0 for the other queries.</summary>
    [ToString]
    [MemoryPackOrder(7)]
    public int MaxModels { get; set; }

    /// <summary>The goal of a <see cref="SmtQuery.Prove"/> as its canonical text; empty for the other queries.</summary>
    [MemoryPackOrder(8)]
    public string Goal { get; set; } = "";

    /// <summary>
    /// The verdict the lab expects: the row's own when it names one, else the problem's
    /// <c>:status</c>. Always <see cref="SmtProblemStatus.None"/> for a <see cref="SmtQuery.Prove"/>:
    /// a proof's verdict is about the goal, not about the problem.
    /// </summary>
    [ToString]
    [MemoryPackOrder(9)]
    public SmtProblemStatus Expected { get; set; }

    /// <summary>The row's tags, passed through as they are; empty when it has none.</summary>
    [MemoryPackOrder(10)]
    public string Tags { get; set; } = "";

    #endregion
}
