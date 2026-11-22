using System.Runtime.CompilerServices;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The accounting of one validation: the steps used, the nesting reached, the size ceilings, and
/// whether a symbol the model left out was given a value. Every loop of the evaluator pays here
/// before it works, so a validation ends within its budget whatever the input is.
/// <para>
/// A step is one term evaluated; an operation on a large value pays as many steps as the
/// machine words it touches, and a value pays for the memory it takes when it is made. The
/// steps therefore bound the time and the memory of a validation together.
/// </para>
/// </summary>
internal sealed class SmtMeter
{
    #region Constants

    /// <summary>The bits of one machine word: an operation on a large number pays a step a word.</summary>
    public const int WORD_BITS = 64;

    /// <summary>
    /// What a term that was not evaluated pays when a connective goes on past it. Raising and
    /// catching the exception was measured at the time of about 150 steps; it is priced higher.
    /// </summary>
    public const int PARTIAL_STEPS = 256;

    /// <summary>
    /// What each level of nesting between such a term and the connective that goes on past it
    /// pays. Unwinding one level was measured at the time of about 13 steps; it is priced higher.
    /// </summary>
    public const int UNWIND_STEPS = 32;

    #endregion

    #region Fields

    private readonly long m_maxSteps;
    private readonly int m_maxDepth;
    private readonly int m_maxIntegerBits;
    private readonly int m_maxBitVectorBits;
    private readonly int m_maxStringCharacters;
    private readonly int m_maxArrayEntries;
    private long m_steps;
    private int m_depth;

    #endregion

    #region Constructors

