using System.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A job's variables as a host activity reads them, without the engine and without the variable
/// types of the modules that declare them: a value by name, whatever its type, read and written as
/// the engine's collection reads and writes them (a value is taken when it is of the type asked
/// for). What a host adapter does not ask of its variables is not there.
/// </summary>
internal sealed class SmtTestVariables : IWitVariablesCollection
{
    #region Fields

    private readonly Dictionary<string, object?> m_values = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>Puts a value under a name.</summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="value">Its value.</param>
    /// <returns>The variables, for the next value.</returns>
    public SmtTestVariables With(string name, object? value)
    {
        m_values[name] = value;
        return this;
    }

    /// <summary>The value under a name, or null when there is none.</summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>The value.</returns>
    public object? Of(string name)
    {
        return m_values.GetValueOrDefault(name);
    }

    #endregion

    #region IWitVariablesCollection

    /// <inheritdoc />
    public bool TryGetValue<TValue>(string? key, out TValue? value)
    {
        value = default;
        if (key == null || !m_values.TryGetValue(key, out var stored) || stored is not TValue typed)
            return false;

        value = typed;
        return true;
    }

    /// <inheritdoc />
    public bool TrySetValue<TValue>(string? key, TValue? value)
    {
        if (key == null)
            return false;

        m_values[key] = value;
        return true;
    }

    /// <inheritdoc />
    public bool TryRemove(string? key)
    {
        return key != null && m_values.Remove(key);
    }

    /// <inheritdoc />
    public bool TryGetString(string? key, out string? value)
    {
        return TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public bool TryGetObject(string? key, out object? value)
    {
        value = null;
        return key != null && m_values.TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public bool TryGetCollection<TValue>(string? key, out IReadOnlyList<TValue>? value)
    {
        return TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public bool TrySetCollection<TValue>(string? key, IReadOnlyList<TValue> value)
    {
        return TrySetValue(key, value);
    }

    /// <inheritdoc />
    public IWitVariablesCollection Join(IEnumerable<IWitVariable> variables)
    {
        throw new NotSupportedException("a host activity does not join variables");
    }

    /// <inheritdoc />
    public IWitVariablesCollection Merge(IEnumerable<IWitVariable> variables)
    {
        throw new NotSupportedException("a host activity does not merge variables");
    }

    /// <inheritdoc />
    public IWitVariable this[string key] => throw new NotSupportedException("a host activity reads values, not variables");

    /// <inheritdoc />
    public int Count => 0;

    /// <inheritdoc />
    public IEnumerator<IWitVariable> GetEnumerator()
    {
        return Enumerable.Empty<IWitVariable>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public object Clone()
    {
        throw new NotSupportedException("a host activity does not clone the job's variables");
    }

    #endregion
}
