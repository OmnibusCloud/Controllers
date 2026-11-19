using System;
using System.Linq;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests.Mock
{
    /// <summary>
    /// Tasks and nodes for tests that run no engine. A task comes in the three shapes Grid.ForEach
    /// knows: as Grid builds it for a transformer that returns a value (the slot is the return
    /// reference of the task's own activity), built by hand around an activity that is not a function
    /// (the slot is the variable under one of Grid's slot names), and as Grid builds it for a
    /// transformer that returns nothing (no slot).
    /// </summary>
    internal static class GridTestTasks
    {
        #region Constants

        private const string SLOT_PREFIX = "return";

        #endregion

        #region Functions

        /// <summary>
        /// The name Grid gives the return slot of the task with the given number.
        /// </summary>
        /// <param name="index">The number of the task.</param>
        /// <returns>The slot name.</returns>
        public static string ReturnName(int index)
        {
            return $"{SLOT_PREFIX}{index}";
        }

        /// <summary>
        /// A measured node of its own machine.
        /// </summary>
        /// <param name="rate">The benchmark rate.</param>
        /// <returns>The node.</returns>
        public static IWitEngineActivityNode Node(double rate)
        {
            return new MockActivityNode(WitEngineNodeSdk.Instance, rate, Guid.NewGuid(), iterations: 1);
        }

        /// <summary>
        /// A task as Grid builds it for a transformer that returns a value.
        /// </summary>
        /// <param name="index">The number of the task and of its slot.</param>
        /// <param name="replicaOf">The item the task is a copy of, when it is one.</param>
        /// <param name="scriptVariables">Variables of the script the task carries besides its slot.</param>
        /// <returns>The task.</returns>
        public static WitGridTask Built(int index, int? replicaOf = null, params string[] scriptVariables)
        {
            var activity = new WitActivitySpecialZip { Values = new IWitParameter[] { (WitReference)"item" } };
            activity.SetReturnReference(ReturnName(index));

            return new WitGridTask
            {
                Work = 1,
                ReplicaOf = replicaOf,
                Variables = Variables(ReturnName(index), index, scriptVariables),
                Activity = activity
            };
        }

        /// <summary>
        /// A task built by hand: a variable under a slot name and an activity that names no slot.
        /// </summary>
        /// <param name="index">The number of the task and of its slot.</param>
        /// <param name="replicaOf">The item the task is a copy of, when it is one.</param>
        /// <param name="scriptVariables">Variables of the script the task carries besides its slot.</param>
        /// <returns>The task.</returns>
        public static WitGridTask ByHand(int index, int? replicaOf = null, params string[] scriptVariables)
        {
            return new WitGridTask
            {
                Work = 1,
                ReplicaOf = replicaOf,
                Variables = Variables(ReturnName(index), index, scriptVariables),
                Activity = new WitActivitySpecialTrace { Message = (WitReference)"item" }
            };
        }

        /// <summary>
        /// A task of either shape that has a slot.
        /// </summary>
        /// <param name="built">True for a task as Grid builds it, false for one built by hand.</param>
        /// <param name="index">The number of the task and of its slot.</param>
        /// <param name="replicaOf">The item the task is a copy of, when it is one.</param>
        /// <param name="scriptVariables">Variables of the script the task carries besides its slot.</param>
        /// <returns>The task.</returns>
        public static WitGridTask Either(bool built, int index, int? replicaOf = null, params string[] scriptVariables)
        {
            return built
                ? Built(index, replicaOf, scriptVariables)
                : ByHand(index, replicaOf, scriptVariables);
        }

        /// <summary>
        /// A task as Grid builds it for a transformer that returns nothing: no slot at all.
        /// </summary>
        /// <param name="index">The number of the task.</param>
        /// <param name="scriptVariables">Variables of the script the task carries.</param>
        /// <returns>The task.</returns>
        public static WitGridTask WithoutSlot(int index, params string[] scriptVariables)
        {
            return new WitGridTask
            {
                Work = 1,
                Variables = Variables(null, index, scriptVariables),
                Activity = new WitActivitySpecialTrace { Message = (WitReference)"item" }
            };
        }

        /// <summary>
        /// Says whether a task asks its node for the variable of the given name, as the tests state the
        /// rule: the return reference of the task's activity, or for a task built by hand a name that
        /// is the slot prefix and a number.
        /// </summary>
        /// <param name="task">The task.</param>
        /// <param name="name">The name of a variable the task carries.</param>
        /// <returns>True when the name is the task's slot.</returns>
        public static bool IsSlot(WitGridTask task, string name)
        {
            if (task.Activity is IWitFunction function && !string.IsNullOrEmpty(function.ReturnReference))
                return name == function.ReturnReference;

            return name.Length > SLOT_PREFIX.Length
                   && name.StartsWith(SLOT_PREFIX, StringComparison.Ordinal)
                   && name.Skip(SLOT_PREFIX.Length).All(char.IsAsciiDigit);
        }

        /// <summary>
        /// Says whether a task asked for the variable of the given name up to 1.3.0, when every name
        /// that starts with the slot prefix passed for a return variable.
        /// </summary>
        /// <param name="task">The task.</param>
        /// <param name="name">The name of a variable the task carries.</param>
        /// <returns>True when the name was asked for.</returns>
        public static bool WasAskedUpTo130(WitGridTask task, string name)
        {
            return name.StartsWith(SLOT_PREFIX, StringComparison.Ordinal);
        }

        private static WitVariableCollection Variables(string? slot, int index, string[] scriptVariables)
        {
            var variables = new WitVariableCollection();

            if (slot != null)
                variables.Add(new WitVariableObject(slot));

            variables.Add(new WitVariableObject("item", index));

            foreach (string name in scriptVariables)
                variables.Add(new WitVariableObject(name, $"script:{name}"));

            return variables;
        }

        #endregion
    }
}
