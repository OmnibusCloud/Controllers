using System.Reflection;
using OutWit.Controller.CalculiX.Activities;
using OutWit.Controller.CalculiX.Variables;

namespace OutWit.Controller.CalculiX.Tests.Variables;

/// <summary>
/// Wire-layout guard for the controller assembly's MemoryPack wrappers — the
/// script variables and activities that ride the wire NEXT TO the Model DTOs
/// (those are frozen by <c>CalculiXModelWireLayoutTests</c>). The wrappers
/// carry no explicit <c>MemoryPackOrder</c>: their layout IS the declaration
/// order, so this test freezes each type's declared serialized property
/// names in metadata order. An accidental insert, reorder, rename or a brand
/// new wire type trips it. Change the frozen table ONLY together with an
/// append-at-the-end change and a server-first rollout.
/// </summary>
[TestFixture]
public sealed class CalculiXWrapperWireLayoutTests
{
    // Declared layouts frozen 2026-08-13. The variable wrappers deliberately
    // declare NOTHING — name and payload serialize through the engine base
    // class; a property declared here would silently extend the wire.
    private static readonly IReadOnlyDictionary<Type, string[]> EXPECTED_LAYOUTS = new Dictionary<Type, string[]>
    {
        [typeof(WitVariableCcxTask)] = [],
        [typeof(WitVariableCcxTaskCollection)] = [],
        [typeof(WitVariableCcxResult)] = [],
        [typeof(WitVariableCcxResultCollection)] = [],
        [typeof(WitActivityCcxSolve)] = ["Task"]
    };

    #region Layout Tests

    [Test]
    public void EveryWrapperTypeIsFrozenTest()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(WitActivityCcxSolve).Assembly.GetTypes())
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
            "A controller wire wrapper changed its declared layout. Append new members at the END only, " +
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
