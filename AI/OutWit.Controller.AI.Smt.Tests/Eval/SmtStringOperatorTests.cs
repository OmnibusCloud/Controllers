using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtStringOperatorTests
{
    #region Operator Tests

    [TestCase("(str.++ \"ab\" \"\" \"c\")", "abc")]
    [TestCase("(str.at \"abc\" 0)", "a")]
    [TestCase("(str.at \"abc\" 2)", "c")]
    [TestCase("(str.at \"abc\" 3)", "")]
    [TestCase("(str.at \"abc\" (- 1))", "")]
    [TestCase("(str.substr \"abcdef\" 2 3)", "cde")]
    [TestCase("(str.substr \"abcdef\" 4 10)", "ef")]
    [TestCase("(str.substr \"abcdef\" 6 1)", "")]
    [TestCase("(str.substr \"abcdef\" 0 0)", "")]
    [TestCase("(str.substr \"abcdef\" (- 1) 2)", "")]
    [TestCase("(str.substr \"abcdef\" 1 (- 2))", "")]
    [TestCase("(str.substr \"abcdef\" 1 100000000000000000000000000)", "bcdef")]
    [TestCase("(str.replace \"abcabc\" \"bc\" \"X\")", "aXabc")]
    [TestCase("(str.replace \"abc\" \"d\" \"X\")", "abc")]
    [TestCase("(str.replace \"abc\" \"\" \"X\")", "Xabc")]
    [TestCase("(str.replace \"abc\" \"abc\" \"\")", "")]
    [TestCase("(str.from_int 0)", "0")]
    [TestCase("(str.from_int 1234567890123456789012)", "1234567890123456789012")]
    [TestCase("(str.from_int (- 5))", "")]
    [TestCase("(int.to.str 42)", "42")]
    [TestCase("(str.from_code 65)", "A")]
    [TestCase("(str.from_code (- 1))", "")]
    [TestCase("(str.from_code 196608)", "")]
    public void StringOperatorGivesTheStandardsStringTest(string term, string expected)
    {
        Assert.That(Text(term), Is.EqualTo(expected));
    }

    [TestCase("(str.len \"\")", 0)]
    [TestCase("(str.len \"abc\")", 3)]
    [TestCase("(str.len \"a\\u{1F600}b\")", 3)]
    [TestCase("(str.indexof \"abcabc\" \"bc\" 0)", 1)]
    [TestCase("(str.indexof \"abcabc\" \"bc\" 2)", 4)]
    [TestCase("(str.indexof \"abcabc\" \"bc\" 5)", -1)]
    [TestCase("(str.indexof \"abc\" \"\" 2)", 2)]
    [TestCase("(str.indexof \"abc\" \"\" 3)", 3)]
    [TestCase("(str.indexof \"abc\" \"\" 4)", -1)]
    [TestCase("(str.indexof \"abc\" \"a\" (- 1))", -1)]
    [TestCase("(str.indexof \"abc\" \"d\" 0)", -1)]
    [TestCase("(str.to_int \"0\")", 0)]
    [TestCase("(str.to_int \"007\")", 7)]
    [TestCase("(str.to_int \"\")", -1)]
    [TestCase("(str.to_int \"-5\")", -1)]
    [TestCase("(str.to_int \"1a\")", -1)]
    [TestCase("(str.to.int \"12\")", 12)]
    [TestCase("(str.to_code \"A\")", 65)]
    [TestCase("(str.to_code \"\")", -1)]
    [TestCase("(str.to_code \"AB\")", -1)]
    [TestCase("(str.to_code \"\\u{2FFFF}\")", 196607)]
    public void StringOperatorGivesTheStandardsNumberTest(string term, int expected)
    {
        Assert.That(((SmtIntValue)SmtCheck.Evaluate(term)).Value, Is.EqualTo(new BigInteger(expected)));
    }

    [TestCase("(str.prefixof \"ab\" \"abc\")", true)]
    [TestCase("(str.prefixof \"\" \"abc\")", true)]
    [TestCase("(str.prefixof \"abcd\" \"abc\")", false)]
    [TestCase("(str.prefixof \"bc\" \"abc\")", false)]
    [TestCase("(str.suffixof \"bc\" \"abc\")", true)]
    [TestCase("(str.suffixof \"\" \"abc\")", true)]
    [TestCase("(str.suffixof \"abcd\" \"abc\")", false)]
    [TestCase("(str.contains \"abc\" \"bc\")", true)]
    [TestCase("(str.contains \"abc\" \"\")", true)]
    [TestCase("(str.contains \"abc\" \"cb\")", false)]
    [TestCase("(str.< \"abc\" \"abd\")", true)]
    [TestCase("(str.< \"abc\" \"abc\")", false)]
    [TestCase("(str.< \"ab\" \"abc\")", true)]
    [TestCase("(str.< \"b\" \"abc\")", false)]
    [TestCase("(str.<= \"abc\" \"abc\")", true)]
    [TestCase("(str.<= \"abd\" \"abc\")", false)]
    [TestCase("(str.is_digit \"7\")", true)]
    [TestCase("(str.is_digit \"77\")", false)]
    [TestCase("(str.is_digit \"a\")", false)]
    [TestCase("(= \"a\\u{62}c\" \"abc\")", true)]
    [TestCase("(= \"\\u0041\" \"A\")", true)]
    [TestCase("(= \"\\u00412\" \"A2\")", true)]
    [TestCase("(= \"a\\b\" \"a\\b\")", true)]
    [TestCase("(= \"say \"\"hi\"\"\" (str.++ \"say \" \"\"\"hi\"\"\"))", true)]
    [TestCase("(distinct \"a\" \"b\" \"a\")", false)]
    public void StringOperatorGivesTheStandardsTruthTest(string term, bool expected)
    {
        Assert.That(((SmtBoolValue)SmtCheck.Evaluate(term)).Value, Is.EqualTo(expected));
    }

    #endregion

    #region Literal Tests

    [TestCase("\"\\u{}\"")]
    [TestCase("\"\\u{123456}\"")]
    [TestCase("\"\\u{30000}\"")]
    [TestCase("\"\\u{12\"")]
    [TestCase("\"\\u12\"")]
    [TestCase("\"\\uZZZZ\"")]
    [TestCase("\"a\\u\"")]
    [TestCase("\"tab\there\"")]
    [TestCase("\"line\nbreak\"")]
    public void LiteralReadersDisagreeOnIsNoValueTest(string literal)
    {
        // An escape that is none of the standard's, or a character outside printable ASCII:
        // what the literal means depends on who reads it.
        Assert.That(SmtCheck.ReasonOf($"(str.len {literal})"), Is.EqualTo(SmtValidationReasons.STRING_LITERAL));
    }

    [Test]
    public void LiteralOutsideAsciiIsNoValueTest()
    {
        var literal = "\"caf" + (char)0xE9 + "\"";

        Assert.That(SmtCheck.ReasonOf($"(str.len {literal})"), Is.EqualTo(SmtValidationReasons.STRING_LITERAL));
    }

    [Test]
    public void BackslashThatStartsNoEscapeIsItselfTest()
    {
        Assert.That(((SmtIntValue)SmtCheck.Evaluate("(str.len \"a\\x41\\n\\\\\")")).Value, Is.EqualTo(new BigInteger(9)));
    }

    #endregion

    #region Refusal Tests

    [TestCase("(str.in_re \"a\" (str.to_re \"a\"))")]
    [TestCase("(str.in_re \"a\" (re.* (str.to_re \"a\")))")]
    [TestCase("(str.in_re \"a\" re.all)")]
    [TestCase("(str.in_re \"a\" (re.range \"a\" \"z\"))")]
    [TestCase("(str.replace_re \"a\" (str.to_re \"a\") \"b\")")]
    public void RegularExpressionIsNotEvaluatedTest(string term)
    {
        Assert.That(SmtCheck.ReasonOf(term), Is.EqualTo(SmtValidationReasons.REGULAR_EXPRESSION));
    }

    [TestCase("(str.len 5)")]
    [TestCase("(str.++ \"a\")")]
    [TestCase("(str.at \"abc\" \"a\")")]
    [TestCase("(str.at \"abc\" 1.0)")]
    [TestCase("(str.substr \"abc\" 1)")]
    [TestCase("(str.< \"a\" \"b\" \"c\")")]
    [TestCase("(str.from_int \"1\")")]
    public void ApplicationOutsideTheSignatureIsNoValueTest(string term)
    {
        Assert.That(SmtCheck.ReasonOf(term), Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    [Test]
    public void StringOperatorOutsideTheBasicOnesIsNamedByItsFamilyTest()
    {
        var exception = Assert.Throws<SmtPartialException>(() => SmtCheck.Evaluate("(str.replace_all \"aa\" \"a\" \"b\")"))!;

        Assert.That(exception.Reason, Is.EqualTo(SmtValidationReasons.UNSUPPORTED_OPERATOR));
        Assert.That(exception.Detail, Is.EqualTo("str."));
    }

    #endregion

    #region Tools

    private static string Text(string term)
    {
        var value = (SmtStringValue)SmtCheck.Evaluate(term);
        return string.Concat(value.CodePoints.Select(char.ConvertFromUtf32));
    }

    #endregion
}
