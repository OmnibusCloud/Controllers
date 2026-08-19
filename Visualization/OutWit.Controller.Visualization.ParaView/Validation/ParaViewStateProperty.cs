namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// One scalar property of a state proxy with its element values in index order, and whether the
/// state marks it as a file list (a <c>&lt;Domain name="files"&gt;</c> child — how ParaView saves
/// reader file properties).
/// </summary>
/// <param name="Name">Property name.</param>
/// <param name="Values">Element values ordered by index.</param>
/// <param name="HasFileDomain">True when the property carries a files domain.</param>
public sealed record ParaViewStateProperty(string Name, IReadOnlyList<string> Values, bool HasFileDomain = false);
