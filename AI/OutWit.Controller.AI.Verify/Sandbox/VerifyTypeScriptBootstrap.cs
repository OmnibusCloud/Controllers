using System.Text.Json;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The script a TypeScript task runs as: QuickJS loads sucrase first (<c>-I /sucrase.js</c>),
/// then this module strips the types of the task's source, held in it as a JSON string, and
/// evaluates the JavaScript as a module, so <c>import</c> of <c>qjs:std</c>, top-level
/// <c>await</c>, <c>scriptArgs</c>, stdin and the exit code behave as in a JavaScript task. A
/// source that does not parse is reported as a SyntaxError with its line and column; an error
/// the program does not catch is printed as QuickJS prints one, and the exit code is 1.
/// </summary>
internal static class VerifyTypeScriptBootstrap
{
    #region Constants

    private const string TEMPLATE =
        "import * as std from \"qjs:std\";\n" +
        "let code;\n" +
        "try {\n" +
        "  code = sucrase.transform(%SOURCE%, { transforms: [\"typescript\"], filePath: %NAME% }).code;\n" +
        "} catch (e) {\n" +
        "  std.err.puts(\"SyntaxError: \" + e.message + \"\\n\");\n" +
        "  std.exit(1);\n" +
        "}\n" +
        "std.evalScript(code, { compile_module: true }).catch((e) => {\n" +
        "  std.err.puts(e instanceof Error ? e + \"\\n\" + (e.stack || \"\") : \"Uncaught \" + e + \"\\n\");\n" +
        "  std.exit(1);\n" +
        "});\n";

    #endregion

    #region Functions

    /// <summary>
    /// The bootstrap for one task's source.
    /// </summary>
    /// <param name="source">The TypeScript source.</param>
    /// <param name="fileName">The source's name, as sucrase's messages show it.</param>
    /// <returns>The bootstrap module's text.</returns>
    public static string Build(string source, string fileName)
    {
        // JSON string literals are JavaScript string literals; the default encoder escapes every
        // character outside ASCII, so the text is the same on every node.
        return TEMPLATE
            .Replace("%NAME%", JsonSerializer.Serialize(fileName), StringComparison.Ordinal)
            .Replace("%SOURCE%", JsonSerializer.Serialize(source), StringComparison.Ordinal);
    }

    #endregion
}
