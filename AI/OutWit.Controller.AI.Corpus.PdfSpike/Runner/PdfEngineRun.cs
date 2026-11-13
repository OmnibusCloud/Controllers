using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.PdfSpike.Engines;
using OutWit.Controller.AI.Corpus.PdfSpike.Guards;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Runner;

/// <summary>
/// Runs one engine over one document in this process, on a thread of its own, and describes
/// the outcome as a <see cref="PdfRunRecord"/>. What it cannot catch — a stack overflow, the
/// operating system's out-of-memory killer — ends the process; the parent sees that.
/// </summary>
public static class PdfEngineRun
{
    #region Functions

    /// <summary>Creates an engine by name.</summary>
    /// <param name="name">pdfpig, pdfpig-budget or pdflibrary.</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentException">The name is unknown.</exception>
    public static IPdfTextEngine Create(string name)
    {
        return name switch
        {
            "pdfpig" => new PdfTextEnginePdfPig(null),
            "pdfpig-budget" => new PdfTextEnginePdfPig(new PdfDecodeBudget(64L << 20, 256L << 20, TimeSpan.FromSeconds(20))),
            "pdflibrary" => new PdfTextEnginePdfLibrary(),
            _ => throw new ArgumentException($"unknown engine '{name}'", nameof(name)),
        };
    }

    /// <summary>Extracts one document and measures it.</summary>
    /// <param name="engineName">The engine.</param>
    /// <param name="path">The PDF.</param>
    /// <param name="maxPages">Pages read at most (0 = all).</param>
    /// <param name="stackBytes">The extraction thread's stack (0 = the runtime's default).</param>
    /// <param name="textPath">Where to write the text, or null.</param>
    /// <returns>The record.</returns>
    public static PdfRunRecord Run(string engineName, string path, int maxPages, int stackBytes, string? textPath)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var record = new PdfRunRecord { Engine = engineName, File = Path.GetFileName(path), Bytes = bytes.LongLength };

        PdfTextOutcome? outcome = null;
        Exception? failure = null;
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var clock = Stopwatch.StartNew();

        var thread = new Thread(() =>
        {
            try
            {
                outcome = Create(engineName).Extract(bytes, maxPages);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }, stackBytes);

        thread.Start();
        thread.Join();
        clock.Stop();

        record.Ms = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        record.AllocatedMb = Math.Round((GC.GetTotalAllocatedBytes(true) - allocatedBefore) / 1048576.0, 1);
        using (var self = Process.GetCurrentProcess())
            record.SelfPeakMb = Math.Round(self.PeakWorkingSet64 / 1048576.0, 1);

        if (failure != null)
        {
            record.Verdict = failure is OutOfMemoryException ? "oom" : "error";
            record.Exception = failure.GetType().Name;
            record.Message = Clip(failure.Message);
            return record;
        }

        var result = outcome!;
        record.Verdict = "ok";
        record.Pages = result.Pages;
        record.PagesRead = result.PagesRead;
        record.PagesFailed = result.PagesFailed;
        record.FirstPageError = result.FirstPageError;
        record.Chars = result.Text.Length;
        record.Replacements = result.Text.Count(character => character == '�');
        record.Sha256 = Hash(result.Text);
        record.Sha256Lf = Hash(result.Text.Replace("\r\n", "\n").Replace('\r', '\n'));

        if (textPath != null)
            System.IO.File.WriteAllText(textPath, result.Text, new UTF8Encoding(false));

        return record;
    }

    private static string Hash(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Clip(string message)
    {
        var line = message.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] : line;
    }

    #endregion
}
