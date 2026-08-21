using System.Reflection;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Tests.Model;

/// <summary>
/// Wire-layout guard for the CalculiX.Model MemoryPack types — the same
/// discipline as Render's: these ride the DEFAULT (non VersionTolerant)
/// MemoryPack format, whose reader hard-fails on a payload with unknown
/// members, so the ONLY safe evolution is to append a new member at the
/// very end AND deploy the server before any client that writes it. This
/// test freezes every type's member count and checks the
/// <c>MemoryPackOrder</c> values stay a contiguous 0..n-1 run — an
/// accidental insert, reorder, gap or silent append trips it. Update a
/// frozen count ONLY together with an append-at-the-end change and a
/// server-first rollout.
/// </summary>
[TestFixture]
public sealed class CalculiXModelWireLayoutTests
{
    // Field counts frozen 2026-08-21 (CalculiX.Model 0.2.0 — the result-index
    // append to SweepStateData for the sweep.state@1 document vocabulary).
    // Bump a count ONLY when appending at the end of that type, and only
    // alongside a server-first rollout.
    private static readonly IReadOnlyDictionary<Type, int> EXPECTED_FIELD_COUNTS = new Dictionary<Type, int>
    {
        [typeof(CcxExtractionRequestData)] = 1,
        [typeof(CcxProbeData)] = 3,
        [typeof(CcxResponseRowData)] = 1,
        [typeof(CcxResponseValueData)] = 2,
        [typeof(CcxResultData)] = 7,
        [typeof(CcxTaskData)] = 6,
        [typeof(SweepManifestData)] = 1,
        [typeof(SweepManifestRowData)] = 8,
        [typeof(SweepOptionsData)] = 8,
        [typeof(SweepParameterData)] = 2,
        [typeof(SweepPlanData)] = 3,
        [typeof(SweepResultIndexEntryData)] = 3,
        [typeof(SweepStateData)] = 6,
        [typeof(SweepVariantData)] = 5
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
            "A CalculiX.Model wire type changed its member count. Append new members at the END only, " +
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
                .Select(order => order!.Value)
                .OrderBy(order => order)
                .ToArray();

            var expected = Enumerable.Range(0, orders.Length).ToArray();
            if (!orders.SequenceEqual(expected))
                offenders.Add(
                    $"{type.Name}: orders [{string.Join(", ", orders)}] are not a contiguous 0..{orders.Length - 1} run");
        }

        Assert.That(offenders, Is.Empty,
            "MemoryPackOrder values must be a contiguous 0..n-1 run (no gaps, duplicates, or reorders) " +
            "so the wire layout stays append-only.");
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
