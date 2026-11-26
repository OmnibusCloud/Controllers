using System.Collections.Frozen;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The fixed lists of the language layer are fixed for the whole process: what the sanitizer
/// refuses by name and the codes it refuses with are handed out, under their published type, as
/// frozen sets that no caller can change, by a cast or otherwise. A caller that could add a word
/// to them would change what every reader of the process refuses.
/// </summary>
[TestFixture]
public sealed class SmtReservedWordsTests
{
    #region Set Tests

    [Test]
    public void TheReservedWordsCannotBeChangedTest()
    {
        // One assertion after the other: a set that can be changed is not changed here.
        Assert.That(SmtReservedWords.General, Is.InstanceOf<FrozenSet<string>>(), "the general reserved words");
        Assert.That(SmtReservedWords.Commands, Is.InstanceOf<FrozenSet<string>>(), "the command names");
        Assert.That(SmtReservedWords.Commands, Is.Not.InstanceOf<HashSet<string>>(), "a cast back to a set that can be changed fails");
        Assert.Throws<NotSupportedException>(() => ((ISet<string>)SmtReservedWords.General).Add("reset-everything"));
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)SmtReservedWords.Commands).Add("reset-everything"));
        Assert.That(SmtReservedWords.IsCommand("reset-everything"), Is.False);
    }

    [Test]
    public void TheRefusalCodesCannotBeChangedTest()
    {
        Assert.That(SmtRefusalCodes.All, Is.InstanceOf<FrozenSet<string>>());
        Assert.That(SmtRefusalCodes.All, Is.Not.InstanceOf<HashSet<string>>(), "a cast back to a set that can be changed fails");
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)SmtRefusalCodes.All).Add("anything"));
        Assert.That(SmtRefusalCodes.All, Does.Not.Contain("anything"));
    }

    #endregion
}
