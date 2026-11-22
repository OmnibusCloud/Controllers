using System;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Utils;

/// <summary>
/// What the return collection of a Grid.ForEach call takes. A typed collection takes a list whole or
/// not at all: one null, or one value of another type than its elements, and it keeps the values it
/// had. So a value is held to this rule before it joins the results of a call: a collection of
/// objects takes every value, any other collection takes a value that is of its element type. A call
/// with no return collection has no rule.
/// </summary>
public sealed class GridValueRule
{
    #region Constants

    /// <summary>The rule that takes every value: no return collection, or a collection of objects.</summary>
    public static readonly GridValueRule ANY = new(null);

    #endregion

    #region Constructors

    private GridValueRule(Type? elementType)
    {
        ElementType = elementType;
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
    /// Says whether the collection would take the value.
    /// </summary>
    /// <param name="value">A value a node returned.</param>
    /// <returns>True when the value may join the results.</returns>
    public bool Accepts(object? value)
    {
        return ElementType == null || ElementType.IsInstanceOfType(value);
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

    #endregion
}
