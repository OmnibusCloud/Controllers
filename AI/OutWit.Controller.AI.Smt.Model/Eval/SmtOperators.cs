namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The theory operators on values. Every operator checks the number and the sorts of its
/// arguments before it computes: an application that does not fit the operator's signature in
/// the standard is a term the evaluator cannot make sense of, never a value. Solvers take more
/// than the standard in places; where the evaluator does not know that they all read such an
/// application alike, it does not read it at all.
/// </summary>
internal sealed class SmtOperators
{
    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the operators of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    public SmtOperators(SmtRun run)
    {
        m_run = run;
        m_meter = run.Meter;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Applies an operator to values.
    /// </summary>
    /// <param name="known">The operator.</param>
    /// <param name="arguments">The values of the arguments, at least one.</param>
    /// <param name="indices">The indices of an indexed operator; none for any other.</param>
    /// <param name="depth">The level of nesting the application is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application does not fit the operator, or its value is left to the model.</exception>
    public SmtValue Apply(SmtOperator known, SmtValue[] arguments, int[] indices, int depth)
    {
        switch (known)
        {
            case SmtOperator.Not when arguments.Length == 1:
                return SmtBoolValue.Of(!Truth(arguments[0]));
            case SmtOperator.Xor when arguments.Length >= 2:
                return Xor(arguments);
            case SmtOperator.Equal when arguments.Length >= 2:
                return Equal(arguments, depth);
            case SmtOperator.Distinct:
                return SmtBoolValue.Of(m_run.Equality.Distinct(arguments, depth));
            case SmtOperator.Select when arguments.Length == 2:
                return m_run.Arrays.Select(arguments[0], arguments[1], depth);
            case SmtOperator.Store when arguments.Length == 3:
                return m_run.Arrays.Store(arguments[0], arguments[1], arguments[2], depth);
            case >= SmtOperator.Add and <= SmtOperator.Divisible:
                return SmtArithmetic.Apply(known, arguments, indices, m_meter);
            case >= SmtOperator.Concat and <= SmtOperator.BvToInt:
                return SmtBitVectors.Apply(known, arguments, m_meter);
            case >= SmtOperator.Extract and <= SmtOperator.IntToBv:
                return SmtBitVectorResizing.Apply(known, arguments, indices, m_meter);
            case >= SmtOperator.StrConcat and <= SmtOperator.StrFromCode:
                return SmtStrings.Apply(known, arguments, m_meter);
            default:
                throw m_meter.Doubt();
        }
    }

    private bool Truth(SmtValue value)
    {
        return value is SmtBoolValue truth ? truth.Value : throw m_meter.Doubt();
    }

    private SmtValue Xor(SmtValue[] arguments)
    {
        var truth = false;
        foreach (var argument in arguments)
            truth ^= Truth(argument);

        return SmtBoolValue.Of(truth);
    }

    /// <summary>
    /// <c>(= a b c)</c> is every neighbour equal to the next.
    /// </summary>
    private SmtValue Equal(SmtValue[] arguments, int depth)
    {
        for (var index = 1; index < arguments.Length; index++)
        {
            if (!m_run.Equality.Same(arguments[index - 1], arguments[index], depth + 1))
                return SmtBoolValue.FALSE;
        }

        return SmtBoolValue.TRUE;
    }

    #endregion
}
