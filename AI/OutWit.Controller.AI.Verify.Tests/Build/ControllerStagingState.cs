namespace OutWit.Controller.AI.Verify.Tests.Build;

/// <summary>
/// What one build of <see cref="ControllerStagingProbe"/> left behind, read into memory so that
/// the state after an earlier build can be compared with the state after a later one.
/// </summary>
/// <param name="Folder">Every file of the module folder by its path inside it, with its text.</param>
/// <param name="Records">The files each project's record lists, by the project's name.</param>
/// <param name="Zipped">The entries of the module's zip, in order.</param>
/// <param name="Packed">The files the package ships under <c>content/module/</c>, in order.</param>
/// <param name="StatedDigest">The content digest the staged manifest states.</param>
/// <param name="PackedDigest">The content digest of the files the package ships, computed by its definition.</param>
internal sealed record ControllerStagingState(
    IReadOnlyDictionary<string, string> Folder,
    IReadOnlyDictionary<string, string[]> Records,
    string[] Zipped,
    string[] Packed,
    string StatedDigest,
    string PackedDigest);
