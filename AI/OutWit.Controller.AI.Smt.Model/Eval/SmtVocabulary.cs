using System.Collections.Frozen;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The names the evaluator knows without a declaration: the operators it computes, the forms it
/// reads, and the operators it knows only to say that it does not evaluate them. A problem that
/// declares the name of an operator or of a form itself gives the name two meanings, which
/// solvers tell apart by the sorts of the arguments and the evaluator does not
/// (<see cref="SmtValidationReasons.AMBIGUOUS_SYMBOL"/>).
/// </summary>
internal static class SmtVocabulary
{
    #region Constants

    public const string TRUE = "true";
    public const string FALSE = "false";
    public const string AND = "and";
    public const string OR = "or";
    public const string IMPLIES = "=>";
    public const string ITE = "ite";
    public const string LAMBDA = "lambda";
    public const string ROOT_OBJECT = "root-obj";
    public const string LET = "let";
    public const string FORALL = "forall";
    public const string EXISTS = "exists";
    public const string MATCH = "match";
    public const string ANNOTATION = "!";
    public const string INDEXED = "_";
    public const string AS = "as";
    public const string PAR = "par";
    public const string NAMED = ":named";
    public const string CONST_ARRAY = "const";
    public const string AS_ARRAY = "as-array";
    public const string IS = "is";
    public const string TESTER_PREFIX = "is-";
    public const string BIT_VECTOR_VALUE_PREFIX = "bv";

    private const string REGULAR_EXPRESSION_PREFIX = "re.";
    private const string FLOATING_POINT_PREFIX = "fp.";
    private const string STRING_PREFIX = "str.";
    private const string SEQUENCE_PREFIX = "seq.";
    private const string CHARACTER_PREFIX = "char.";

