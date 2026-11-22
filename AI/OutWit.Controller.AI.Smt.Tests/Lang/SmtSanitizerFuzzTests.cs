using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// A mutation fuzz with a fixed seed: valid problems with tokens dropped, doubled, swapped and
/// replaced by tokens of a hostile alphabet. Whatever comes out is a refusal with a code of the
/// contract or a problem that holds the properties of one; nothing else, and no exception.
/// </summary>
[TestFixture]
public class SmtSanitizerFuzzTests
{
    #region Constants

    private const int SEED = 20261122;
    private const int ROUNDS = 30_000;
    private const int MAX_MUTATIONS = 3;
    private const int ATOM_ATTEMPTS = 3;

    private static readonly string[] PROBLEMS =
    [
        "(set-info :smt-lib-version 2.6)\n(set-logic QF_LIA)\n(set-info :status sat)\n(declare-const x Int)\n(declare-fun y () Int)\n(assert (and (> x 0) (< y 10) (= (+ x y) 7)))\n(check-sat)\n(exit)\n",
        "(set-logic QF_BV)\n(declare-const a (_ BitVec 8))\n(define-fun twice ((v (_ BitVec 8))) (_ BitVec 8) (bvadd v v))\n(assert (! (= (twice a) #x10) :named double))\n(assert (not (= ((_ zero_extend 8) a) #b0000000000000001)))\n(check-sat)\n(get-model)\n",
        "(set-logic ALL)\n(set-info :source |a hand-written\nproblem|)\n(declare-datatypes ((Tree 1)) ((par (T) ((leaf) (node (left (Tree T)) (value T) (right (Tree T)))))))\n(declare-const |my tree| (Tree String))\n(assert (= (value |my tree|) \"say \"\"hi\"\"\"))\n",
        "(declare-sort U 0)\n(define-sort Pair (A) (Array A A))\n(declare-fun f (U U) Bool)\n(define-funs-rec ((even ((n Int)) Bool) (odd ((n Int)) Bool)) ((ite (= n 0) true (odd (- n 1))) (ite (= n 0) false (even (- n 1)))))\n(define-fun-rec fact ((n Int)) Int (ite (<= n 0) 1 (* n (fact (- n 1)))))\n(assert (forall ((u U)) (f u u)))\n(assert (even 4))\n(get-value ((fact 3)))\n",
        "(set-info :license \"https://creativecommons.org/licenses/by/4.0/\")\n(set-info :status unsat)\n(declare-datatype Color ((red) (green) (blue)))\n(declare-const c Color)\n(declare-const r Real)\n(assert (distinct c red green blue))\n(assert (> r 1.50))\n(get-info :reason-unknown)\n"
    ];

    private static readonly string[] SEPARATORS = [" ", " ", " ", " ", " ", " ", "\n", "\n", "\r\n", "\t", "  ", "", " ; a comment (exit)\n"];

    #endregion

    #region Fuzz Tests

    [Test]
    public void MutatedProblemIsRefusedByTheContractOrIsAProblemTest()
    {
        var random = new Random(SEED);
        var seeds = PROBLEMS.Select(Tokens).ToList();
        var alphabet = Alphabet();
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var accepted = 0;
        var watch = Stopwatch.StartNew();

        for (var round = 0; round < ROUNDS; round++)
        {
            var tokens = new List<string>(seeds[random.Next(seeds.Count)]);
            var mutations = 1 + random.Next(MAX_MUTATIONS);
            for (var mutation = 0; mutation < mutations; mutation++)
                Mutate(tokens, alphabet, random);

            var text = Join(tokens, random);
            var result = SmtSanitizer.Sanitize(text);

            if (result.Refusal != null)
            {
                Assert.That(result.Problem, Is.Null);
                SmtSanitizerChecks.HoldRefusal(result.Refusal);
                codes[result.Refusal.Code] = codes.GetValueOrDefault(result.Refusal.Code) + 1;
                continue;
            }

            SmtSanitizerChecks.HoldProperties(result.Problem!);
            accepted++;
        }

        watch.Stop();
        TestContext.Out.WriteLine($"{ROUNDS} rounds in {watch.ElapsedMilliseconds} ms: {accepted} problems, {ROUNDS - accepted} refusals");
        foreach (var (code, count) in codes)
            TestContext.Out.WriteLine($"  {code}: {count}");

        // The fuzz is worth its name only while it reaches both outcomes, and many rules.
        Assert.That(accepted, Is.GreaterThan(ROUNDS / 10));
        Assert.That(ROUNDS - accepted, Is.GreaterThan(ROUNDS / 2));
        Assert.That(codes.Keys, Is.SupersetOf(new[]
        {
            SmtRefusalCodes.SYNTAX_ERROR, SmtRefusalCodes.NUMERAL_TOO_LONG, SmtRefusalCodes.LITERAL_TOO_WIDE, SmtRefusalCodes.NOT_A_COMMAND,
            SmtRefusalCodes.COMMAND_NOT_ALLOWED, SmtRefusalCodes.COMMAND_MALFORMED, SmtRefusalCodes.RESERVED_SYMBOL, SmtRefusalCodes.AMBIGUOUS_TOKEN,
            SmtRefusalCodes.NON_ASCII_STRING, SmtRefusalCodes.INDEX_TOO_LARGE, SmtRefusalCodes.RESERVED_PREFIX
        }));
    }

