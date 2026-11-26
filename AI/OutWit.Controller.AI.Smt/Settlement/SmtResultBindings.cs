using System.Runtime.CompilerServices;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// Which package of problems each result package was returned for, as the platform told the host.
/// A result names the ticket of the package it answers, and that is the node's word: a node that
/// was given packages A and B may return a result of A under each of its two tasks. Grid (1.4.0
/// and later) knows which item every task was built from and tells the return collection of a
/// <c>Grid.ForEach</c> of every value it keeps (<c>OnGridAnswer</c> of
/// <see cref="Variables.WitVariableSmtResultPackageCollection"/>): the collection hands the pair to
/// this table, and the host's intake looks a result up here when it reads its round
/// (<see cref="SmtRoundIntake"/>).
/// <para>
/// What is remembered hangs on the result OBJECT, by its identity, and does not keep it alive: a
/// value Grid told and never stored (a call that failed after it was told) is simply never looked
/// up, and goes when nothing else holds it. Nothing of it is on the wire: no member of a type a
/// node sends or receives carries it, so a node cannot say it of itself. A copy of a result (a
/// clone of the collection, a result read back from bytes) is another object and is not bound:
/// it counts as a result of an older Grid. A result told for two different packages is bound to
/// none of them: it answers at most one, and the host cannot tell which.
/// </para>
/// <para>
/// Grid calls the collection one value after another under <c>Balanced</c>, the strategy of every
/// Smt fan-out, and promises nothing of the kind under <c>Queued</c>: every member here may be
/// called from several threads at once. Nothing here throws for what it is given: an exception
/// out of the collection's method would leave that result unbound.
/// </para>
/// </summary>
internal static class SmtResultBindings
{
    #region Constants

    // The ticket of a result told for two different packages: no ticket a package has.
    private const string TOLD_TWICE = "";

    // Result package -> the ticket of the package it was returned for, by the result's identity.
    private static readonly ConditionalWeakTable<SmtResultPackageData, string> TICKETS = new();

    #endregion

    #region Functions

    /// <summary>
    /// Remembers that a result was returned for a task of a package: the pair Grid tells a return
    /// collection. Anything else (no item, no value, an item that is not a package of problems, a
    /// value that is not a result package) is not remembered, and nothing is thrown.
    /// </summary>
    /// <param name="item">The item of the call's collection the task was built from.</param>
    /// <param name="value">The value the node returned for the task, as it joins the results.</param>
    public static void Bind(object? item, object? value)
    {
        if (item is not SmtTaskPackageData package || value is not SmtResultPackageData result)
            return;

        var ticket = package.Ticket ?? TOLD_TWICE;
        if (TICKETS.TryAdd(result, ticket))
            return;

        // Told before: for the same package it changes nothing; for another one the result is bound to neither.
        if (!TICKETS.TryGetValue(result, out var known) || !string.Equals(known, ticket, StringComparison.Ordinal))
            TICKETS.AddOrUpdate(result, TOLD_TWICE);
    }

    /// <summary>The ticket of the package a result was returned for, as the platform told it; null when it told nothing of this result.</summary>
    /// <param name="result">The result package, the object the round holds.</param>
    /// <returns>The ticket; empty for a result told for two packages; null for a result that is not bound.</returns>
    public static string? TicketOf(SmtResultPackageData result)
    {
        return TICKETS.TryGetValue(result, out var ticket) ? ticket : null;
    }

    #endregion
}
