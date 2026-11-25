using System;
using System.Collections.Generic;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Utils;

/// <summary>
/// What the return collection of a Grid.ForEach call takes. A typed collection takes a list whole or
/// not at all: one null, or one value of another type than its elements, and it keeps the values it
/// had. So a value is held to this rule before it joins the results of a call: a collection of
/// objects takes every value, any other collection takes a value that is of its element type. A call
/// with no return collection has no rule.
///
/// The rule of a collection that asks to be told which item each value answers (see
/// <see cref="ForCollection(IWitVariable, IReadOnlyDictionary{string, object}, Action{string})"/>)
/// takes the same values and tells the collection of every value that is kept. It is a rule of its
/// own, made for one call: a rule that tells nobody, <see cref="ANY"/> among them, never changes.
/// </summary>
public sealed class GridValueRule
{
    #region Constants

    /// <summary>The rule that takes every value: no return collection, or a collection of objects.</summary>
    public static readonly GridValueRule ANY = new(null);

    #endregion

    #region Fields

    private readonly GridAnswerListener? m_listener;

    #endregion

    #region Constructors

    private GridValueRule(Type? elementType)
    {
        ElementType = elementType;
    }

    private GridValueRule(Type? elementType, GridAnswerListener listener)
    {
        ElementType = elementType;
        m_listener = listener;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The rule of a collection whose elements are of the given type.
    /// </summary>
    /// <param name="elementType">The element type; null or <see cref="object"/> for a collection that takes every value.</param>
    /// <returns>The rule.</returns>
    public static GridValueRule ForElementType(Type? elementType)
    {
        if (elementType == null || elementType == typeof(object))
            return ANY;

        // A collection of a nullable value type takes the value itself and refuses null, as any other.
        return new GridValueRule(Nullable.GetUnderlyingType(elementType) ?? elementType);
    }

    /// <summary>
    /// The rule of a collection variable, read from the engine's typed collection it derives from.
    /// </summary>
    /// <param name="collection">The variable a call returns into; null when there is none.</param>
    /// <returns>The rule; <see cref="ANY"/> when the variable is not a typed collection.</returns>
    public static GridValueRule ForCollection(IWitVariable? collection)
    {
        for (Type? type = collection?.GetType(); type != null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WitCollection<>))
                return ForElementType(type.GetGenericArguments()[0]);
        }

        return ANY;
    }

    /// <summary>
    /// The rule of a collection variable that may ask to be told which item each value answers. A
    /// collection asks by declaring <c>public void OnGridAnswer(object? item, object? value)</c>: its
    /// rule takes what <see cref="ForCollection(IWitVariable)"/> says it takes, and the collection
    /// is called once for every value that is kept, with the item of the return slot the value came
    /// under. A collection without the method has the rule it always had. A public method of that
    /// name with another signature is warned of, once, and never called.
    /// </summary>
    /// <param name="collection">The variable a call returns into; null when there is none.</param>
    /// <param name="items">The item of every return slot of the call, by slot name. It is read when a value is kept, so it may be filled after this call; it must not change while the call runs.</param>
    /// <param name="logWarning">Where the warning about a method of another signature goes; null when nobody listens.</param>
    /// <returns>The rule: a new one when the collection asks to be told, the one of <see cref="ForCollection(IWitVariable)"/> when it does not.</returns>
    public static GridValueRule ForCollection(IWitVariable? collection, IReadOnlyDictionary<string, object?> items, Action<string>? logWarning)
    {
        GridValueRule rule = ForCollection(collection);

        GridAnswerListener? listener = GridAnswerListener.Find(collection, items, logWarning);

        return listener == null
            ? rule
            : new GridValueRule(rule.ElementType, listener);
    }

    /// <summary>
    /// Says whether the collection would take the value.
    /// </summary>
    /// <param name="value">A value a node returned.</param>
    /// <returns>True when the value may join the results.</returns>
    public bool Accepts(object? value)
    {
        return ElementType == null || ElementType.IsInstanceOfType(value);
    }

    /// <summary>
    /// Says in one warning how many values of the call the return collection was not told of because
    /// its method threw, and the type of the first exception. The values were kept all the same.
    /// Nothing is said for a collection that does not ask, and for one whose method never threw; what
    /// was said is not said again.
    /// </summary>
    /// <param name="logWarning">Where the warning goes; null when nobody listens.</param>
    public void WarnOfUntold(Action<string>? logWarning)
    {
        m_listener?.WarnOfUntold(logWarning);
    }

    /// <summary>
    /// Tells the return collection, when it asked, of a value that is kept: the item of the return
    /// slot the value came under, and the value. Under the Queued strategy this runs in the loop of
    /// the node that answered, so the collection may be called from several threads at once.
    /// </summary>
    /// <param name="slot">The name the value came under.</param>
    /// <param name="value">The value, as it joins the results.</param>
    internal void Tell(string slot, object? value)
    {
        m_listener?.Tell(slot, value);
    }

    /// <summary>
    /// Describes a value for a warning: that it is null, or its type.
    /// </summary>
    /// <param name="value">A value a node returned.</param>
    /// <returns>The description.</returns>
    public static string Describe(object? value)
    {
        return value == null ? "null" : $"of type {value.GetType().Name}";
    }

    #endregion

    #region Properties

    /// <summary>
    /// The type a value has to be of; null when every value is taken.
    /// </summary>
    public Type? ElementType { get; }

    /// <summary>
    /// True when the return collection asked to be told which item each kept value answers.
    /// </summary>
    public bool Tells => m_listener != null;

    #endregion
}
