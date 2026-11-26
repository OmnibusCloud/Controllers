using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Utils;

/// <summary>
/// A return collection of a Grid.ForEach call that asked to be told which item each value answers.
/// Grid knows which item every return slot was built for, and a value says nothing of it: a node
/// that was given two items may return a result of the first under both slots. A collection that
/// declares <c>public void OnGridAnswer(object? item, object? value)</c> is called once for every
/// value Grid keeps, with the item of the slot the value came under, before the value joins the
/// results. Grid does nothing with the call: what the controller makes of the pair is the
/// controller's.
///
/// The method is found by reflection, by its name and its exact signature, and not through an
/// interface. Grid is a host-only module: a type of Grid's on a controller's variable class would
/// not load on a node, where Grid is not installed.
///
/// The slots and their items stay on the host, beside the tasks. Nothing of them is sent to a node.
///
/// Under the Queued strategy an answer is held in the loop of the node that gave it, so the method
/// may be called from several threads at once. Nothing here locks around it: the code is the
/// controller's.
/// </summary>
internal sealed class GridAnswerListener
{
    #region Constants

    /// <summary>The name of the method a return collection declares to be told.</summary>
    public const string METHOD_NAME = "OnGridAnswer";

    private const BindingFlags PUBLIC_METHODS = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    private const string WARNING_OTHER_SIGNATURE = "Grid.ForEach: the return collection '{0}' ({1}) has a public method " + METHOD_NAME + " that is not 'public void " + METHOD_NAME + "(object item, object value)'; the collection is not told which item each value answers.";

    private const string WARNING_THREW = "Grid.ForEach: " + METHOD_NAME + " of the return collection '{0}' threw for {1} value(s) of the call (first exception: {2}); the value(s) are kept, and the collection was not told of them.";

    #endregion

    #region Fields

    private readonly string m_collectionName;

    private readonly Action<object?, object?> m_tell;

    private readonly IReadOnlyDictionary<string, object?> m_items;

    private int m_thrown;

    private Type? m_firstThrown;

    #endregion

    #region Constructors

    private GridAnswerListener(string collectionName, Action<object?, object?> tell, IReadOnlyDictionary<string, object?> items)
    {
        m_collectionName = collectionName;
        m_tell = tell;
        m_items = items;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Looks for the method on the variable a call returns into: once a call. A variable without a
    /// public method of that name is not told and nothing is said. A public method of that name
    /// with another signature is a mistake someone should hear of: one warning, and the variable is
    /// not told.
    /// </summary>
    /// <param name="collection">The variable a call returns into; null when there is none.</param>
    /// <param name="items">The item of every return slot of the call, by slot name. It is read when a value is kept, so it may be filled after this call.</param>
    /// <param name="logWarning">Where the warning goes; null when nobody listens.</param>
    /// <returns>The listener; null when the variable does not ask to be told.</returns>
    public static GridAnswerListener? Find(IWitVariable? collection, IReadOnlyDictionary<string, object?> items, Action<string>? logWarning)
    {
        if (collection == null)
            return null;

        MemberInfo[] named = collection.GetType().GetMember(METHOD_NAME, MemberTypes.Method, PUBLIC_METHODS);
        if (named.Length == 0)
            return null;

        foreach (MemberInfo member in named)
        {
            if (member is MethodInfo method && IsTheMethod(method))
                return new GridAnswerListener(collection.Name, method.CreateDelegate<Action<object?, object?>>(collection), items);
        }

        logWarning?.Invoke(string.Format(WARNING_OTHER_SIGNATURE, collection.Name, collection.GetType().Name));

        return null;
    }

    /// <summary>
    /// Tells the collection of a value that is kept: the item of the slot the value came under, and
    /// the value. A slot the call has no item for is not told. The method may throw: the value is
    /// kept all the same and the throw is counted for <see cref="WarnOfUntold"/>. Only an
    /// <see cref="OutOfMemoryException"/> goes on. A cancellation the method throws is held back
    /// like any other exception: the call's own cancellation does not come through this method, and
    /// a controller's method must not be able to end the call.
    /// </summary>
    /// <param name="slot">The name the value came under.</param>
    /// <param name="value">The value, as it joins the results.</param>
    public void Tell(string slot, object? value)
    {
        if (!m_items.TryGetValue(slot, out object? item))
            return;

        try
        {
            m_tell(item, value);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Interlocked.CompareExchange(ref m_firstThrown, exception.GetType(), null);
            Interlocked.Increment(ref m_thrown);
        }
    }

    /// <summary>
    /// Says in one warning how many values the collection was not told of because its method threw,
    /// and the type of the first exception. Nothing is said when it never threw. What was said is
    /// not said again.
    /// </summary>
    /// <param name="logWarning">Where the warning goes; null when nobody listens.</param>
    public void WarnOfUntold(Action<string>? logWarning)
    {
        int thrown = Interlocked.Exchange(ref m_thrown, 0);
        Type? first = Interlocked.Exchange(ref m_firstThrown, null);

        if (thrown == 0 || logWarning == null)
            return;

        logWarning(string.Format(WARNING_THREW, m_collectionName, thrown, first?.Name ?? nameof(Exception)));
    }

    private static bool IsTheMethod(MethodInfo method)
    {
        if (method.IsStatic || method.IsGenericMethodDefinition || method.ReturnType != typeof(void))
            return false;

        ParameterInfo[] parameters = method.GetParameters();

        return parameters.Length == 2
               && parameters[0].ParameterType == typeof(object)
               && parameters[1].ParameterType == typeof(object);
    }

    #endregion
}
