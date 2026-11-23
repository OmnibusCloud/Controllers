using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The solver asked about a text no sanitizer has looked at: the profile's options, the text and
/// <c>(check-sat)</c>, in the process's sandbox, with the solver's answer as it wrote it. For the
/// tests that must know what the solver itself does with a text the sanitizer refuses; a
/// session of the controller never runs such a text.
/// <para>
/// The controller's types stand in method bodies only, as in every helper that runs the solver:
/// no signature here names one.
/// </para>
/// </summary>
internal static class SmtRawSolver
{
    #region Constants

    private const ulong RESOURCE_LIMIT = 50_000_000;
    private const int HARD_CAP_SECONDS = 10;

    #endregion

    #region Functions

    /// <summary>
    /// Gives the solver a text and <c>(check-sat)</c>.
    /// </summary>
    /// <param name="text">The commands, as they are: nothing is checked.</param>
    /// <returns>What the solver wrote: <c>sat</c>, <c>unsat</c>, <c>unknown</c>, an error, or several of them.</returns>
    /// <exception cref="InvalidOperationException">This process has no sandbox for the solver.</exception>
    public static string Ask(string text)
    {
        var sandbox = SmtSandboxHost.Get(out var unavailable) ?? throw new InvalidOperationException(unavailable);
        var envelope = SmtProfile.Envelope(new SmtOptionsData { HardCapSeconds = HARD_CAP_SECONDS });
        var setup = SmtScripts.Text([.. SmtProfile.Options(0), SmtScripts.SetResourceLimit(RESOURCE_LIMIT)]);
        var conversation = new SmtRawConversation($"{setup}{text}\n(check-sat)\n");

        sandbox.Run(new WasmHostConversation(conversation.Next), envelope);
        return conversation.Answer.Trim();
    }

    #endregion
}
