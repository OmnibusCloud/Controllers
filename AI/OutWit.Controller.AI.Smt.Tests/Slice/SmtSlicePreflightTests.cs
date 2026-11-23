using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Slice;

/// <summary>
/// The preflight of a run on real problems: the 469 problems of the slice (<c>Slice/fetch-list.tsv</c>)
/// as one problem set, each a check row with its file's text, read as a run reads it. Every one is
/// taken; the logics and the expected verdicts are the list's; every file's own check-sat is
/// stripped; the upper bound is the problems times the default hard cap; the set is cut into the
/// packages the planner's rule gives.
/// </summary>
[TestFixture]
[Category("Slice")]
public sealed class SmtSlicePreflightTests
{
    #region Tests

    [Test]
    public void ThePreflightOfTheSliceSaysWhatTheListSaysTest()
    {
        var listing = SmtSlice.Listing();
        var folder = SmtSlice.RequireFolder();

        var path = Path.Combine(Path.GetTempPath(), $"witai_smt_slice_set_{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var set = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var (id, _, sha256, _) in listing)
                {
                    var text = SmtSlice.Read(folder, id, sha256) ?? throw new AssertionException($"{id} is not in the slice's folder");
                    set.Write(JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = id, ["smt2"] = text }));
                    set.Write('\n');
                }
            }

            var preflight = SmtRunRig.Preflight(path, null);
            TestContext.Out.WriteLine($"{preflight.Accepted} of {preflight.Rows} taken, {preflight.BytesRead:N0} bytes; {preflight.Packages} packages of at most {preflight.ProblemsPerPackage}; " +
                                      $"upper bound {preflight.UpperBoundSeconds:N0} s; logics {string.Join(", ", preflight.Logics.Select(count => $"{count.Name} {count.Count}"))}");

            Assert.Multiple(() =>
            {
                Assert.That(preflight.Runnable, Is.True);
                Assert.That(preflight.Rows, Is.EqualTo(SmtSlice.PROBLEMS));
                Assert.That(preflight.Accepted, Is.EqualTo(SmtSlice.PROBLEMS), "every problem of the slice is taken");
                Assert.That(preflight.Refused, Is.Zero);
                Assert.That(preflight.Logics.ToDictionary(count => count.Name, count => count.Count),
                    Is.EquivalentTo(listing.GroupBy(row => row.Logic).ToDictionary(group => group.Key, group => group.Count())), "the logics of the list");
                Assert.That(preflight.Expected.Single(count => count.Name == "sat").Count, Is.EqualTo(listing.Count(row => row.Status == SmtProblemStatus.Sat)));
                Assert.That(preflight.Expected.Single(count => count.Name == "unsat").Count, Is.EqualTo(listing.Count(row => row.Status == SmtProblemStatus.Unsat)));
                Assert.That(preflight.Stripped.Single(count => count.Name == "check-sat").Count, Is.GreaterThanOrEqualTo(SmtSlice.PROBLEMS), "every file's own check");
                Assert.That(preflight.Licenses.Sum(count => count.Count) + preflight.OtherLicenses, Is.EqualTo(SmtSlice.PROBLEMS));
                Assert.That(preflight.UpperBoundSeconds, Is.EqualTo(SmtSlice.PROBLEMS * 60L));
                Assert.That(preflight.Packages, Is.GreaterThanOrEqualTo((SmtSlice.PROBLEMS + preflight.ProblemsPerPackage - 1) / preflight.ProblemsPerPackage));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion
}
