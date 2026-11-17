namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>A source whose size at the origin is far above the one its manifest entry declares.</summary>
/// <param name="Index">The source's index in the manifest.</param>
/// <param name="DeclaredBytes">What the manifest declares.</param>
/// <param name="OriginBytes">What the origin answered.</param>
public sealed record ShardSizeMismatch(int Index, long DeclaredBytes, long OriginBytes);
