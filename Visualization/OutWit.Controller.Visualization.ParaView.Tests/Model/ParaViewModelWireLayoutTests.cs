using System.Reflection;
using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Tests.Model;

/// <summary>
/// Wire-layout guard for the ParaView.Model MemoryPack types. Every type rides the VersionTolerant
/// format with an explicit MemoryPackOrder on each member, so the only safe evolution is appending
/// at the end. This freezes the member COUNT of every wire type and checks the orders stay a
/// contiguous 0..n-1 run; an insert-in-the-middle, reorder, gap, or silent append trips it —
/// update a count here ONLY together with an append-at-the-end change.
/// </summary>
[TestFixture]
public sealed class ParaViewModelWireLayoutTests
{
    // Field counts frozen 2026-08-19 (ParaView.Model 0.1.0).
    private static readonly IReadOnlyDictionary<Type, int> ExpectedFieldCounts = new Dictionary<Type, int>
    {
        [typeof(ParaViewSceneRefData)] = 7,
        [typeof(ParaViewAttachmentRefData)] = 8,
        [typeof(ParaViewRuntimeRequirementData)] = 6,
        [typeof(ParaViewPluginRequirementData)] = 2,
        [typeof(ParaViewOutputOptionsData)] = 6,
        [typeof(ParaViewFrameSelectionData)] = 5,
        [typeof(ParaViewRenderTaskData)] = 14,
        [typeof(ParaViewRenderResultData)] = 14,
        [typeof(ParaViewValidationReportData)] = 17,
    };

    [Test]
    public void WireTypesKeepTheirFrozenFieldCountTest()
    {
        var mismatches = new List<string>();

        foreach (var (type, expected) in ExpectedFieldCounts)
        {
            var actual = OrderedMembers(type).Count;
            if (actual != expected)
                mismatches.Add($"{type.Name}: expected {expected} MemoryPack members, found {actual}");
        }

        Assert.That(mismatches, Is.Empty,
            "A ParaView.Model wire type changed its member count. Append new members at the END only, then update the frozen count here.");
    }

    [Test]
    public void EveryModelTypeIsCoveredByTheFrozenCountsTest()
    {
        var uncovered = typeof(ParaViewSceneRefData).Assembly.GetTypes()
            .Where(IsMemoryPackable)
            .Where(type => !ExpectedFieldCounts.ContainsKey(type))
            .Select(type => type.Name)
            .ToList();

        Assert.That(uncovered, Is.Empty, "Every MemoryPackable model type must have a frozen member count.");
    }

    [Test]
    public void EveryModelTypeHasContiguousMemoryPackOrdersTest()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(ParaViewSceneRefData).Assembly.GetTypes().Where(IsMemoryPackable))
        {
            var orders = OrderedMembers(type)
                .Select(GetMemoryPackOrder)
                .Where(order => order.HasValue)
                .Select(order => order!.Value)
                .OrderBy(order => order)
                .ToArray();

            var expected = Enumerable.Range(0, orders.Length).ToArray();
            if (orders.Length == 0 || !orders.SequenceEqual(expected))
                offenders.Add($"{type.Name}: orders [{string.Join(", ", orders)}] are not a contiguous 0..{Math.Max(0, orders.Length - 1)} run");
        }

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void EveryModelTypeIsVersionTolerantTest()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(ParaViewSceneRefData).Assembly.GetTypes().Where(IsMemoryPackable))
        {
            var attribute = type.GetCustomAttributes(inherit: false).First(me => me.GetType().Name == "MemoryPackableAttribute");
            var generateType = attribute.GetType().GetProperty("GenerateType")?.GetValue(attribute)?.ToString();
            if (generateType != "VersionTolerant")
                offenders.Add($"{type.Name}: GenerateType is {generateType ?? "(default)"}");
        }

        Assert.That(offenders, Is.Empty, "Every ParaView wire type must be VersionTolerant (plugin↔server skew degrades, never rejects).");
    }

    private static bool IsMemoryPackable(Type type)
    {
        return type.IsClass && type.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute");
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
            .FirstOrDefault(me => me.GetType().Name == "MemoryPackOrderAttribute");
        if (attribute is null)
            return null;

        var order = attribute.GetType().GetProperty("Order")?.GetValue(attribute);
        return order is int value ? value : null;
    }
}
