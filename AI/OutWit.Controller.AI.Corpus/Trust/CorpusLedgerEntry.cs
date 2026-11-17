using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>One value that came back under a shard's ticket, after the checks.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Result">The result when it is an execution or a fault (checked: no null inside); null for a rejected value, which nothing reads.</param>
/// <param name="Code">The fault's code, the reason of the rejection, or "" for an execution.</param>
public sealed record CorpusLedgerEntry(CorpusLedgerEntryKind Kind, CorpusResultData? Result, string Code);
