using System.Reflection;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// A rule of this test assembly, held by looking at the assembly itself: no type of it names a
/// type of the controller or of the wasmtime binding in its shape. A test framework loads every
/// type of a test assembly when it looks for tests: each with its base type and its interfaces,
/// with the value types its fields hold (the class the compiler writes for a closure or for an
/// iterator is a type like any other), with the types its attributes name, and a test's method
/// with the types of its signature. A controller type in any of those places loads the
/// controller's assembly into the default load context before the first test runs, and a proof
/// that the controller's module loads in a context of its own (as a node loads it) cannot be
/// made in such a process any more. Inside a method body a controller type costs nothing until
/// the method runs.
/// <para>
/// The test loads a second copy of this assembly into a load context of its own
/// (<see cref="SmtWatchingLoadContext"/>), which notes when the controller or the binding is
/// asked for, looks at every type as a framework does, and holds that nothing asked.
/// </para>
/// </summary>
[TestFixture]
public sealed class SmtTestShapeTests
{
    #region Constants

    private const string CONTROLLER = "OutWit.Controller.AI.Smt";
    private const string BINDING = "Wasmtime.Dotnet";

    private const BindingFlags EVERY_METHOD = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    #endregion

    #region Tests

    [Test]
    public void NoTypeOfTheTestsNamesTheControllerOrTheBindingInItsShapeTest()
    {
        var assembly = typeof(SmtTestShapeTests).Assembly;
        var names = assembly.GetTypes().Select(type => type.FullName).OfType<string>().ToList();
        var context = new SmtWatchingLoadContext(Path.GetDirectoryName(assembly.Location)!, [CONTROLLER, BINDING]);

        var named = new List<string>();
        try
        {
            var copy = context.LoadFromAssemblyPath(assembly.Location);
            foreach (var name in names)
            {
                var before = context.Asked;
                Look(copy.GetType(name, throwOnError: true)!);
                if (context.Asked > before)
                    named.Add($"{name} ({string.Join(", ", context.Names.Skip(before))})");
            }
        }
        finally
        {
            context.Unload();
        }

        TestContext.Out.WriteLine($"{names.Count} types of the test assembly looked at");
        Assert.Multiple(() =>
        {
            Assert.That(names, Has.Count.GreaterThan(100));
            Assert.That(named, Is.Empty,
                "a type that names the controller or the binding in its shape (a base type, an interface, a field of a value type, an attribute, a test's signature); " +
                "once an assembly is loaded it is not asked for again, so the first type named here is one of perhaps several");
        });
    }

    #endregion

    #region Tools

    /// <summary>Looks at a type as a test framework does when it looks for tests: the type itself, its attributes, and the methods it could run.</summary>
    private static void Look(Type type)
    {
        _ = type.BaseType;
        _ = type.GetInterfaces();
        _ = type.GetCustomAttributesData();

        foreach (var method in type.GetMethods(EVERY_METHOD))
        {
            // A method with an attribute may be a test, a source of cases, a set-up: its signature is read.
            if (method.GetCustomAttributesData().Count == 0)
                continue;

            _ = method.ReturnType;
            _ = method.GetParameters().Select(parameter => parameter.ParameterType).ToList();
        }
    }

    #endregion
}
