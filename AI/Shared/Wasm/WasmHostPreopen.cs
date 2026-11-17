namespace OutWit.Controller.AI.Wasm;

/// <summary>A host folder a guest sees, at the guest path it sees it under.</summary>
/// <param name="HostPath">The folder on the host.</param>
/// <param name="GuestPath">The path the guest opens it by (e.g. <c>/</c>, <c>/in</c>).</param>
/// <param name="Writable">Whether the guest may create and change files in it; read-only by default.</param>
/// <param name="Names">
/// The exact names the guest may reach in the folder, or null for whatever the host's file system
/// holds and however it matches names. With a table the folder is read-only in effect: a name
/// outside it does not exist, and nothing can be made in it.
/// </param>
internal sealed record WasmHostPreopen(string HostPath, string GuestPath, bool Writable = false, WasmHostNames? Names = null);