    /// <summary>
    /// Starts the accounting of one validation.
    /// </summary>
    /// <param name="budget">What the validation may spend.</param>
    public SmtMeter(SmtEvaluationBudget budget)
    {
        m_maxSteps = Math.Max(0, budget.MaxSteps);
        m_maxDepth = Math.Clamp(budget.MaxDepth, 1, SmtEvaluationBudget.DEPTH_CEILING);
        m_maxIntegerBits = Math.Max(1, budget.MaxIntegerBits);
        m_maxBitVectorBits = Math.Max(1, budget.MaxBitVectorBits);
        m_maxStringCharacters = Math.Max(0, budget.MaxStringCharacters);
        m_maxArrayEntries = Math.Max(0, budget.MaxArrayEntries);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Pays for work before it is done.
    /// </summary>
    /// <param name="steps">The steps the work is worth; a negative number is taken as more than any budget.</param>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public void Charge(long steps)
    {
        if (steps < 0 || steps > m_maxSteps - m_steps)
        {
            m_steps = m_maxSteps;
            throw new SmtPartialException(SmtPartialKind.Terminal, SmtValidationReasons.BUDGET_STEPS, null, m_depth);
        }

        m_steps += steps;
    }

    /// <summary>
    /// Pays for work on a number: a step a machine word.
    /// </summary>
    /// <param name="bits">The bits the work reads or writes.</param>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public void ChargeBits(long bits)
    {
        Charge(1 + bits / WORD_BITS);
    }

    /// <summary>
    /// Pays for work that multiplies or divides two numbers: the product of their sizes in
    /// machine words, which bounds the schoolbook methods.
    /// </summary>
    /// <param name="leftBits">The bits of one number.</param>
    /// <param name="rightBits">The bits of the other.</param>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public void ChargeProduct(long leftBits, long rightBits)
    {
        Charge(Product(1 + leftBits / WORD_BITS, 1 + rightBits / WORD_BITS));
    }

    /// <summary>
    /// Enters one more level of nesting: a term in a term, a call, a value in a value.
    /// </summary>
    /// <param name="depth">The level entered: 1 for an assertion's own term.</param>
    /// <exception cref="SmtPartialException">The nesting is deeper than the ceiling.</exception>
    public void Descend(int depth)
    {
        m_depth = depth;

        // The count is the rule: the same input ends at the same level on every machine. The
        // stack check is a second line behind it, for a thread smaller than the one the
        // evaluator makes for itself.
        if (depth > m_maxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new SmtPartialException(SmtPartialKind.Terminal, SmtValidationReasons.BUDGET_DEPTH, null, depth);
    }

    /// <summary>
    /// Checks an integer, or a rational's numerator and denominator together, against its ceiling.
    /// </summary>
    /// <param name="bits">The bits the number has, or will have.</param>
    /// <exception cref="SmtPartialException">The number is past the ceiling.</exception>
    public void CheckIntegerBits(long bits)
    {
        if (bits > m_maxIntegerBits)
            throw TooLarge();
    }

    /// <summary>
    /// Checks a bit-vector's width against its ceiling.
    /// </summary>
    /// <param name="width">The width the bit-vector has, or will have.</param>
    /// <exception cref="SmtPartialException">The width is past the ceiling.</exception>
    public void CheckBitVectorBits(long width)
    {
        if (width > m_maxBitVectorBits)
            throw TooLarge();
    }

    /// <summary>
    /// Checks a string's length against its ceiling.
    /// </summary>
    /// <param name="characters">The characters the string has, or will have.</param>
    /// <exception cref="SmtPartialException">The length is past the ceiling.</exception>
    public void CheckStringCharacters(long characters)
    {
        if (characters > m_maxStringCharacters)
            throw TooLarge();
    }

    /// <summary>
    /// Checks the indices an array stores against their ceiling.
    /// </summary>
    /// <param name="entries">The indices the array stores, or will store.</param>
    /// <exception cref="SmtPartialException">The count is past the ceiling.</exception>
    public void CheckArrayEntries(long entries)
    {
        if (entries > m_maxArrayEntries)
            throw TooLarge();
    }

    /// <summary>
    /// Makes the exception of a term whose value the evaluator does not compute.
    /// </summary>
    /// <param name="reason">A code of <see cref="SmtValidationReasons"/>.</param>
    /// <param name="detail">The name of an operator from the evaluator's vocabulary, or null.</param>
    /// <returns>The exception, to throw.</returns>
    public SmtPartialException Undecided(string reason, string? detail = null)
    {
        return new SmtPartialException(SmtPartialKind.Undecided, reason, detail, m_depth);
    }

    /// <summary>
    /// Makes the exception of a term the evaluator cannot make sense of.
    /// </summary>
    /// <param name="reason">A code of <see cref="SmtValidationReasons"/>.</param>
    /// <returns>The exception, to throw.</returns>
    public SmtPartialException Doubt(string reason = SmtValidationReasons.ILL_FORMED_TERM)
    {
        return new SmtPartialException(SmtPartialKind.Doubt, reason, null, m_depth);
    }

    /// <summary>
    /// Makes the exception of a value nested deeper than the place it is used in allows.
    /// </summary>
    /// <returns>The exception, to throw.</returns>
    public SmtPartialException TooDeep()
    {
        return new SmtPartialException(SmtPartialKind.Terminal, SmtValidationReasons.BUDGET_DEPTH, null, m_depth);
    }

    /// <summary>
    /// Pays for going on past a term that was not evaluated: what raising the exception and
    /// unwinding to the handler cost.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    /// <param name="depth">The level of the handler.</param>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public void ChargeCaught(SmtPartialException exception, int depth)
    {
        Charge(PARTIAL_STEPS + (long)Math.Max(0, exception.Depth - depth) * UNWIND_STEPS);
    }

    /// <summary>
    /// Pays for an exception that reached the top of an assertion, without raising another.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    /// <param name="depth">The level of the handler.</param>
    /// <returns>False when the step budget is used up by it.</returns>
    public bool TryChargeCaught(SmtPartialException exception, int depth)
    {
        var steps = PARTIAL_STEPS + (long)Math.Max(0, exception.Depth - depth) * UNWIND_STEPS;
        if (steps > m_maxSteps - m_steps)
        {
            m_steps = m_maxSteps;
            return false;
        }

        m_steps += steps;
        return true;
    }

    /// <summary>
    /// Records that a symbol the model left out was given a value: whatever is evaluated from
    /// here on in the same assertion may rest on it.
    /// </summary>
    public void Complete()
    {
        Completions++;
    }

    private SmtPartialException TooLarge()
    {
        return new SmtPartialException(SmtPartialKind.Terminal, SmtValidationReasons.BUDGET_SIZE, null, m_depth);
    }

    private static long Product(long left, long right)
    {
        return left > long.MaxValue / right ? long.MaxValue : left * right;
    }

    #endregion

    #region Properties

    /// <summary>The steps used so far.</summary>
    public long Steps => m_steps;

    /// <summary>
    /// How many times a symbol the model left out was read. It only grows: an evaluation rests
    /// on such a symbol exactly when the count moved while it ran.
    /// </summary>
    public long Completions { get; private set; }

    #endregion
}
