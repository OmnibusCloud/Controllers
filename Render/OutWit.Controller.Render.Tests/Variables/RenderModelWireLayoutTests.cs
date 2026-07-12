using System.Reflection;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

/// <summary>
/// Wire-layout guard for the Render.Model MemoryPack types. Most of these ride the DEFAULT (non
/// VersionTolerant) MemoryPack format, whose reader hard-fails on a payload with unknown members —
/// so the ONLY safe evolution is to append a new member at the very end AND deploy the server before
/// any client that reads or writes it. This test freezes the field COUNT of the types the SERVER
/// writes and OLDER readers consume (nodes read RenderTaskData/Options; the plugin reads the
/// preflight/diagnostics types), and checks every type's <c>MemoryPackOrder</c> values stay a
/// contiguous 0..n-1 run. An accidental insert-in-the-middle, reorder, gap, or silent append trips
/// this test — update the expected count here ONLY together with an append-at-the-end change and a
/// server-first deploy.
/// </summary>
[TestFixture]
public sealed class RenderModelWireLayoutTests
{
    // Field counts frozen 2026-07-12 (Render.Model 1.7.0). Bump a count ONLY when appending a member
    // at the end of that type, and only alongside a server-first rollout.
    private static readonly IReadOnlyDictionary<Type, int> ExpectedFieldCounts = new Dictionary<Type, int>
    {
        [typeof(RenderTaskData)] = 13,
        [typeof(RenderTaskBatchData)] = 4,
        [typeof(RenderResultData)] = 10,
        [typeof(RenderResultBatchData)] = 1,
        [typeof(RenderOptionsData)] = 10,
        [typeof(RenderRuntimeDiagnosticsData)] = 13,
        [typeof(RenderPreflightData)] = 6,
        [typeof(RenderPreflightFramesData)] = 4,
        [typeof(RenderPreflightStillTiledData)] = 5,
        [typeof(RenderPreflightVideoData)] = 4,
    };

    [Test]
    public void ServerWrittenTypesKeepTheirFrozenFieldCountTest()
    {
        var mismatches = new List<string>();

        foreach (var (type, expected) in ExpectedFieldCounts)
        {
            var actual = OrderedMembers(type).Count;
            if (actual != expected)
                mismatches.Add($"{type.Name}: expected {expected} MemoryPack members, found {actual}");
        }

        Assert.That(mismatches, Is.Empty,
            "A Render.Model wire type changed its member count. Append new members at the END only, deploy the server before any client that reads/writes them, then update the frozen count here.");
    }

    [Test]
    public void EveryRenderModelTypeHasContiguousMemoryPackOrdersTest()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(RenderTaskData).Assembly.GetTypes())
        {
            if (!IsMemoryPackable(type))
                continue;

            var orders = OrderedMembers(type)
                .Select(GetMemoryPackOrder)
                .Where(order => order.HasValue)
                .Select(order => order!.Value)
                .OrderBy(order => order)
                .ToArray();

            if (orders.Length == 0)
                continue;

            var expected = Enumerable.Range(0, orders.Length).ToArray();
            if (!orders.SequenceEqual(expected))
                offenders.Add($"{type.Name}: orders [{string.Join(", ", orders)}] are not a contiguous 0..{orders.Length - 1} run");
        }

        Assert.That(offenders, Is.Empty,
            "MemoryPackOrder values must be a contiguous 0..n-1 run (no gaps, duplicates, or reorders) so the wire layout stays append-only.");
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
            .FirstOrDefault(me => me.GetType().Name == "MemoryPackOrderAttribute");
        if (attribute is null)
            return null;

        var order = attribute.GetType().GetProperty("Order")?.GetValue(attribute);
        return order is int value ? value : null;
    }
}
