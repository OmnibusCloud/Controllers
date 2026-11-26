namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// A table of a Markdown document as the tests read it: its header and its rows, each a list of
/// cells as they are written (code spans and all), trimmed.
/// </summary>
internal sealed class SmtMarkdownTable
{
    #region Constructors

    /// <summary>Makes a table.</summary>
    /// <param name="header">The cells of its header.</param>
    /// <param name="rows">Its rows, below the line that ends the header.</param>
    public SmtMarkdownTable(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Header = header;
        Rows = rows;
    }

    #endregion

    #region Functions

    /// <summary>The row whose first cell is a text.</summary>
    /// <param name="first">The first cell, as written.</param>
    /// <returns>The row.</returns>
    /// <exception cref="InvalidOperationException">No row, or more than one, has that first cell.</exception>
    public IReadOnlyList<string> Row(string first)
    {
        var rows = Rows.Where(row => string.Equals(row[0], first, StringComparison.Ordinal)).ToList();
        if (rows.Count != 1)
            throw new InvalidOperationException($"the table has {rows.Count} row(s) whose first cell is '{first}'");

        return rows[0];
    }

    /// <summary>The third cell of the row whose first two cells are texts.</summary>
    /// <param name="first">The first cell, as written.</param>
    /// <param name="second">The second cell, as written.</param>
    /// <returns>The third cell.</returns>
    /// <exception cref="InvalidOperationException">No row, or more than one, has those first cells.</exception>
    public string Cell(string first, string second)
    {
        var rows = Rows.Where(row => row.Count > 2 && string.Equals(row[0], first, StringComparison.Ordinal) && string.Equals(row[1], second, StringComparison.Ordinal)).ToList();
        if (rows.Count != 1)
            throw new InvalidOperationException($"the table has {rows.Count} row(s) whose first cells are '{first}' and '{second}'");

        return rows[0][2];
    }

    /// <summary>The cells of one column, a row after another.</summary>
    /// <param name="index">The column, from 0.</param>
    /// <returns>The cells.</returns>
    public List<string> Column(int index)
    {
        return [.. Rows.Select(row => index < row.Count ? row[index] : "")];
    }

    #endregion

    #region Properties

    /// <summary>The cells of the header.</summary>
    public IReadOnlyList<string> Header { get; }

    /// <summary>The rows.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    #endregion
}
