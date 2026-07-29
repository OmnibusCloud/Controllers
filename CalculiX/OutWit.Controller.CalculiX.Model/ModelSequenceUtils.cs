using OutWit.Common.Abstract;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// Element-wise value comparison for lists of model types — the counterpart of
/// the scalar-sequence Is that OutWit.Common provides, with the tolerance
/// threaded through to each element's own Is.
/// </summary>
internal static class ModelSequenceUtils
{
    #region Functions

    /// <summary>
    /// Compares two model lists by count and element-wise <see cref="ModelBase.Is"/>.
    /// </summary>
    /// <param name="source">This list.</param>
    /// <param name="other">List to compare against.</param>
    /// <param name="tolerance">Tolerance forwarded to element comparison.</param>
    /// <returns>True when both lists hold pairwise value-equal elements.</returns>
    public static bool IsSequence<TModel>(this IReadOnlyList<TModel> source, IReadOnlyList<TModel> other, double tolerance)
        where TModel : ModelBase
    {
        if (source.Count != other.Count)
            return false;

        for (var i = 0; i < source.Count; i++)
        {
            if (!source[i].Is(other[i], tolerance))
                return false;
        }

        return true;
    }

    #endregion
}
