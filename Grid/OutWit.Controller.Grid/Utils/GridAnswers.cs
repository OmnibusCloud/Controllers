using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Grid.Builders;
using OutWit.Controller.Grid.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Utils;

/// <summary>
/// What a task asks its node for, and what is kept of the node's answer. Both strategies of
/// Grid.ForEach use it, and so does Grid.Delegate for its one task, so a node is asked and held the
/// same way whether its tasks travel as one batch or one by one.
///
/// A task asks for one value: the return slot Grid gave its activity. A value is kept when it comes
/// under a name the tasks asked for, as many times as they asked for it, and when the return
/// collection of the call would take it. Anything else is left out, and the rest of the answer stands.
/// </summary>
internal static class GridAnswers
{
    #region Constants

    private const string SLOT_PREFIX = WitGridTaskBuilder.RETURN_VARIABLE_NAME;

    private const int MAX_REASONS = 4;

    private static readonly IReadOnlyList<object?> NO_VALUES = Array.Empty<object?>();

    private static readonly IReadOnlyList<string> NO_NAMES = Array.Empty<string>();

    #endregion

    #region Asked

    /// <summary>
    /// The names a task asks its node for. It is the return reference Grid set on the task's own
    /// activity: a variable of the script is not a second slot, whatever its name. A task whose
    /// activity carries no return reference asks for the variables it carries under one of Grid's
    /// own slot names, the prefix and a number. That keeps a task built by hand working (a slot
    /// variable and an activity that is not a function). A task Grid builds for a transformer that
    /// returns nothing has no slot variable, so it asks for nothing, unless the script itself has a
    /// variable named exactly like a slot.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The names, each once.</returns>
    public static IReadOnlyList<string> AskedNames(this WitGridTask task)
    {
        if (task.Activity is IWitFunction function && !string.IsNullOrEmpty(function.ReturnReference))
            return new[] { function.ReturnReference };

        if (task.Variables == null)
            return NO_NAMES;

        List<string>? names = null;
        foreach (var variable in task.Variables)
        {
            if (IsSlotName(variable.Name))
                (names ??= new List<string>()).Add(variable.Name);
        }

        return names ?? NO_NAMES;
    }

    /// <summary>
    /// Says whether a name is one Grid gives a return slot: the prefix and a number, nothing else.
    /// </summary>
    private static bool IsSlotName(string? name)
    {
        if (name == null || name.Length <= SLOT_PREFIX.Length || !name.StartsWith(SLOT_PREFIX, StringComparison.Ordinal))
            return false;

        for (int index = SLOT_PREFIX.Length; index < name.Length; index++)
        {
            if (name[index] < '0' || name[index] > '9')
                return false;
        }

        return true;
    }

    #endregion

    #region Kept

    /// <summary>
    /// The values of an answer that its tasks asked for and the return collection would take, in the
    /// order the node returned them. A name is good for as many values as tasks asked for it. The
    /// first value under an asked name is that task's answer: when the collection would refuse it,
    /// the task has no answer, and a later value under the same name does not stand in for it. The
    /// list itself comes back when nothing is left out.
    /// </summary>
    /// <param name="tasks">The tasks the node was given.</param>
    /// <param name="variables">What the node returned for them.</param>
    /// <param name="values">What the return collection takes; null when every value is taken.</param>
    /// <param name="unasked">How many values came under no name the tasks asked for, or once too often.</param>
    /// <param name="refused">The values that were asked for and that the collection would refuse.</param>
    /// <returns>The values that are kept.</returns>
    public static IReadOnlyList<IWitVariable> Keep(IEnumerable<WitGridTask> tasks, IReadOnlyList<IWitVariable> variables,
        GridValueRule? values, out int unasked, out IReadOnlyList<object?> refused)
    {
        unasked = 0;
        refused = NO_VALUES;

        if (variables.Count == 0)
            return variables;

        var asked = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            foreach (string name in task.AskedNames())
                asked[name] = asked.GetValueOrDefault(name) + 1;
        }

        List<IWitVariable>? kept = null;
        List<object?>? refusedValues = null;

        for (int index = 0; index < variables.Count; index++)
        {
            var variable = variables[index];

            if (variable?.Name is string name && asked.TryGetValue(name, out int left) && left > 0)
            {
                asked[name] = left - 1;

                object? value = variable.Value;
                if (values == null || values.Accepts(value))
                {
                    kept?.Add(variable);
                    continue;
                }

                (refusedValues ??= new List<object?>()).Add(value);
            }
            else
                unasked++;

            // The first value left out: from here on the kept ones are a list of their own.
            kept ??= variables.Take(index).ToList();
        }

        if (refusedValues != null)
            refused = refusedValues;

        return kept ?? variables;
    }

    #endregion

    #region Warnings

    /// <summary>
    /// Reports what was left out of an answer: one warning for the values nobody asked for, one for
    /// the values the return collection would refuse. Nothing is reported for an answer kept whole.
    /// </summary>
    /// <param name="logWarning">Where the warnings go; null when nobody listens.</param>
    /// <param name="who">The node, as <see cref="Who"/> names it.</param>
    /// <param name="tasks">How many tasks the node was given.</param>
    /// <param name="unasked">How many values were not asked for.</param>
    /// <param name="refused">The values the collection would refuse.</param>
    /// <param name="values">The rule that refused them.</param>
    public static void Warn(Action<string>? logWarning, string who, int tasks, int unasked, IReadOnlyList<object?> refused, GridValueRule? values)
    {
        if (logWarning == null)
            return;

        if (unasked > 0)
            logWarning(
                $"{who} returned {unasked} value(s) its {tasks} task(s) did not ask for; "
                + "they are left out of the results.");

        if (refused.Count > 0)
            logWarning(
                $"{who} returned {refused.Count} value(s) the return collection would refuse ({Why(refused, values)}); "
                + "they are left out of the results and their task(s) count as not answered.");
    }

    /// <summary>
    /// Names the node an answer came from, as far as it is known. The plan names a node; without
    /// copies the server may give the work to another one. The status that comes back carries the id
    /// of the engine that made it, so the node is named by the status when it has one, and as the
    /// planned node otherwise.
    /// </summary>
    /// <param name="plannedNodeId">The node the work was planned for.</param>
    /// <param name="status">The status that came back with the answer.</param>
    /// <param name="work">What was planned: "batch" or "task".</param>
    /// <returns>The subject of a warning about the answer.</returns>
    public static string Who(Guid plannedNodeId, IWitProcessingStatus? status, string work)
    {
        Guid named = status?.EngineId ?? Guid.Empty;

        if (named == plannedNodeId)
            return $"Node {plannedNodeId}";

        if (named == Guid.Empty)
            return $"The node that ran the {work} planned for node {plannedNodeId}";

        return $"Node {named}, as the returned status names it (the {work} was planned for node {plannedNodeId}),";
    }

    private static string Why(IReadOnlyList<object?> refused, GridValueRule? values)
    {
        var reasons = refused
            .GroupBy(value => GridValueRule.Describe(value), StringComparer.Ordinal)
            .Select(group => $"{group.Count()} {group.Key}")
            .ToList();

        string listed = string.Join(", ", reasons.Take(MAX_REASONS));
        if (reasons.Count > MAX_REASONS)
            listed += $", and {reasons.Count - MAX_REASONS} more kind(s)";

        return values?.ElementType == null
            ? listed
            : $"{listed} where {values.ElementType.Name} was expected";
    }

    #endregion
}
