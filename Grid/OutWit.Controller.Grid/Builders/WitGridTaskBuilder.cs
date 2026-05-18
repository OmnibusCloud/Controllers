using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OutWit.Controller.Grid.Model;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Builders;

internal static class WitGridTaskBuilder
{
    #region Constants

    private const string RETURN_VARIABLE_NAME = "return";

    #endregion

    public static IReadOnlyList<WitGridTask> BuildTasks(this IWitControllerManager me, IEnumerable collection, IWitActivity activity, IWitVariablesCollection pool ,string iterationVariableName )
    {
        var tasks = new List<WitGridTask>();

        int index = 0;
        foreach (var value in collection)
        {
            string? returnVariableName = null;
            var taskActivity = (IWitActivity)activity.Clone();

            if (taskActivity is IWitFunction function)
            {
                returnVariableName = $"{RETURN_VARIABLE_NAME}{index++}";
                function.SetReturnReference(returnVariableName);
            }

            var variables = BuildVariables(pool, iterationVariableName, returnVariableName, value, taskActivity);
            var work = me.EstimateWork(taskActivity, variables);

            tasks.Add(new WitGridTask
            {
                Work = work,
                Variables = variables,
                Activity = taskActivity
            });
        }

        return tasks.OrderByDescending(task => task.Work).ToList();
    }

    private static IWitVariablesCollection BuildVariables(IWitVariablesCollection pool, string iterationVariableName, string? returnVariableName, object value, IWitActivity transformerActivity)
    {
        var variables = new WitVariableCollection();
        if(!string.IsNullOrEmpty(returnVariableName))
            variables.Add(new WitVariableObject(returnVariableName));

        if (value is string || value is not IEnumerable collection)
            variables.Add(new WitVariableObject(iterationVariableName, value));
        else
        {
            int i = 1;
            foreach(object? item in collection)
                variables.Add(new WitVariableObject($"{iterationVariableName}.Item{i++}", item));
        }

        // Only forward variables that the transformer actually references —
        // anything else from the outer host pool is unused on the node side
        // and would waste serialised payload bytes per task. Distributed
        // ForEach over thousands of iterations would otherwise multiply the
        // outer-scope cost by the iteration count. Variables added above
        // (iteration var + return var) are unconditionally included; the
        // referenced subset of pool joins them.
        var referenced = CollectReferences(transformerActivity);
        var iterationPrefix = iterationVariableName + ".Item";
        var filteredPool = pool.Where(variable =>
            referenced.Contains(variable.Name)
            // Iteration variable and its tuple-expanded sub-items must always
            // pass — they're per-task locals introduced just above.
            || variable.Name == iterationVariableName
            || variable.Name.StartsWith(iterationPrefix));
        return variables.Join(filteredPool);
    }

    /// <summary>
    /// Walks <paramref name="activity"/>'s declared properties (recursively
    /// through nested IWitParameter / IWitArray / IWitActivity values) and
    /// collects every <see cref="IWitReference.Reference"/> name. Lets
    /// <see cref="BuildVariables"/> ship only the outer-pool variables the
    /// transformer actually consumes to remote nodes.
    /// </summary>
    private static HashSet<string> CollectReferences(IWitActivity activity)
    {
        var refs = new HashSet<string>(System.StringComparer.Ordinal);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(activity, refs, visited);
        return refs;
    }

    private static void Walk(object? node, HashSet<string> refs, HashSet<object> visited)
    {
        if (node is null) return;
        if (!visited.Add(node)) return;

        switch (node)
        {
            case IWitReference reference when !string.IsNullOrEmpty(reference.Reference):
                refs.Add(reference.Reference!);
                return;
            case IWitConstant:
                return;
        }

        // IWitArray exposes nested parameters via IReadOnlyList<IWitParameter>.
        if (node is IEnumerable<IWitParameter> seq)
        {
            foreach (var item in seq)
                Walk(item, refs, visited);
            // Fall through to also inspect properties on the collection
            // itself (some types are both a sequence and carry extra state).
        }

        // Reflect over properties whose declared type can transport a reference:
        // anything assignable to IWitParameter, IWitActivity, or a sequence of
        // either. Skip primitives, strings, indexers, and write-only props.
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetMethod is null) continue;

            var propType = prop.PropertyType;
            if (!CanCarryReferences(propType)) continue;

            object? value;
            try { value = prop.GetValue(node); }
            catch { continue; }

            Walk(value, refs, visited);
        }
    }

    private static bool CanCarryReferences(System.Type type)
    {
        if (typeof(IWitParameter).IsAssignableFrom(type)) return true;
        if (typeof(IWitActivity).IsAssignableFrom(type)) return true;
        if (typeof(IEnumerable<IWitParameter>).IsAssignableFrom(type)) return true;
        if (typeof(IEnumerable<IWitActivity>).IsAssignableFrom(type)) return true;
        return false;
    }

    public static bool IsReturnVariable(this IWitVariable variable)
    {
        return variable.Name.StartsWith(RETURN_VARIABLE_NAME);
    }
}