    private static readonly FrozenDictionary<string, SmtOperator> OPERATORS = new Dictionary<string, SmtOperator>(StringComparer.Ordinal)
    {
        ["not"] = SmtOperator.Not, ["xor"] = SmtOperator.Xor, ["="] = SmtOperator.Equal, ["distinct"] = SmtOperator.Distinct,
        ["+"] = SmtOperator.Add, ["-"] = SmtOperator.Subtract, ["*"] = SmtOperator.Multiply, ["/"] = SmtOperator.Divide,
        ["div"] = SmtOperator.IntegerDivide, ["mod"] = SmtOperator.Modulo, ["abs"] = SmtOperator.Absolute,
        ["<"] = SmtOperator.Less, ["<="] = SmtOperator.LessOrEqual, [">"] = SmtOperator.Greater, [">="] = SmtOperator.GreaterOrEqual,
        ["to_real"] = SmtOperator.ToReal, ["to_int"] = SmtOperator.ToInt, ["is_int"] = SmtOperator.IsInt,
        ["select"] = SmtOperator.Select, ["store"] = SmtOperator.Store,
        ["concat"] = SmtOperator.Concat, ["bvnot"] = SmtOperator.BvNot, ["bvneg"] = SmtOperator.BvNeg,
        ["bvand"] = SmtOperator.BvAnd, ["bvor"] = SmtOperator.BvOr, ["bvxor"] = SmtOperator.BvXor,
        ["bvnand"] = SmtOperator.BvNand, ["bvnor"] = SmtOperator.BvNor, ["bvxnor"] = SmtOperator.BvXnor, ["bvcomp"] = SmtOperator.BvComp,
        ["bvadd"] = SmtOperator.BvAdd, ["bvsub"] = SmtOperator.BvSub, ["bvmul"] = SmtOperator.BvMul,
        ["bvudiv"] = SmtOperator.BvUdiv, ["bvurem"] = SmtOperator.BvUrem, ["bvsdiv"] = SmtOperator.BvSdiv,
        ["bvsrem"] = SmtOperator.BvSrem, ["bvsmod"] = SmtOperator.BvSmod,
        ["bvshl"] = SmtOperator.BvShl, ["bvlshr"] = SmtOperator.BvLshr, ["bvashr"] = SmtOperator.BvAshr,
        ["bvult"] = SmtOperator.BvUlt, ["bvule"] = SmtOperator.BvUle, ["bvugt"] = SmtOperator.BvUgt, ["bvuge"] = SmtOperator.BvUge,
        ["bvslt"] = SmtOperator.BvSlt, ["bvsle"] = SmtOperator.BvSle, ["bvsgt"] = SmtOperator.BvSgt, ["bvsge"] = SmtOperator.BvSge,
        ["bv2nat"] = SmtOperator.BvToNat, ["ubv_to_int"] = SmtOperator.BvToNat, ["sbv_to_int"] = SmtOperator.BvToInt,
        ["str.++"] = SmtOperator.StrConcat, ["str.len"] = SmtOperator.StrLen, ["str.at"] = SmtOperator.StrAt, ["str.substr"] = SmtOperator.StrSubstr,
        ["str.prefixof"] = SmtOperator.StrPrefixOf, ["str.suffixof"] = SmtOperator.StrSuffixOf, ["str.contains"] = SmtOperator.StrContains,
        ["str.indexof"] = SmtOperator.StrIndexOf, ["str.replace"] = SmtOperator.StrReplace,
        ["str.<"] = SmtOperator.StrLess, ["str.<="] = SmtOperator.StrLessOrEqual,
        ["str.to_int"] = SmtOperator.StrToInt, ["str.to.int"] = SmtOperator.StrToInt,
        ["str.from_int"] = SmtOperator.StrFromInt, ["int.to.str"] = SmtOperator.StrFromInt,
        ["str.is_digit"] = SmtOperator.StrIsDigit, ["str.to_code"] = SmtOperator.StrToCode, ["str.from_code"] = SmtOperator.StrFromCode
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, SmtOperator> INDEXED_OPERATORS = new Dictionary<string, SmtOperator>(StringComparer.Ordinal)
    {
        ["extract"] = SmtOperator.Extract, ["zero_extend"] = SmtOperator.ZeroExtend, ["sign_extend"] = SmtOperator.SignExtend,
        ["repeat"] = SmtOperator.Repeat, ["rotate_left"] = SmtOperator.RotateLeft, ["rotate_right"] = SmtOperator.RotateRight,
        ["int2bv"] = SmtOperator.IntToBv, ["int_to_bv"] = SmtOperator.IntToBv, ["divisible"] = SmtOperator.Divisible
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FORMS = new[] { TRUE, FALSE, AND, OR, IMPLIES, ITE, LAMBDA, ROOT_OBJECT }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> REGULAR_EXPRESSIONS = new[]
    {
        "str.in_re", "str.in.re", "str.to_re", "str.to.re", "str.replace_re", "str.replace_re_all"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FLOATING_POINT = new[]
    {
        "fp", "to_fp", "to_fp_unsigned", "RNE", "RNA", "RTP", "RTN", "RTZ", "roundNearestTiesToEven", "roundNearestTiesToAway",
        "roundTowardPositive", "roundTowardNegative", "roundTowardZero", "+oo", "-oo", "+zero", "-zero", "NaN"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> UNSUPPORTED = new[]
    {
        "^", "rem", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "asinh", "acosh", "atanh", "pi", "euler",
        "bvredor", "bvredand", "bvumul_noovfl", "bvsmul_noovfl", "bvsmul_noudfl", "bvnego", "bvuaddo", "bvsaddo",
        "bvumulo", "bvsmulo", "bvusubo", "bvssubo", "bvsdivo", "ext_rotate_left", "ext_rotate_right", "bv2int"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] UNSUPPORTED_PREFIXES = [STRING_PREFIX, SEQUENCE_PREFIX, CHARACTER_PREFIX];

    #endregion

    #region Functions

    /// <summary>
    /// Finds the operator of a name.
    /// </summary>
    /// <param name="name">A symbol's name.</param>
    /// <param name="operator">The operator.</param>
    /// <returns>True when the name is an operator the evaluator computes.</returns>
    public static bool TryGetOperator(string name, out SmtOperator @operator)
    {
        return OPERATORS.TryGetValue(name, out @operator);
    }

    /// <summary>
    /// Finds the operator of an indexed name: <c>extract</c> in <c>(_ extract 7 0)</c>.
    /// </summary>
    /// <param name="name">The name after the underscore.</param>
    /// <param name="operator">The operator.</param>
    /// <returns>True when the name is an indexed operator the evaluator computes.</returns>
    public static bool TryGetIndexedOperator(string name, out SmtOperator @operator)
    {
        return INDEXED_OPERATORS.TryGetValue(name, out @operator);
    }

    /// <summary>
    /// Whether a name is a truth value.
    /// </summary>
    /// <param name="name">A symbol's name.</param>
    /// <returns>True for <c>true</c> and <c>false</c>.</returns>
    public static bool IsTruthValue(string name)
    {
        return string.Equals(name, TRUE, StringComparison.Ordinal) || string.Equals(name, FALSE, StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds why a name the evaluator knows is not evaluated.
    /// </summary>
    /// <param name="name">A symbol's name.</param>
    /// <param name="reason">A code of <see cref="SmtValidationReasons"/>.</param>
    /// <param name="detail">The name itself when it is one of the vocabulary; a family's prefix, or null, otherwise.</param>
    /// <returns>True when the name is known and not evaluated.</returns>
    public static bool TryGetUnsupported(string name, out string reason, out string? detail)
    {
        detail = null;
        if (REGULAR_EXPRESSIONS.Contains(name) || name.StartsWith(REGULAR_EXPRESSION_PREFIX, StringComparison.Ordinal))
        {
            reason = SmtValidationReasons.REGULAR_EXPRESSION;
            return true;
        }

        if (FLOATING_POINT.Contains(name) || name.StartsWith(FLOATING_POINT_PREFIX, StringComparison.Ordinal))
        {
            reason = SmtValidationReasons.FLOATING_POINT;
            return true;
        }

        reason = SmtValidationReasons.UNSUPPORTED_OPERATOR;
        if (UNSUPPORTED.TryGetValue(name, out var known))
        {
            detail = known;
            return true;
        }

        foreach (var prefix in UNSUPPORTED_PREFIXES)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                detail = prefix;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a name is one the evaluator reads by itself: an operator it computes or a form
    /// it reads. A problem that declares such a name gives it a second meaning. A name known
    /// only as not evaluated is no such name: where a problem declares it, the declaration is
    /// what the name means, here as for a solver.
    /// </summary>
    /// <param name="name">A symbol's name.</param>
    /// <returns>True for an operator or a form.</returns>
    public static bool IsOperator(string name)
    {
        return OPERATORS.ContainsKey(name) || FORMS.Contains(name);
    }

    #endregion
}
