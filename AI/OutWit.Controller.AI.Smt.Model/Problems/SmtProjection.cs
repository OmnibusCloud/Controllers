using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// The names an enumeration is projected on (<c>project</c>), held to the problem they are named
/// for. One check for everybody who reads a projection: the reader of a problem set, which takes
/// or refuses a row before anything is sent, and the node, which reads again what travelled to
/// it. So a node refuses no projection the host took, and takes none the host would refuse.
/// <para>
/// The names are looked at in their order, and the first one that breaks a rule is the one a
/// refusal names. Of each name, in this order:
/// </para>
/// <list type="number">
/// <item>it is a term the sanitizer takes (<see cref="SmtSanitizer.SanitizeTerm"/>);</item>
/// <item>it is one symbol;</item>
/// <item>the problem declares a constant of that name (by <c>declare-const</c>, or by a
/// <c>declare-fun</c> of no arguments);</item>
/// <item>no name before it is the same constant;</item>
/// <item>the solver can be told the constant's values back: they hold no element of a sort the
/// problem only declares (<see cref="SmtRefusalCodes.PROJECT_SORT_UNSUPPORTED"/>).</item>
/// </list>
/// <para>
/// The last rule is what an enumeration rests on. After each model it tells the solver that not
/// all of the projected constants have the values they had, and asks again. The solver names the
/// elements of a sort the problem leaves to it (<c>declare-sort</c>) by names of its own, inside
/// one model (<c>U!val!0</c>), and takes none of them in a later command: such a model cannot be
/// ruled out, so it would be found again and again. Every other sort the pinned solver prints in
/// terms it reads back (<see cref="SmtUninterpretedSorts"/> says which sorts hold such elements).
/// </para>
/// </summary>
public static class SmtProjection
{
    #region Functions

    /// <summary>
    /// Takes the names of a projection, or says why they are not taken.
    /// </summary>
    /// <param name="problem">The problem as the sanitizer took it.</param>
    /// <param name="names">The texts of the names, in the order they were given.</param>
    /// <param name="constants">The constants that were named, each as the problem declares it, in the order of the names; none when the projection is refused.</param>
    /// <returns>Null when the projection is taken; the refusal otherwise, about the row itself: whoever reads a set gives it its row and its id.</returns>
    /// <exception cref="ArgumentNullException">There is no problem, or there are no names.</exception>
    public static SmtRefusalData? Take(SmtProblem problem, IReadOnlyList<string?> names, out IReadOnlyList<SmtDeclaredConstant> constants)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(names);

        constants = [];

        if (names.Count == 0)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_MISSING, "'project' is missing: an enumeration names the constants its models are told apart by");

        if (names.Count > SmtProblemSetLimits.MAX_PROJECT)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_INVALID, $"'project' holds more than the {SmtProblemSetLimits.MAX_PROJECT} names allowed");

        var declared = new Dictionary<string, SmtDeclaredConstant>(StringComparer.Ordinal);
        foreach (var constant in problem.Constants)
            declared.TryAdd(constant.Name, constant);

        // Every name is read first, so that the sorts of the constants they name are looked at
        // in one pass over the problem, and only for those constants.
        var terms = new SmtSanitizerTermResult[names.Count];
        var asked = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < terms.Length; index++)
        {
            terms[index] = SmtSanitizer.SanitizeTerm(names[index] ?? "");
            if (terms[index].Term is { Kind: SmtExpressionKind.Symbol } symbol && declared.ContainsKey(symbol.Text))
                asked.Add(symbol.Text);
        }

        var uninterpreted = SmtUninterpretedSorts.Held(problem, asked);

        var named = new HashSet<string>(StringComparer.Ordinal);
        var taken = new List<SmtDeclaredConstant>(terms.Length);
        for (var index = 0; index < terms.Length; index++)
        {
            var refusal = Check(terms[index], index, declared, named, uninterpreted, out var constant);
            if (refusal != null)
                return refusal;

            taken.Add(constant!);
        }

        constants = taken;
        return null;
    }

    #endregion

    #region Tools

    private static SmtRefusalData? Check(SmtSanitizerTermResult term, int index, Dictionary<string, SmtDeclaredConstant> declared, HashSet<string> named,
        IReadOnlyDictionary<string, string> uninterpreted, out SmtDeclaredConstant? constant)
    {
        constant = null;

        if (!term.IsAccepted)
        {
            // The refusal of a term, with the field the term stands in said first.
            term.Refusal!.Reason = $"'project[{index}]': {term.Refusal.Reason}";
            return term.Refusal;
        }

        if (term.Term.Kind != SmtExpressionKind.Symbol)
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_INVALID, $"'project[{index}]' must be the name of a constant: one symbol");

        var name = term.Term.Text;
        if (!declared.TryGetValue(name, out constant))
            return SmtRefusals.OfRow(SmtRefusalCodes.UNKNOWN_CONSTANT, $"'project[{index}]' names '{SmtRefusals.Token(name)}', which the problem does not declare as a constant");

        if (!named.Add(name))
            return SmtRefusals.OfRow(SmtRefusalCodes.FIELD_INVALID, $"'project[{index}]' names '{SmtRefusals.Token(name)}' again: a constant is named once");

        if (uninterpreted.TryGetValue(name, out var sort))
        {
            return SmtRefusals.OfRow(SmtRefusalCodes.PROJECT_SORT_UNSUPPORTED,
                $"'project[{index}]' names '{SmtRefusals.Token(name)}', whose values hold elements of the sort '{SmtRefusals.Token(sort)}'. The problem only declares that sort (declare-sort): "
                + "the solver names its elements by names of its own that it takes in no later command, so an enumeration cannot tell it which models it has found");
        }

        return null;
    }

    #endregion
}
