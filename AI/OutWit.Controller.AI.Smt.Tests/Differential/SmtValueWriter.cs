using System.Globalization;
using System.Numerics;
using System.Text;
using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Writes a value of the evaluator back as an SMT-LIB term of its sort, for a solver to compare
/// with: a numeral, a decimal or a quotient of two; a bit-vector as <c>(_ bvN w)</c>; a string
/// literal with every character outside printable ASCII, and the backslash, as an escape of the
/// standard; a constructor applied to its fields; a constant array under its stores.
/// </summary>
internal static class SmtValueWriter
{
    #region Constants

    private const char FIRST_PRINTABLE = ' ';
    private const char LAST_PRINTABLE = '~';
    private const char QUOTE = '"';
    private const char BACKSLASH = '\\';

    #endregion

    #region Functions

    /// <summary>
    /// Writes a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="sort">The sort it is a value of.</param>
    /// <returns>The term, or null when the value is not of the sort.</returns>
    public static string? Write(SmtValue value, SmtGenSort sort)
    {
        switch (value)
        {
            case SmtBoolValue truth when sort.Kind == SmtGenSortKind.Bool:
                return truth.Value ? "true" : "false";
            case SmtIntValue integer when sort.Kind == SmtGenSortKind.Int:
                return SmtTermGenerator.Integer(integer.Value);
            case SmtRealValue real when sort.Kind == SmtGenSortKind.Real:
                return Rational(real.Value);
            case SmtBitVectorValue vector when sort.Kind == SmtGenSortKind.BitVector && vector.Width == sort.Width:
                return $"(_ bv{vector.Value.ToString(CultureInfo.InvariantCulture)} {vector.Width})";
            case SmtStringValue text when sort.Kind == SmtGenSortKind.String:
                return String(text.CodePoints);
            case SmtDatatypeValue built when sort.Kind == SmtGenSortKind.Datatype:
                return Datatype(built);
            case SmtElementValue element when sort.Kind == SmtGenSortKind.Uninterpreted:
                return element.Name;
            case SmtArrayValue array when sort.Kind == SmtGenSortKind.Array && array.Default != null:
                return Array(array, sort);
            default:
                return null;
        }
    }

    private static string Rational(SmtRational value)
    {
        var magnitude = BigInteger.Abs(value.Numerator);
        var written = value.IsInteger
            ? $"{magnitude.ToString(CultureInfo.InvariantCulture)}.0"
            : $"(/ {magnitude.ToString(CultureInfo.InvariantCulture)}.0 {value.Denominator.ToString(CultureInfo.InvariantCulture)}.0)";

        return value.Sign < 0 ? $"(- {written})" : written;
    }

    private static string String(int[] codePoints)
    {
        var builder = new StringBuilder(codePoints.Length + 2).Append(QUOTE);
        foreach (var codePoint in codePoints)
        {
            if (codePoint == QUOTE)
                builder.Append(QUOTE).Append(QUOTE);
            else if (codePoint is >= FIRST_PRINTABLE and <= LAST_PRINTABLE && codePoint != BACKSLASH)
                builder.Append((char)codePoint);
            else
                builder.Append(BACKSLASH).Append("u{").Append(codePoint.ToString("x", CultureInfo.InvariantCulture)).Append('}');
        }

        return builder.Append(QUOTE).ToString();
    }

    private static string? Datatype(SmtDatatypeValue built)
    {
        if (built.Fields.Length == 0)
            return built.Constructor.Name;

        var fields = new string[built.Fields.Length];
        for (var index = 0; index < fields.Length; index++)
        {
            var sort = built.Constructor.FieldSorts?[index];
            var field = sort == null ? null : Write(built.Fields[index], Of(sort));
            if (field == null)
                return null;

            fields[index] = field;
        }

        return $"({built.Constructor.Name} {string.Join(" ", fields)})";
    }

    private static string? Array(SmtArrayValue array, SmtGenSort sort)
    {
        var written = Write(array.Default!, sort.Element!);
        if (written == null)
            return null;

        written = $"((as const {sort.Text}) {written})";
        foreach (var entry in array.Entries)
        {
            var index = Write(entry.Key, sort.Index!);
            var element = Write(entry.Value, sort.Element!);
            if (index == null || element == null)
                return null;

            written = $"(store {written} {index} {element})";
        }

        return written;
    }

    /// <summary>The generator's sort of an evaluator's sort, for the fields of the datatypes the generator declares.</summary>
    private static SmtGenSort Of(SmtSort sort)
    {
        return sort.Kind switch
        {
            SmtSortKind.Int => SmtGenSort.INT,
            SmtSortKind.Datatype when sort.Datatype!.Name == SmtTermFamilyDatatypes.TREE.Text => SmtTermFamilyDatatypes.TREE,
            SmtSortKind.Datatype when sort.Datatype!.Name == SmtTermFamilyDatatypes.COLOR.Text => SmtTermFamilyDatatypes.COLOR,
            SmtSortKind.Datatype => SmtTermFamilyDatatypes.LIST,
            _ => SmtGenSort.BOOL
        };
    }

    #endregion
}
