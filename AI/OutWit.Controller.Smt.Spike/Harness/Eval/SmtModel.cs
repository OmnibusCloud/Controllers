using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// The model a solver printed, read for the evaluator: the interpretations of get-model
/// (define-fun, constants and functions, with Z3's auxiliary functions such as <c>k!0</c> behind
/// an <c>as-array</c>), the elements of uninterpreted sorts it declares (<c>U!val!0</c>), and the
/// get-value answer for every declared constant (complete: get-value completes the model).
/// </summary>
internal sealed class SmtModel
{
    #region Functions

    /// <summary>Reads a model and a get-value answer.</summary>
    /// <param name="modelText">The get-model answer, or null.</param>
    /// <param name="valuesText">The get-value answer, or null.</param>
    /// <returns>The model.</returns>
    /// <exception cref="FormatException">Either text is not well-formed.</exception>
    public static SmtModel Parse(string? modelText, string? valuesText)
    {
        var model = new SmtModel();
        if (!string.IsNullOrWhiteSpace(modelText))
        {
            foreach (var form in Forms(SmtReader.ReadAll(modelText)))
                model.Read(form);
        }

        if (!string.IsNullOrWhiteSpace(valuesText))
        {
            foreach (var answer in SmtReader.ReadAll(valuesText))
            {
                foreach (var pair in answer.Items)
                {
                    if (pair.IsList && pair.Items.Count == 2)
                        model.Values[pair.Items[0].Kind == SExprKind.Symbol ? pair.Items[0].Text : pair.Items[0].ToString()] = pair.Items[1];
                }
            }
        }

        return model;
    }

    private static IEnumerable<SExpr> Forms(List<SExpr> top)
    {
        // Z3 prints "(\n  (define-fun ...) ...)"; older versions "(model (define-fun ...) ...)".
        foreach (var form in top)
        {
            if (form.Head is "define-fun" or "declare-fun" or "forall")
            {
                yield return form;
                continue;
            }

            foreach (var item in form.Items)
            {
                if (item.IsList)
                    yield return item;
            }
        }
    }

    private void Read(SExpr form)
    {
        var items = form.Items;
        switch (form.Head)
        {
            case "define-fun" when items.Count == 5:
                Functions[items[1].Text] = new SmtDefinition(items[2].Items.Select(p => p.Items[0].Text).ToList(),
                    items[2].Items.Select(p => p.Items[1]).ToList(), items[3], items[4]);
                break;
            case "declare-fun" when items.Count == 4 && items[2].Items.Count == 0:
                // An element of an uninterpreted sort.
                var sort = items[3].ToString();
                if (!Universe.TryGetValue(sort, out var elements))
                    Universe[sort] = elements = [];
                elements.Add(items[1].Text);
                Elements.Add(items[1].Text);
                break;
        }
    }

    #endregion

    #region Properties

    /// <summary>The model's define-fun interpretations by name.</summary>
    public Dictionary<string, SmtDefinition> Functions { get; } = [];

    /// <summary>The get-value answer: each declared constant's value term.</summary>
    public Dictionary<string, SExpr> Values { get; } = [];

    /// <summary>The elements the model declares, per uninterpreted sort (as text).</summary>
    public Dictionary<string, List<string>> Universe { get; } = [];

    /// <summary>Every element name the model declares.</summary>
    public HashSet<string> Elements { get; } = [];

    #endregion
}
