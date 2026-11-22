namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>A result package a node wrote to a file.</summary>
/// <param name="Path">The package file, complete.</param>
/// <param name="Results">The results in it.</param>
/// <param name="Batches">The batches in it.</param>
public sealed record VerifyWrittenResults(string Path, int Results, int Batches);
