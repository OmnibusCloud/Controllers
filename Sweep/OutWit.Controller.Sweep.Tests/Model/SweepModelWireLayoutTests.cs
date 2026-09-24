using System.Reflection;
using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Tests.Model;

/// <summary>
/// Wire-layout guard for the Sweep.Model MemoryPack types. They ride the
/// DEFAULT (non version-tolerant) MemoryPack format, whose reader hard-fails
/// on a payload with unknown members, so the only safe evolution is to append
/// a member at the very end and deploy the server before any client that
/// writes it. This test freezes every type's member count and checks the
/// <c>MemoryPackOrder</c> values stay a contiguous 0..n-1 run - an
/// accidental insert, reorder, gap or silent append trips it.
/// </summary>
[TestFixture]
public sealed class SweepModelWireLayoutTests
{
    // Field counts frozen 2026-09-24 (Sweep.Model 2.0.0, the family-neutral
    // sweep). Bump a count ONLY when appending at the end of that type.
    private static readonly IReadOnlyDictionary<Type, int> EXPECTED_FIELD_COUNTS = new Dictionary<Type, int>
    {
        [typeof(SweepArtifactData)] = 3,
        [typeof(SweepCalculiXDeckData)] = 4,
        [typeof(SweepCalculiXStudyData)] = 6,
        [typeof(SweepManifestData)] = 1,
        [typeof(SweepManifestRowData)] = 4,
        [typeof(SweepOptionsData)] = 6,
        [typeof(SweepParameterData)] = 2,
        [typeof(SweepPlanData)] = 2,
        [typeof(SweepResultIndexEntryData)] = 4,
        [typeof(SweepStateData)] = 7,
        [typeof(SweepVariantData)] = 2
    };

    #region Layout Tests

    [Test]
    public void WireTypesKeepTheirFrozenFieldCountTest()
    {
        var mismatches = new List<string>();

        foreach (var (type, expected) in EXPECTED_FIELD_COUNTS)
        {
            var actual = OrderedMembers(type).Count;
            if (actual != expected)
                mismatches.Add($"{type.Name}: expected {expected} MemoryPack members, found {actual}");
        }

        Assert.That(mismatches, Is.Empty,
            "A Sweep.Model wire type changed its member count. Append new members at the END only, " +
            "deploy the server before any client that writes them, then update the frozen count here.");
    }

    [Test]
    public void EveryModelTypeIsFrozenAndHasContiguousMemoryPackOrdersTest()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(SweepOptionsData).Assembly.GetTypes())
        {
            if (!IsMemoryPackable(type))
                continue;

            if (!EXPECTED_FIELD_COUNTS.ContainsKey(type))
            {
                offenders.Add($"{type.Name}: a new wire type must be added to the frozen-count table");
                continue;
            }

            var orders = OrderedMembers(type)
                .Select(GetMemoryPackOrder)
                .Where(order => order.HasValue)
                .Select(order => order ?? -1)
                .OrderBy(order => order)
                .ToArray();

            if (!orders.SequenceEqual(Enumerable.Range(0, orders.Length)))
                offenders.Add($"{type.Name}: orders [{string.Join(", ", orders)}] are not a contiguous 0..{orders.Length - 1} run");
        }

        Assert.That(offenders, Is.Empty,
            "MemoryPackOrder values must be a contiguous 0..n-1 run (no gaps, duplicates, or reorders) " +
            "so the wire layout stays append-only.");
    }

    [Test]
    public void TheEnumsKeepTheirValuesTest()
    {
        // Enum members travel as numbers on the MemoryPack wire and as names
        // in the documents: neither a renumbering nor a rename is harmless.
        Assert.That(Enum.GetValues<SweepFamily>().Select(value => ((int)value, value.ToString())),
            Is.EqualTo(new[] { (0, "CalculiX"), (1, "OpenFOAM") }));
        Assert.That(Enum.GetValues<SweepOutcome>().Select(value => ((int)value, value.ToString())),
            Is.EqualTo(new[] { (0, "Succeeded"), (1, "Failed"), (2, "Refused") }));
        Assert.That(Enum.GetValues<SweepArtifactKind>().Select(value => ((int)value, value.ToString())),
            Is.EqualTo(new[] { (0, "CalculiXFrd"), (1, "CalculiXDat"), (2, "OpenFOAMCase") }));
    }

    #endregion

    #region Tools

    private static bool IsMemoryPackable(Type type)
    {
        return type.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute");
    }

    private static IReadOnlyList<PropertyInfo> OrderedMembers(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => GetMemoryPackOrder(property).HasValue)
            .ToList();
    }

    private static int? GetMemoryPackOrder(PropertyInfo property)
    {
        var attribute = property.GetCustomAttributes(inherit: false)
            .FirstOrDefault(candidate => candidate.GetType().Name == "MemoryPackOrderAttribute");
        if (attribute is null)
            return null;

        var order = attribute.GetType().GetProperty("Order")?.GetValue(attribute);
        return order is int value ? value : null;
    }

    #endregion
}
