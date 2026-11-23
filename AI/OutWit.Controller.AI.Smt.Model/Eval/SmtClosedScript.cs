using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The closed script of a problem and a model: the problem with every symbol the model
/// defines turned from a declaration into a <c>define-fun</c> of the model's value or
/// interpretation. With nothing left to choose, a solver that is handed the script only
/// evaluates: <c>sat</c> says the model satisfies every assertion, <c>unsat</c> that it does
/// not. It is the second opinion on the evaluator: the solver itself, asked the same question.
/// <para>
/// The script is laid out so that everything is declared before it is used: the problem's
/// logic and sorts; the elements of the model's universes, as constants; the model's
/// definitions, each after the ones it mentions; the rest of the problem in its own order
/// (the symbols the model leaves out stay declared, so the solver chooses them); and
/// <c>(check-sat)</c>. What a problem says about itself with <c>set-info</c> is left out: it
/// means nothing to a solver. The script adds no name of its own.
/// </para>
/// <para>
/// The elements of a universe are declared, not defined: the script does not say that they
/// differ. A <c>sat</c> for it is still a witness; an <c>unsat</c> says that no choice of the
/// elements satisfies the assertions, the model's own choice included. An element the model's
/// definitions name without the model listing its sort's universe (a solver names an element
/// after its sort, <c>A!val!0</c> of a sort <c>A</c> the problem declares) is declared with
/// the listed ones (<see cref="Unlisted"/>).
/// </para>
/// </summary>
public sealed class SmtClosedScript
{
    #region Constants

    private const string SET_INFO = "set-info";
    private const string DECLARE_SORT = "declare-sort";
    private const string NO_PARAMETERS = "0";
    private const string DECLARE_CONST = "declare-const";
    private const string DECLARE_FUN = "declare-fun";
    private const string DEFINE_FUN = "define-fun";
    private const string DEFINE_FUN_REC = "define-fun-rec";
    private const string DEFINE_FUNS_REC = "define-funs-rec";
    private const string CHECK_SAT = "check-sat";
    private const char LINE_FEED = '\n';

    private static readonly HashSet<string> HEADER_COMMANDS = new(StringComparer.Ordinal)
    {
        "set-logic", "declare-sort", "define-sort", "declare-datatype", "declare-datatypes"
    };

    #endregion

    #region Constructors

    private SmtClosedScript(IReadOnlyList<SmtExpression> commands, int defined, int free, int unlisted)
    {
        Commands = commands;
        Defined = defined;
        Free = free;
        Unlisted = unlisted;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Builds the closed script.
    /// </summary>
    /// <param name="commands">The commands of the problem, in order.</param>
    /// <param name="model">The model; an unreadable one defines nothing, and every symbol stays declared.</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentNullException">The commands or the model are null.</exception>
    public static SmtClosedScript Build(IReadOnlyList<SmtExpression> commands, SmtModel model)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(model);

        var script = new List<SmtExpression>(commands.Count + model.UniverseElements + model.Definitions + 1);
        var rest = new List<SmtExpression>(commands.Count);
        var definitions = new SmtClosedDefinitions(model);
        var sorts = new Dictionary<string, SmtExpression>(StringComparer.Ordinal);
        var free = 0;

        // What the problem defines itself, the model does not define again.
        foreach (var command in commands)
            Reserve(command, definitions);

        foreach (var command in commands)
        {
            var name = NameOf(command);
            if (command == null || name == SET_INFO)
                continue;

            if (name != null && HEADER_COMMANDS.Contains(name))
            {
                script.Add(command);
                if (IsSortWithoutParameters(command, name))
                    sorts.TryAdd(command.Items[1].Text, command.Items[1]);
            }
            else if (!IsDeclaration(command, name))
                rest.Add(command);
            else if (!definitions.TryDefine(command))
            {
                rest.Add(command);
                free++;
            }
        }

        var defined = definitions.Declared;
        var ordered = definitions.InOrderOfUse();
        var unlisted = definitions.UnlistedElements(ordered, sorts);
        script.AddRange(model.ElementsInOrder);
        script.AddRange(unlisted);
        script.AddRange(ordered);
        script.AddRange(rest);
        script.Add(SmtExpression.List(SmtExpression.Symbol(CHECK_SAT)));
        return new SmtClosedScript(script, defined, free, unlisted.Count);
    }

    /// <summary>
    /// Writes the script as SMT-LIB text, one command a line.
    /// </summary>
    /// <param name="builder">Where to write.</param>
    /// <exception cref="ArgumentNullException">The builder is null.</exception>
    public void WriteTo(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var command in Commands)
        {
            command.WriteTo(builder);
            builder.Append(LINE_FEED);
        }
    }

    /// <summary>
    /// The script as SMT-LIB text (<see cref="WriteTo"/>).
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        WriteTo(builder);
        return builder.ToString();
    }

    /// <summary>
    /// The name of a command: the symbol a list starts with, not written between bars.
    /// </summary>
    private static string? NameOf(SmtExpression? command)
    {
        return command is { IsList: true, Items.Count: > 0 } && SmtTerms.IsSymbol(command.Items[0]) && !command.Items[0].IsQuoted ? command.Items[0].Text : null;
    }

    private static void Reserve(SmtExpression? command, SmtClosedDefinitions definitions)
    {
        if (command == null)
            return;

        switch (NameOf(command))
        {
            case DEFINE_FUN or DEFINE_FUN_REC when command.Items.Count > 1 && SmtTerms.IsSymbol(command.Items[1]):
                definitions.Reserve(command.Items[1].Text);
                break;
            case DEFINE_FUNS_REC when command.Items.Count > 1:
                foreach (var declaration in command.Items[1].Items)
                {
                    if (declaration.Items.Count > 0 && SmtTerms.IsSymbol(declaration.Items[0]))
                        definitions.Reserve(declaration.Items[0].Text);
                }

                break;
        }
    }

    /// <summary>
    /// Whether a command declares a sort without parameters, whose elements a model may name.
    /// </summary>
    private static bool IsSortWithoutParameters(SmtExpression command, string name)
    {
        var items = command.Items;
        return name == DECLARE_SORT && items.Count is 2 or 3 && SmtTerms.IsSymbol(items[1])
               && (items.Count == 2 || (items[2].Kind == SmtExpressionKind.Numeral && items[2].Text == NO_PARAMETERS));
    }

    /// <summary>
    /// Whether a command declares a constant or a function, in a shape that can be read.
    /// </summary>
    private static bool IsDeclaration(SmtExpression command, string? name)
    {
        return name switch
        {
            DECLARE_CONST => command.Items.Count == 3 && SmtTerms.IsSymbol(command.Items[1]),
            DECLARE_FUN => command.Items.Count == 4 && SmtTerms.IsSymbol(command.Items[1]) && command.Items[2].IsList,
            _ => false
        };
    }

    #endregion

    #region Properties

    /// <summary>The commands of the script, in order; <c>(check-sat)</c> is the last.</summary>
    public IReadOnlyList<SmtExpression> Commands { get; }

    /// <summary>How many of the symbols the problem declares the script defines from the model.</summary>
    public int Defined { get; }

    /// <summary>How many of them the model leaves out: they stay declared, and the solver chooses them.</summary>
    public int Free { get; }

    /// <summary>How many elements the script declares that the model's definitions name and its universes do not list.</summary>
    public int Unlisted { get; }

    #endregion
}
