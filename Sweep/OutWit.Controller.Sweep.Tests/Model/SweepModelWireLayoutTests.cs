using System.Reflection;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Tests.Model;

/// <summary>
/// Wire-layout guard for the Sweep.Model MemoryPack types. They ride the
/// DEFAULT (non version-tolerant) MemoryPack format, whose reader hard-fails
/// on a payload with unknown members, so the only safe evolution is to append
/// a member at the very end and deploy the server before any client that
/// writes it. This test freezes every type's member count and checks the
/// <c>MemoryPackOrder</c> values stay a contiguous 0..n-1 run - an
/// accidental insert, reorder, gap or silent append trips it. The family
/// types that ride inside them (the node results in every manifest row, the
/// case and the extraction request in every study) are part of the same
/// layouts, so they are frozen here too.
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

    // The family types the Sweep.Model types carry, frozen 2026-09-25
    // (CalculiX.Model 2.0.0, OpenFOAM.Model 1.0.x). A member appended to one
    // of them changes the manifest's layout (the results) or the study's (the
    // case, the extraction request): bump the count here only together with
    // the family model's own frozen count, every manifest reader updated
    // before a host writes the new member.
    private static readonly IReadOnlyDictionary<Type, int> EMBEDDED_FIELD_COUNTS = new Dictionary<Type, int>
    {
        [typeof(CcxExtractionRequestData)] = 1,
        [typeof(CcxProbeData)] = 3,
        [typeof(CcxResponseRowData)] = 1,
        [typeof(CcxResponseValueData)] = 2,
        [typeof(CcxResultData)] = 7,
        [typeof(FoamArtifactPolicyData)] = 4,
        [typeof(FoamCaseData)] = 8,
        [typeof(FoamExtractionRequestData)] = 1,
        [typeof(FoamFileRefData)] = 5,
        [typeof(FoamNamedValueData)] = 2,
        [typeof(FoamRecipeData)] = 3,
        [typeof(FoamResponseRowData)] = 1,
        [typeof(FoamResponseSpecData)] = 6,
        [typeof(FoamResponseValueData)] = 2,
        [typeof(FoamResultData)] = 17,
        [typeof(FoamStepData)] = 3,
        [typeof(FoamStepOutcomeData)] = 4
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
    public void FamilyTypesRidingInTheSweepKeepTheirFrozenFieldCountTest()
    {
        var mismatches = new List<string>();

        foreach (var (type, expected) in EMBEDDED_FIELD_COUNTS)
        {
            var actual = OrderedMembers(type).Count;
            if (actual != expected)
                mismatches.Add($"{type.Name}: expected {expected} MemoryPack members, found {actual}");
        }

        Assert.That(mismatches, Is.Empty,
            "A family type that rides inside the sweep's manifest or study changed its member count. " +
            "Every manifest reader must be updated before a host writes the new member; then update the frozen count here.");
    }

    [Test]
    public void EveryFamilyTypeRidingInTheSweepIsFrozenTest()
    {
        var reachable = new HashSet<Type>();
        foreach (var type in EXPECTED_FIELD_COUNTS.Keys)
            CollectEmbedded(type, reachable);

        Assert.That(reachable.Select(type => type.Name).Order(), Is.EqualTo(EMBEDDED_FIELD_COUNTS.Keys.Select(type => type.Name).Order()),
            "Every family type a Sweep.Model type carries, and no other, is in the frozen table of embedded types.");
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

    /// <summary>
    /// Every MemoryPack type from outside Sweep.Model that the members of
    /// <paramref name="type"/> carry, directly or through lists, nullables
    /// and the types they carry in turn.
    /// </summary>
    private static void CollectEmbedded(Type type, HashSet<Type> found)
    {
        foreach (var property in OrderedMembers(type))
        {
            foreach (var carried in CarriedTypes(property.PropertyType))
            {
                if (!IsMemoryPackable(carried))
                    continue;

                var isFamilyType = carried.Assembly != typeof(SweepOptionsData).Assembly;
                if (isFamilyType && !found.Add(carried))
                    continue;

                CollectEmbedded(carried, found);
            }
        }
    }

    private static IEnumerable<Type> CarriedTypes(Type type)
    {
        yield return Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsArray && type.GetElementType() is { } element)
            yield return element;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                yield return Nullable.GetUnderlyingType(argument) ?? argument;
        }
    }

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
