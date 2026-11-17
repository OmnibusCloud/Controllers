namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>A shard that arrived.</summary>
/// <param name="Bytes">The bytes written to the file.</param>
/// <param name="Waited">How long the fetch waited for a download slot first.</param>
/// <param name="Framed">
/// Whether the origin said where the body ends (a length, chunks, an HTTP/2 stream), so that a
/// body cut short was seen as a failure and tried again. False for a body that ends when the
/// connection closes: such a body can end early unnoticed, and the caller holds a shard that ends
/// inside a record against it.
/// </param>
public sealed record ShardFetchOutcome(long Bytes, TimeSpan Waited, bool Framed);
