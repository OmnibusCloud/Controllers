namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Format of the verdict report assembled by Verify.Collect.
/// </summary>
public enum VerifyReportFormat
{
    /// <summary>One JSON object per result per line — the pipeline-native default.</summary>
    Jsonl = 0,

    /// <summary>A single JSON document with results and aggregates.</summary>
    Json = 1
}
