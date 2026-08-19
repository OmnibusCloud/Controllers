namespace OutWit.Controller.Visualization.ParaView.State;

/// <summary>
/// One proxy instantiated by a ParaView state: its XML group and type (the allowlist key), its
/// state-local id, and its scalar properties (name → element values, in index order). Proxy-valued
/// properties (inputs, views) carry no element values.
/// </summary>
/// <param name="Group">XML group, for example sources, representations, views.</param>
/// <param name="Type">XML type, for example XMLUnstructuredGridReader.</param>
/// <param name="Id">State-local proxy id.</param>
/// <param name="Properties">Scalar properties in document order.</param>
public sealed record ParaViewStateProxy(
    string Group,
    string Type,
    string Id,
    IReadOnlyList<ParaViewStateProperty> Properties)
{
    #region Functions

    /// <summary>
    /// Finds a property by name.
    /// </summary>
    /// <param name="name">Property name.</param>
    /// <returns>The property or null.</returns>
    public ParaViewStateProperty? FindProperty(string name)
    {
        return Properties.FirstOrDefault(me => string.Equals(me.Name, name, StringComparison.Ordinal));
    }

    #endregion

    #region Properties

    /// <summary>
    /// The allowlist key of the proxy: group/type.
    /// </summary>
    public string Key => $"{Group}/{Type}";

    #endregion
}
