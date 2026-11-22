namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>A node of the pool as the share plan sees it: its benchmark rate and whether it has measured itself. Nothing that names it.</summary>
/// <param name="Rate">Megabytes of Common Crawl WET per second (the benchmark's unit); the engine's default for a node that has not measured itself.</param>
/// <param name="Measured">Whether the node has measured itself on the reference shard.</param>
public readonly record struct CorpusShareNode(double Rate, bool Measured);
