namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The theory operators the evaluator computes on values. The connectives that may leave an
/// argument unevaluated (<c>and</c>, <c>or</c>, <c>=&gt;</c>, <c>ite</c>) and the binders are no
/// operators here: the evaluator reads them as the forms they are.
/// </summary>
internal enum SmtOperator
{
    /// <summary>No operator.</summary>
    None = 0,

    // Core.
    Not,
    Xor,
    Equal,
    Distinct,

    // Ints and Reals.
    Add,
    Subtract,
    Multiply,
    Divide,
    IntegerDivide,
    Modulo,
    Absolute,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    ToReal,
    ToInt,
    IsInt,
    Divisible,

    // Arrays.
    Select,
    Store,

    // Bit-vectors.
    Concat,
    BvNot,
    BvNeg,
    BvAnd,
    BvOr,
    BvXor,
    BvNand,
    BvNor,
    BvXnor,
    BvComp,
    BvAdd,
    BvSub,
    BvMul,
    BvUdiv,
    BvUrem,
    BvSdiv,
    BvSrem,
    BvSmod,
    BvShl,
    BvLshr,
    BvAshr,
    BvUlt,
    BvUle,
    BvUgt,
    BvUge,
    BvSlt,
    BvSle,
    BvSgt,
    BvSge,
    BvToNat,
    BvToInt,
    Extract,
    ZeroExtend,
    SignExtend,
    Repeat,
    RotateLeft,
    RotateRight,
    IntToBv,

    // Strings.
    StrConcat,
    StrLen,
    StrAt,
    StrSubstr,
    StrPrefixOf,
    StrSuffixOf,
    StrContains,
    StrIndexOf,
    StrReplace,
    StrLess,
    StrLessOrEqual,
    StrToInt,
    StrFromInt,
    StrIsDigit,
    StrToCode,
    StrFromCode
}
