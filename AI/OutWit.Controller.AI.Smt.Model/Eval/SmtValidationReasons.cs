namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The reasons a validation gives: fixed codes, never text of the problem or of the model.
/// <see cref="ASSERTION_FALSE"/> and <see cref="VALUE_MISMATCH"/> go with
/// <see cref="SmtValidationClass.Failed"/>; every other code goes with
/// <see cref="SmtValidationClass.Partial"/>.
/// </summary>
public static class SmtValidationReasons
{
    #region Constants

    /// <summary>An assertion evaluated to false with nothing guessed; the detail is its 1-based index.</summary>
    public const string ASSERTION_FALSE = "assertion_false";

    /// <summary>
    /// A value the answer of <c>get-value</c> reports is not the value its term has under the
    /// model, with nothing guessed; the detail is the 1-based index of the value among those
    /// reported.
    /// </summary>
    public const string VALUE_MISMATCH = "value_mismatch";

    /// <summary>An assertion is false only with a symbol the model left out, which was given its sort's default value.</summary>
    public const string COMPLETED_SYMBOL = "completed_symbol";

    /// <summary>A quantified formula: nothing is searched here.</summary>
    public const string QUANTIFIER = "quantifier";

    /// <summary>An algebraic number the solver printed as a root of a polynomial.</summary>
    public const string ALGEBRAIC_NUMBER = "algebraic_number";

    /// <summary>A division or a remainder by zero: the standard leaves its value to the model.</summary>
    public const string DIVISION_BY_ZERO = "division_by_zero";

    /// <summary>A regular expression.</summary>
    public const string REGULAR_EXPRESSION = "regular_expression";

    /// <summary>Floating-point arithmetic.</summary>
    public const string FLOATING_POINT = "floating_point";

    /// <summary>A selector applied to a value of another constructor: the standard leaves its value to the model.</summary>
    public const string SELECTOR_OF_OTHER_CONSTRUCTOR = "selector_of_other_constructor";

    /// <summary>An operator the evaluator knows by name and does not evaluate; the detail is its name.</summary>
    public const string UNSUPPORTED_OPERATOR = "unsupported_operator";

    /// <summary>A sort the evaluator has no values for.</summary>
    public const string UNSUPPORTED_SORT = "unsupported_sort";

    /// <summary>
    /// Two arrays whose equality their finite maps do not decide: one is known only as a
    /// function, or their defaults differ over an index sort the stored indices may exhaust.
    /// </summary>
    public const string ARRAY_COMPARISON = "array_comparison";

    /// <summary>
    /// A string literal that readers are known to read differently: an escape that is none of
    /// the standard's, or a character outside printable ASCII.
    /// </summary>
    public const string STRING_LITERAL = "string_literal";

    /// <summary>A term the evaluator cannot make sense of: a wrong number of arguments, a value of the wrong sort.</summary>
    public const string ILL_FORMED_TERM = "ill_formed_term";

    /// <summary>A name that is neither declared, nor defined, nor in the model, nor an operator the evaluator knows.</summary>
    public const string UNKNOWN_SYMBOL = "unknown_symbol";

    /// <summary>A command of the problem the evaluator cannot make sense of, or one that is no declaration, definition or assertion.</summary>
    public const string ILL_FORMED_COMMAND = "ill_formed_command";

    /// <summary>
    /// A name the problem gives two meanings: declared or defined twice, or the name of an
    /// operator. Solvers resolve such a name by rules the evaluator does not repeat.
    /// </summary>
    public const string AMBIGUOUS_SYMBOL = "ambiguous_symbol";

    /// <summary>The text is not a model a solver prints.</summary>
    public const string UNREADABLE_MODEL = "unreadable_model";

    /// <summary>
    /// The text is not the answer of <c>get-value</c> for the terms that were asked: it is no
    /// list of pairs of a term and a value, it holds another number of pairs, a pair names
    /// another term, or a value is written with a symbol the problem declares or defines.
    /// </summary>
    public const string UNREADABLE_VALUES = "unreadable_values";

    /// <summary>The step budget ended the evaluation.</summary>
    public const string BUDGET_STEPS = "budget_steps";

    /// <summary>A term or a chain of calls nested deeper than the ceiling.</summary>
    public const string BUDGET_DEPTH = "budget_depth";

    /// <summary>A value grew past a size ceiling: an integer, a bit-vector, a string or an array.</summary>
    public const string BUDGET_SIZE = "budget_size";

    /// <summary>The evaluator failed in a way it does not account for. It is a defect of the evaluator, never a property of the input.</summary>
    public const string EVALUATOR_ERROR = "evaluator_error";

    private static readonly string[] ALL_CODES =
    [
        ASSERTION_FALSE, VALUE_MISMATCH, COMPLETED_SYMBOL, QUANTIFIER, ALGEBRAIC_NUMBER, DIVISION_BY_ZERO,
        REGULAR_EXPRESSION, FLOATING_POINT, SELECTOR_OF_OTHER_CONSTRUCTOR, UNSUPPORTED_OPERATOR, UNSUPPORTED_SORT,
        ARRAY_COMPARISON, STRING_LITERAL, ILL_FORMED_TERM, UNKNOWN_SYMBOL, ILL_FORMED_COMMAND, AMBIGUOUS_SYMBOL,
        UNREADABLE_MODEL, UNREADABLE_VALUES, BUDGET_STEPS, BUDGET_DEPTH, BUDGET_SIZE, EVALUATOR_ERROR
    ];

    #endregion

    #region Properties

    /// <summary>Every code a validation can give.</summary>
    public static IReadOnlyList<string> All => ALL_CODES;

    #endregion
}
