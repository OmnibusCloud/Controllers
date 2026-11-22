using System.Collections.Immutable;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// An array: the indices that were stored, over a base. The base is either one value for every
/// other index (<c>((as const (Array I E)) v)</c>): the array is then a finite map and can be
/// compared; or a function of the index (<see cref="SmtArraySource"/>): the array can then be
/// read and not compared.
/// <para>
/// The stored indices are kept in a sorted map that shares structure between an array and the
/// arrays made from it by a store, so a chain of stores costs a step of the map each, not a
/// copy each.
/// </para>
/// </summary>
internal sealed class SmtArrayValue : SmtValue
{
    #region Constants

    /// <summary>The stored indices of an array nothing was stored in.</summary>
    public static readonly ImmutableSortedDictionary<SmtValue, SmtValue> NO_ENTRIES =
        ImmutableSortedDictionary.Create<SmtValue, SmtValue>(SmtValueComparer.INSTANCE);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an array over one value for every index that is not stored.
    /// </summary>
    /// <param name="indexSort">The sort of the indices.</param>
    /// <param name="elementSort">The sort of the elements; null when it is not known yet.</param>
    /// <param name="otherwise">The value of every index that is not stored.</param>
    /// <param name="entries">The stored indices.</param>
    public SmtArrayValue(SmtSort indexSort, SmtSort? elementSort, SmtValue otherwise, ImmutableSortedDictionary<SmtValue, SmtValue> entries)
    {
        IndexSort = indexSort;
        ElementSort = elementSort;
        Default = otherwise;
        Entries = entries;
    }

    /// <summary>
    /// Creates an array over a function of the index.
    /// </summary>
    /// <param name="indexSort">The sort of the indices.</param>
    /// <param name="elementSort">The sort of the elements; null when it is not known yet.</param>
    /// <param name="source">The function.</param>
    /// <param name="entries">The stored indices.</param>
    public SmtArrayValue(SmtSort indexSort, SmtSort? elementSort, SmtArraySource source, ImmutableSortedDictionary<SmtValue, SmtValue> entries)
    {
        IndexSort = indexSort;
        ElementSort = elementSort;
        Source = source;
        Entries = entries;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The array with other stored indices over the same base.
    /// </summary>
    /// <param name="entries">The stored indices.</param>
    /// <returns>The new array.</returns>
    public SmtArrayValue WithEntries(ImmutableSortedDictionary<SmtValue, SmtValue> entries)
    {
        return Default != null
            ? new SmtArrayValue(IndexSort, ElementSort, Default, entries)
            : new SmtArrayValue(IndexSort, ElementSort, Source!, entries);
    }

    /// <summary>
    /// The same array with its element sort known.
    /// </summary>
    /// <param name="elementSort">The sort of the elements.</param>
    /// <returns>The new array.</returns>
    public SmtArrayValue WithElementSort(SmtSort elementSort)
    {
        return Default != null
            ? new SmtArrayValue(IndexSort, elementSort, Default, Entries)
            : new SmtArrayValue(IndexSort, elementSort, Source!, Entries);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.Array;

    /// <inheritdoc />
    public override long Weight => 1 + Entries.Count;

    /// <inheritdoc />
    public override bool HoldsArray => true;

    /// <summary>The sort of the indices.</summary>
    public SmtSort IndexSort { get; }

    /// <summary>The sort of the elements; null when it is not known (a lambda's body gives no sort).</summary>
    public SmtSort? ElementSort { get; }

    /// <summary>The value of every index that is not stored; null for an array over a function.</summary>
    public SmtValue? Default { get; }

    /// <summary>The function under the stored indices; null for an array over one value.</summary>
    public SmtArraySource? Source { get; }

    /// <summary>The stored indices and their values, in the order of <see cref="SmtValueComparer"/>.</summary>
    public ImmutableSortedDictionary<SmtValue, SmtValue> Entries { get; }

    #endregion
}
