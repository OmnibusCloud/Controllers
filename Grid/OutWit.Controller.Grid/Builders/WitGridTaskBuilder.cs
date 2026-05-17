using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

            var variables = BuildVariables(pool, iterationVariableName, returnVariableName, value);
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

    private static IWitVariablesCollection BuildVariables(IWitVariablesCollection pool, string iterationVariableName, string? returnVariableName, object value)
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
            
        return variables.Join(pool);
    }
    
    public static bool IsReturnVariable(this IWitVariable variable)
    {
        return variable.Name.StartsWith(RETURN_VARIABLE_NAME);
    }
}