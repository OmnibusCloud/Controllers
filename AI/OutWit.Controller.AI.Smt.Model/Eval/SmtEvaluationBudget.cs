using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// How much one validation may spend before it stops with
/// <see cref="SmtValidationClass.Partial"/>: steps, nesting, and the sizes a value may grow to.
/// Everything is counted by the evaluator itself, never measured by a clock, so the same
/// problem and model end the same way on every machine.
/// </summary>
public sealed class SmtEvaluationBudget : ModelBase
{
    #region Constants

    /// <summary>
    /// The default step budget. A step is one term evaluated, and an operation on a large value
    /// counts as many steps as the machine words it touches, so the budget bounds the time and
    /// the memory of a validation together. Of 224 models that a solver printed for problems
    /// of the SMT-LIB benchmark library (up to half a megabyte of problem and seven megabytes
    /// of model), the costliest took 0.42 million steps; the budget is about fifty times that.
    /// A runaway evaluation uses it up in about half a second of an optimized build.
    /// </summary>
    public const long DEFAULT_STEPS = 20_000_000;

    /// <summary>
    /// The highest nesting ceiling a budget can ask for, and the default one: the evaluation's
    /// thread is sized for it, half a gigabyte of stack of which only what a term really nests
    /// is ever used. It is above the deepest nesting a problem can be read with, so the nesting
    /// of a problem's own terms does not end a validation by itself; the body of a definition
    /// adds its nesting at every call, and a call itself counts two levels. The chains real
    /// terms are written as (nested let, ite, and, or) are evaluated without nesting, and are
    /// not bounded by the ceiling at all.
    /// </summary>
    public const int DEPTH_CEILING = 131_072;

    /// <summary>The default nesting ceiling: the highest one.</summary>
    public const int DEFAULT_DEPTH = DEPTH_CEILING;

    /// <summary>
    /// The default ceiling of an integer, or of the numerator and the denominator of a
    /// rational together, in bits: sixteen times the widest literal a problem may hold.
    /// </summary>
    public const int DEFAULT_INTEGER_BITS = 1_048_576;

    /// <summary>The default ceiling of a bit-vector's width: sixteen times the widest literal a problem may hold.</summary>
    public const int DEFAULT_BIT_VECTOR_BITS = 1_048_576;

    /// <summary>The default ceiling of a string's length: sixteen times the longest literal a problem may hold.</summary>
    public const int DEFAULT_STRING_CHARACTERS = 1_048_576;

    /// <summary>The default ceiling of the indices an array's finite map stores: more than a problem of two million tokens can store one by one.</summary>
    public const int DEFAULT_ARRAY_ENTRIES = 1_048_576;

    #endregion

    #region Model Base

    /// <summary>
    /// Whether another model is a budget of the same numbers.
    /// </summary>
    /// <param name="modelBase">The other model.</param>
    /// <param name="tolerance">Not used: every number is whole.</param>
    /// <returns>True for a budget of the same numbers.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtEvaluationBudget other)
            return false;

        return MaxSteps.Is(other.MaxSteps)
               && MaxDepth.Is(other.MaxDepth)
               && MaxIntegerBits.Is(other.MaxIntegerBits)
               && MaxBitVectorBits.Is(other.MaxBitVectorBits)
               && MaxStringCharacters.Is(other.MaxStringCharacters)
               && MaxArrayEntries.Is(other.MaxArrayEntries);
    }

    /// <summary>
    /// Copies the budget.
    /// </summary>
    /// <returns>A budget of the same numbers.</returns>
    public override SmtEvaluationBudget Clone()
    {
        return new SmtEvaluationBudget
        {
            MaxSteps = MaxSteps,
            MaxDepth = MaxDepth,
            MaxIntegerBits = MaxIntegerBits,
            MaxBitVectorBits = MaxBitVectorBits,
            MaxStringCharacters = MaxStringCharacters,
            MaxArrayEntries = MaxArrayEntries
        };
    }

    #endregion

    #region Properties

    /// <summary>The budget a validation runs under when nothing else is asked for.</summary>
    public static SmtEvaluationBudget Default { get; } = new();

    /// <summary>The most steps of one validation, all assertions together.</summary>
    [ToString]
    public long MaxSteps { get; init; } = DEFAULT_STEPS;

    /// <summary>
    /// The deepest nesting of terms, calls and values. A number above
    /// <see cref="DEPTH_CEILING"/> is taken as that ceiling.
    /// </summary>
    [ToString]
    public int MaxDepth { get; init; } = DEFAULT_DEPTH;

    /// <summary>The most bits of an integer, or of a rational's numerator and denominator together.</summary>
    [ToString]
    public int MaxIntegerBits { get; init; } = DEFAULT_INTEGER_BITS;

    /// <summary>The widest bit-vector, in bits.</summary>
    [ToString]
    public int MaxBitVectorBits { get; init; } = DEFAULT_BIT_VECTOR_BITS;

    /// <summary>The longest string, in characters.</summary>
    [ToString]
    public int MaxStringCharacters { get; init; } = DEFAULT_STRING_CHARACTERS;

    /// <summary>The most indices the finite map of one array stores.</summary>
    [ToString]
    public int MaxArrayEntries { get; init; } = DEFAULT_ARRAY_ENTRIES;

    #endregion
}
