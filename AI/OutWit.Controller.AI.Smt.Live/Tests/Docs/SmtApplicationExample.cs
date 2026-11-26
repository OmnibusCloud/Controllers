using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Docs;

/// <summary>
/// The call of the controller's application page (<c>docs/application.md</c>, "The call"),
/// compiled against the platform's SDK and the model package: the page's example is the body of
/// <see cref="RunAsync"/>, held to it by <see cref="SmtApplicationPageTests"/>, so the example
/// names only what the SDK and the model package have. It is never run here: it needs a server.
/// </summary>
internal static class SmtApplicationExample
{
    #region Functions

    /// <summary>Runs a problem set through SmtRun and downloads its four documents into a folder.</summary>
    /// <param name="serverUrl">The server's address.</param>
    /// <param name="identityUrl">The identity server's address.</param>
    /// <param name="apiKey">A <c>wit_sk_</c> application key.</param>
    /// <param name="folder">Where the documents go.</param>
    /// <returns>A task that ends when the four documents are on disk.</returns>
    /// <exception cref="InvalidOperationException">The job did not complete.</exception>
    public static async Task RunAsync(string serverUrl, string identityUrl, string apiKey, string folder)
    {
        // <example>
        await using var client = new WitCloudClient(serverUrl, identityUrl, apiKey);   // a wit_sk_ application key
        await client.ConnectAsync();

        // The problem set goes up as a blob, in chunks; a break is resumed.
        var problems = await client.Blobs.UploadBlobFromFileAsync("problems.jsonl");
        var options = new SmtOptionsData { HardCapSeconds = 30, SamplingRate = 0.25 };

        var job = await client.Scripts.SubmitAsync("SmtRun", [problems, options]);

        // Follow the job until it ends; the progress is a number from 0 to 1.
        var end = await job.WaitAsync<Guid?>("report", new Progress<double>(fraction => Console.WriteLine($"{fraction:P0}")), TimeSpan.FromSeconds(3));
        if (end.Status != ProcessingJobStatus.Completed)
            throw new InvalidOperationException($"{end.Status}: {end.ErrorMessage}");

        // The four documents are blobs, named by the job's variables.
        foreach (var (variable, file) in new[] { ("results", "results.jsonl"), ("report", "report.json"), ("provenance", "provenance.json"), ("integrity", "integrity.json") })
        {
            var blob = await client.Jobs.GetResultAsync<Guid?>(job.JobId, variable);
            await client.Blobs.DownloadBlobToFileAsync(blob!.Value, Path.Combine(folder, file));
        }
        // </example>
    }

    #endregion
}