    #endregion

    #region Tools

    private static void Mutate(List<string> tokens, string[] alphabet, Random random)
    {
        if (tokens.Count == 0)
        {
            tokens.Add(alphabet[random.Next(alphabet.Length)]);
            return;
        }

        // Half the tokens of a problem are parentheses, and a change of one of them is nearly
        // always the same syntax error: most changes are made to an atom.
        var at = random.Next(tokens.Count);
        for (var attempt = 0; attempt < ATOM_ATTEMPTS && tokens[at] is "(" or ")"; attempt++)
            at = random.Next(tokens.Count);

        switch (random.Next(5))
        {
            case 0:
                tokens.RemoveAt(at);
                break;
            case 1:
                tokens.Insert(random.Next(tokens.Count + 1), tokens[at]);
                break;
            case 2:
                var other = random.Next(tokens.Count);
                (tokens[at], tokens[other]) = (tokens[other], tokens[at]);
                break;
            case 3:
                tokens.Insert(at, alphabet[random.Next(alphabet.Length)]);
                break;
            default:
                tokens[at] = alphabet[random.Next(alphabet.Length)];
                break;
        }
    }

    private static string Join(List<string> tokens, Random random)
    {
        var builder = new StringBuilder();
        foreach (var token in tokens)
            builder.Append(token).Append(SEPARATORS[random.Next(SEPARATORS.Length)]);

        return builder.ToString();
    }

    /// <summary>The tokens of a valid text, parentheses included, each as the writer writes it.</summary>
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var open = new Stack<(SmtExpression List, int Next)>();

        foreach (var form in SmtReader.Read(text, SmtReaderLimits.Problem))
        {
            tokens.Add("(");
            open.Push((form, 0));
            while (open.Count > 0)
            {
                var (list, next) = open.Pop();
                if (next >= list.Items.Count)
                {
                    tokens.Add(")");
                    continue;
                }

                open.Push((list, next + 1));
                var item = list.Items[next];
                if (item.IsList)
                {
                    tokens.Add("(");
                    open.Push((item, 0));
                }
                else
                {
                    tokens.Add(item.ToString());
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// The hostile alphabet: command names of every class, parentheses, bars, quotes and
    /// backslashes, control characters and blanks that are none, numbers of every bad size,
    /// reserved words with and without bars, the controller's prefix, and whole commands.
    /// </summary>
    private static string[] Alphabet()
    {
        return
        [
            "assert", "declare-const", "declare-fun", "define-fun", "set-info", "set-logic", "declare-datatype", "define-funs-rec",
            "check-sat", "get-model", "get-value", "get-info", "exit",
            "push", "pop", "reset", "get-proof", "get-unsat-core", "check-sat-assuming", "set-option",
            "echo", "include", "eval", "simplify", "define-const", "dbg-x", "frobnicate",
            "(", ")", "(", ")", "((", "))", "()", ")(",
            "|", "\"", "\\", "||", "\"\"", "\"\"\"", "|a b|", "|a\\|", "\"str\"", "\"a\"\"b\"", "|a\r\nb|", "\"a\r\r\nb\r\"", "\"caf\u00e9\"", "\"\\\"", "|caf\u00e9|",
            "|let|", "|_|", "|!|", "|as|", "|forall|", "|par|", "|assert|", "|check-sat|", "|exit|", "|set-option|", "|-5|", "|x|",
            "\0", "\f", "\v", ((char)27).ToString(), ((char)127).ToString(), "\r", "\ufeff", "\u00a0", "\u2028", "\ud83d\ude00",
            new string('7', 5_000), "99999999999999999999", "65536", "65537", "1048576", "007", "1.5e3", "12abc", "0", "1.50",
            "#b" + new string('1', 70_000), "#xZZ", "#x", "#b0101", "#xFF",
            "_", "!", "as", "let", "forall", "exists", "par", "match", "NUMERAL",
            "-5", "-0.5", "-", "-x", "+5",
            ":status", ":source", ":license", ":named", ":", ":smt-lib-version", "::", ":a:b",
            "sat", "unsat", "unknown", "2.0", "true", "false",
            "oc!x", "oc!", "|oc!y|", "x", "y", "Int", "Bool", "BitVec", "zero_extend", "repeat", "red", "Tree",
            ";", "; hidden (exit)\n", ";\r", ";\n",
            "(exit)", "(check-sat)", "(get-model)", "(set-option :a 1)", "(push 1)", "(echo \"sat\")", "(include \"f\")",
            "(_ BitVec 1048576)", "(_ BitVec 8)", "((_ repeat 100000) x)", "(set-info :status unsat)", "(set-info :smt-lib-version 2.0)", "(set-info :source |two\r\nlines|)",
            "(declare-const oc!y Int)", "(declare-const z Int)", "(assert z)", "(assert (push 1))", "(|assert| true)", "(assert |let|)",
            "(assert (> x -5))", "(assert (= s \"\u00e9\"))", "(declare-datatype D ((a) (b (c Int))))", "(declare-datatype D (a b))"
        ];
    }

    #endregion
}
