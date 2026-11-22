namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>One data asset as a module's manifest declares it.</summary>
/// <param name="Id">The asset's id.</param>
/// <param name="RuntimeIdentifier">The platform it is for, or "any".</param>
/// <param name="Uri">Where it comes from: <c>gh-release://owner/repository/tag/file</c>.</param>
/// <param name="Sha256">The SHA-256 of its archive.</param>
/// <param name="Size">The size of its archive in bytes.</param>
/// <param name="ExtractTo">The folder of the module it is unpacked into.</param>
internal sealed record VerifyPackagedAsset(string Id, string RuntimeIdentifier, string Uri, string Sha256, long Size, string ExtractTo);
