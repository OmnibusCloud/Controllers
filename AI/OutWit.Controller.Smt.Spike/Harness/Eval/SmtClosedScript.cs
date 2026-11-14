using System.Text;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// The closed script of the specification's <c>validate</c> query (§4.5): the problem with every
/// declared symbol the model defines turned into a <c>define-fun</c> of the model's value or
/// interpretation, the model's own auxiliary functions (<c>k!0</c> behind an as-array) and the
/// elements of its uninterpreted sorts declared ahead of them. Z3 then only evaluates: sat means
/// every assertion holds under the model, unsat that one does not. A symbol the model leaves out
/// stays declared (free).
/// </summary>
internal static class SmtClosedScript
{
    #region Functions

    /// <summary>Builds the closed problem text.</summary>
    /// <param name="problem">The canonical problem.</param>
    /// <param name="model">The model (get-model and get-value).</param>
    /// <returns>SMT-LIB text without check-sat.</returns>
    public static string Build(SmtProblem problem, SmtModel model)
    {
        var declared = new HashSet<string>();
        foreach (var command in problem.Commands)
        {
            if (command.Head is "declare-const" or "declare-fun")
                declared.Add(command.Items[1].Text);
        }

        var builder = new StringBuilder();
        var prefixWritten = false;
        foreach (var command in problem.Commands)
        {
            var head = command.Head;
            var isSymbolCommand = head is "declare-const" or "declare-fun" or "define-fun" or "define-fun-rec" or "define-funs-rec" or "assert";
            if (isSymbolCommand && !prefixWritten)
            {
                WritePrefix(builder, model, declared);
                prefixWritten = true;
            }

            if (head is "declare-const" or "declare-fun")
            {
                var name = command.Items[1].Text;
                var sort = head == "declare-const" ? command.Items[2] : command.Items[3];
                var arity = head == "declare-const" ? 0 : command.Items[2].Items.Count;
                if (arity == 0 && model.Values.TryGetValue(name, out var value))
                {
                    builder.Append("(define-fun ").Append(Name(name)).Append(" () ").Append(sort).Append(' ').Append(value).Append(")\n");
                    continue;
                }

                if (model.Functions.TryGetValue(name, out var function) && function.Parameters.Count == arity)
                {
                    WriteDefinition(builder, name, function);
                    continue;
                }
            }

            builder.Append(command).Append('\n');
        }

        if (!prefixWritten)
            WritePrefix(builder, model, declared);
        return builder.ToString();
    }

    private static void WritePrefix(StringBuilder builder, SmtModel model, HashSet<string> declared)
    {
        foreach (var (sort, elements) in model.Universe)
        {
            foreach (var element in elements)
                builder.Append("(declare-fun ").Append(Name(element)).Append(" () ").Append(sort).Append(")\n");
        }

        foreach (var (name, function) in model.Functions)
        {
            if (!declared.Contains(name) && function.Parameters.Count > 0)
                WriteDefinition(builder, name, function);
        }
    }

    private static void WriteDefinition(StringBuilder builder, string name, SmtDefinition function)
    {
        builder.Append("(define-fun ").Append(Name(name)).Append(" (");
        for (var i = 0; i < function.Parameters.Count; i++)
            builder.Append(i > 0 ? " " : "").Append('(').Append(Name(function.Parameters[i])).Append(' ').Append(function.Sorts[i]).Append(')');
        builder.Append(") ").Append(function.Result).Append(' ').Append(function.Body).Append(")\n");
    }

    private static string Name(string name)
    {
        return SExpr.QuotedSymbol(name, 0).ToString();
    }

    #endregion
}
