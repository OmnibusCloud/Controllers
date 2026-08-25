namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// One attachment as it landed in a task workspace: its materialized path and the digest and size
/// the copy actually had (what the composer stamps into the package reference it publishes).
/// </summary>
/// <param name="LogicalPath">The attachment's logical path.</param>
/// <param name="Path">Absolute path under the package root.</param>
/// <param name="Sha256">Lower-case hexadecimal SHA-256 of the materialized bytes.</param>
/// <param name="Size">Byte size of the materialized file.</param>
public sealed record ParaViewMaterializedAttachment(string LogicalPath, string Path, string Sha256, long Size);
