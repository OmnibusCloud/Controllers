using System.Reflection;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Variables;

namespace OutWit.Controller.Sweep.Tests.Variables;

/// <summary>
/// Wire-layout guard for the Sweep controller assembly's MemoryPack wrappers
/// — the script variables and activities that ride the wire next to the
/// CalculiX.Model DTOs (frozen in the CalculiX test suite). The wrappers
/// carry no explicit <c>MemoryPackOrder</c>: their layout IS the declaration
/// order, so this test freezes each type's declared serialized property
/// names in metadata order. An accidental insert, reorder, rename or a brand
/// new wire type trips it. Change the frozen table ONLY together with an
/// append-at-the-end change and a server-first rollout.
/// </summary>
[TestFixture]
public sealed class SweepWrapperWireLayoutTests
{
    // Declared layouts frozen 2026-08-13. The variable wrappers deliberately
    // declare NOTHING — name and payload serialize through the engine base
    // class; a property declared here would silently extend the wire.
    private static readonly IReadOnlyDictionary<Type, string[]> EXPECTED_LAYOUTS = new Dictionary<Type, string[]>
    {
        [typeof(WitVariableSweepPlan)] = [],
        [typeof(WitVariableSweepState)] = [],
        [typeof(WitVariableSweepOptions)] = [],
        [typeof(WitActivitySweepPlan)] = ["BaseDeck", "Options"],
        [typeof(WitActivitySweepInitState)] = ["Plan"],
        [typeof(WitActivitySweepChunkCount)] = ["Plan"],
        [typeof(WitActivitySweepMakeChunk)] = ["Plan", "State"],
        [typeof(WitActivitySweepHarvest)] = ["Plan", "State", "Wave"],
        [typeof(WitActivitySweepFinish)] = ["Plan", "State"]
    };

    #region Layout Tests

    [Test]
    public void EveryWrapperTypeIsFrozenTest()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(WitActivitySweepPlan).Assembly.GetTypes())
        {
            if (!IsMemoryPackable(type))
                continue;

            if (!EXPECTED_LAYOUTS.TryGetValue(type, out var frozen))
            {
                offenders.Add($"{type.Name}: a new wire type must be added to the frozen layout table");
                continue;
            }

            var declared = DeclaredSerializedProperties(type);
            if (!declared.SequenceEqual(frozen, StringComparer.Ordinal))
                offenders.Add(
                    $"{type.Name}: declared wire members [{string.Join(", ", declared)}] " +
                    $"differ from the frozen [{string.Join(", ", frozen)}]");
        }

        Assert.That(offenders, Is.Empty,
            "A Sweep wire wrapper changed its declared layout. Append new members at the END only, " +
            "deploy the server before any client that writes them, then update the frozen table here.");
    }

    #endregion

    #region Tools

    private static bool IsMemoryPackable(Type type)
    {
        return type.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute");
    }

    private static List<string> DeclaredSerializedProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.GetMethod != null && property.SetMethod != null)
            .Where(property => property.GetCustomAttributes(inherit: false)
                .All(attribute => attribute.GetType().Name != "MemoryPackIgnoreAttribute"))
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToList();
    }

    #endregion
}
