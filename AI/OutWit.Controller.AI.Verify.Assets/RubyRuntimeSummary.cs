namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>What the Ruby runtime folder was built from.</summary>
/// <param name="LibraryFiles">Files of the standard library.</param>
/// <param name="GemFiles">Library files of the bundled gems.</param>
/// <param name="Licences">The licence files of the bundled gems, as written.</param>
internal sealed record RubyRuntimeSummary(int LibraryFiles, int GemFiles, IReadOnlyList<string> Licences);
