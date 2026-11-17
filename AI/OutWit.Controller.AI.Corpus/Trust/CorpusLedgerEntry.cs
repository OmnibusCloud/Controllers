using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>One value that came back under a shard's ticket, after the checks.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Result">The result when it is an execution, or a fault a node returned for the shard itself (checked: no null inside); null for a rejected value, which nothing reads, and for a shard of a package its node returned as a fault as a whole.</param>
/// <param name="Code">The fault's code, the reason of the rejection, or "" for an execution.</param>
public sealed record CorpusLedgerEntry(CorpusLedgerEntryKind Kind, CorpusResultData? Result, string Code);
