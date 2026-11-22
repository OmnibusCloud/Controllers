using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>A result package the server accepted: the task package it answers and its batches.</summary>
/// <param name="Unit">The task package the results answer.</param>
/// <param name="Sha256">The SHA-256 of the result blob.</param>
/// <param name="Batches">The package's batches: each one's provenance and the executions it holds.</param>
internal sealed record VerifyAcceptedPackage(VerifyTaskPackageData Unit, string Sha256, List<VerifyAcceptedBatch> Batches);
