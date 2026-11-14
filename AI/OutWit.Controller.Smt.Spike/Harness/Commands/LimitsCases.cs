using System.Numerics;
using System.Text;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary>The problems of the limits run, generated (none is a benchmark file).</summary>
internal static class LimitsCases
{
    #region Functions

    /// <summary>An assertion that names an undeclared constant: Z3 reports an error and the session goes on.</summary>
    /// <returns>SMT-LIB text.</returns>
    public static string InputError()
    {
        return "(set-logic QF_LIA)\n(declare-const x Int)\n(assert (> y 1))\n(assert (> x 1))\n";
    }

    /// <summary>
    /// Factoring a 62-bit semiprime (2^31-1 times 2147483629) with 64-bit vectors: hard enough that
    /// a small resource limit or a small fuel budget stops it.
    /// </summary>
    /// <returns>SMT-LIB text.</returns>
    public static string Factoring()
    {
        var product = new BigInteger(2147483647) * new BigInteger(2147483629);
        var hex = product.ToString("x16");
        hex = hex.Length > 16 ? hex[^16..] : hex.PadLeft(16, '0');
        return "(set-logic QF_BV)\n(declare-const a (_ BitVec 64))\n(declare-const b (_ BitVec 64))\n"
               + $"(assert (= (bvmul a b) #x{hex}))\n"
               + "(assert (bvult a #x0000000080000000))\n(assert (bvult b #x0000000080000000))\n"
               + "(assert (bvugt a #x0000000000000001))\n(assert (bvugt b #x0000000000000001))\n";
    }

    /// <summary>
    /// The pigeonhole principle, holes + 1 pigeons in holes holes, as Booleans: unsat, and
    /// exponentially hard for resolution, so a resource limit or a fuel budget stops it first.
    /// </summary>
    /// <param name="holes">The number of holes.</param>
    /// <returns>SMT-LIB text.</returns>
    public static string Pigeonhole(int holes)
    {
        var builder = new StringBuilder("(set-logic QF_UF)\n");
        for (var i = 0; i <= holes; i++)
        {
            for (var j = 0; j < holes; j++)
                builder.Append($"(declare-const p{i}_{j} Bool)\n");
        }

        for (var i = 0; i <= holes; i++)
            builder.Append("(assert (or").Append(string.Concat(Enumerable.Range(0, holes).Select(j => $" p{i}_{j}"))).Append("))\n");
        for (var j = 0; j < holes; j++)
        {
            for (var i = 0; i <= holes; i++)
            {
                for (var k = i + 1; k <= holes; k++)
                    builder.Append($"(assert (not (and p{i}_{j} p{k}_{j})))\n");
            }
        }

        return builder.ToString();
    }

    /// <summary>A multiplication of two bit-vectors of a given width: bit-blasting it takes memory quadratic in the width.</summary>
    /// <param name="width">The width (a multiple of 4).</param>
    /// <returns>SMT-LIB text.</returns>
    public static string MemoryBomb(int width)
    {
        var one = "#x" + new string('0', width / 4 - 1) + "1";
        return $"(set-logic QF_BV)\n(declare-const a (_ BitVec {width}))\n(declare-const b (_ BitVec {width}))\n"
               + $"(assert (= (bvmul a b) (bvadd a {one})))\n(assert (distinct a {one.Replace("1", "0")}))\n";
    }

    /// <summary>A term nested to a given depth: (not (not ... p)) or (+ 1 (+ 1 ... x)).</summary>
    /// <param name="shape">not or plus.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <returns>SMT-LIB text.</returns>
    public static string Deep(string shape, int depth)
    {
        var builder = new StringBuilder();
        if (shape == "not")
        {
            builder.Append("(set-logic QF_UF)\n(declare-const p Bool)\n(assert ");
            for (var i = 0; i < depth; i++)
                builder.Append("(not ");
            builder.Append('p').Append(')', depth).Append(")\n");
        }
        else
        {
            builder.Append("(set-logic QF_LIA)\n(declare-const x Int)\n(assert (= ");
            for (var i = 0; i < depth; i++)
                builder.Append("(+ 1 ");
            builder.Append('x').Append(')', depth).Append($" {depth + 5}))\n");
        }

        return builder.ToString();
    }

    /// <summary>The n-queens problem as Ints (92 solutions for n = 8).</summary>
    /// <param name="n">The board size.</param>
    /// <returns>SMT-LIB text.</returns>
    public static string Queens(int n)
    {
        var builder = new StringBuilder("(set-logic QF_LIA)\n");
        for (var i = 0; i < n; i++)
            builder.Append($"(declare-const q{i} Int)\n(assert (and (>= q{i} 0) (< q{i} {n})))\n");
        builder.Append("(assert (distinct");
        for (var i = 0; i < n; i++)
            builder.Append($" q{i}");
        builder.Append("))\n");
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
                builder.Append($"(assert (and (not (= (- q{i} q{j}) {j - i})) (not (= (- q{j} q{i}) {j - i}))))\n");
        }

        return builder.ToString();
    }

    #endregion
}
