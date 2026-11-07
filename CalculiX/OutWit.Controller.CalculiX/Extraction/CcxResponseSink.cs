using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Collects the responses and notes of one variant per step and names the
/// columns at the end, once the step count is known: a single-step deck
/// keeps the plain names (<c>max_von_mises</c>), a multi-step deck names
/// the step in every column (<c>max_von_mises[step 2]</c>) so a static step
/// followed by a frequency step reports both, each under its own name,
/// instead of the last block of the file under the static name. Notes
/// about a column carry that column's final name; a note about a step but
/// no column is prefixed with the step in a multi-step deck.
/// </summary>
internal sealed class CcxResponseSink
{
    #region Fields

    private readonly List<(int Step, string Name, double Value)> m_values = [];
    private readonly List<(int Step, CcxNoteKind Kind, string? Column, string Text)> m_notes = [];

    #endregion

    #region Functions

    /// <summary>
    /// Names a column for its step.
    /// </summary>
    /// <param name="name">Plain column name.</param>
    /// <param name="step">1-based step number.</param>
    /// <param name="multiStep">True to name the step.</param>
    /// <returns>The column name.</returns>
    public static string ColumnName(string name, int step, bool multiStep)
    {
        return multiStep ? $"{name}[step {step}]" : name;
    }

    /// <summary>
    /// Makes a step known even when nothing was extracted from it, so the
    /// naming sees the deck's full step count.
    /// </summary>
    /// <param name="step">1-based step number.</param>
    public void DeclareStep(int step)
    {
        MaxStep = System.Math.Max(MaxStep, step);
    }

    /// <summary>
    /// Adds a response of a step.
    /// </summary>
    /// <param name="step">1-based step number.</param>
    /// <param name="name">Plain column name.</param>
    /// <param name="value">The value.</param>
    public void Add(int step, string name, double value)
    {
        DeclareStep(step);
        m_values.Add((step, name, value));
    }

    /// <summary>
    /// Adds a note.
    /// </summary>
    /// <param name="kind">What the note means for the reading of the variant.</param>
    /// <param name="step">1-based step number; 0 for a note about the variant as a whole.</param>
    /// <param name="column">Plain column name the note is about; null for none.</param>
    /// <param name="text">The note.</param>
    public void Note(CcxNoteKind kind, int step, string? column, string text)
    {
        if (step > 0)
            DeclareStep(step);

        m_notes.Add((step, kind, column, text));
    }

    /// <summary>
    /// Writes the responses and notes out under their final column names.
    /// </summary>
    /// <param name="row">Receives the responses, in extraction order.</param>
    /// <param name="notes">Receives the notes, in extraction order.</param>
    public void Emit(CcxResponseRowData row, List<CcxNote> notes)
    {
        var multiStep = MaxStep > 1;

        foreach (var (step, name, value) in m_values)
            row.Values.Add(new CcxResponseValueData { Name = ColumnName(name, step, multiStep), Value = value });

        foreach (var (step, kind, column, text) in m_notes)
        {
            notes.Add(new CcxNote
            {
                Kind = kind,
                Column = column == null ? null : ColumnName(column, step, multiStep),
                Text = column == null && step > 0 && multiStep ? $"step {step}: {text}" : text
            });
        }
    }

    #endregion

    #region Properties

    /// <summary>Highest step number seen so far.</summary>
    public int MaxStep { get; private set; }

    /// <summary>Number of responses collected.</summary>
    public int Count => m_values.Count;

    #endregion
}
