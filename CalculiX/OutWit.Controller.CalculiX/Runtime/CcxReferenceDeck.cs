using System.Globalization;
using System.Text;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// The reference deck of the node benchmark, generated rather than shipped: a unit cube of
/// N x N x N nodes meshed with C3D8 bricks, the x = 0 face fixed, every node of the x = 1 face
/// loaded with -1 in z, steel. Same physics as the ref20 deck that used to be embedded
/// (Resources/benchmark.inp keeps that one for the harness and the docs); the size is a
/// constant, so the deck is the same on every machine forever - a rate is comparable only
/// against the same deck, which is why the unit names it.
/// </summary>
/// <remarks>
/// Why 40 and not 20: the 20-cube solves in under a second, where process start and a single
/// core decide the time and a 2-vCPU virtual machine ties a 16-core desktop (2026-09-22:
/// 0.78 s vs 0.67 s). The 40-cube (64 000 nodes, 192 000 equations) takes 8-25 s and lets
/// the cores speak: the same desktop 8.4 s, a 6-core Linux box 10.9 s, the VM 17.8 s.
/// </remarks>
public static class CcxReferenceDeck
{
    #region Constants

    /// <summary>Nodes per edge of the benchmark cube.</summary>
    public const int SIZE = 40;

    /// <summary>The node count of the benchmark deck.</summary>
    public const int NODES = SIZE * SIZE * SIZE;

    /// <summary>The element count of the benchmark deck.</summary>
    public const int ELEMENTS = (SIZE - 1) * (SIZE - 1) * (SIZE - 1);

    #endregion

    #region Functions

    /// <summary>
    /// The benchmark deck text.
    /// </summary>
    /// <returns>A complete CalculiX input deck.</returns>
    public static string Benchmark()
    {
        return Generate(SIZE);
    }

    /// <summary>
    /// A cube deck of the given size (nodes per edge).
    /// </summary>
    /// <param name="size">Nodes per edge, at least 2.</param>
    /// <returns>A complete CalculiX input deck.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size below 2.</exception>
    public static string Generate(int size)
    {
        if (size < 2)
            throw new ArgumentOutOfRangeException(nameof(size), "A cube needs at least two nodes per edge.");

        var culture = CultureInfo.InvariantCulture;
        var step = 1.0 / (size - 1);
        var builder = new StringBuilder(size * size * size * 40 + (size - 1) * (size - 1) * (size - 1) * 60);

        builder.Append("*HEADING\n");
        builder.Append(culture, $"Reference benchmark cube, {size}x{size}x{size} nodes - generated, unit ccx-static@ref{size}\n");
        builder.Append("*NODE, NSET=NALL\n");
        for (var k = 0; k < size; k++)
        for (var j = 0; j < size; j++)
        for (var i = 0; i < size; i++)
            builder.Append(culture, $"{NodeId(size, i, j, k)}, {i * step:F6}, {j * step:F6}, {k * step:F6}\n");

        builder.Append("*ELEMENT, TYPE=C3D8, ELSET=EALL\n");
        var element = 0;
        for (var k = 0; k < size - 1; k++)
        for (var j = 0; j < size - 1; j++)
        for (var i = 0; i < size - 1; i++)
        {
            element++;
            builder.Append(culture,
                $"{element}, {NodeId(size, i, j, k)}, {NodeId(size, i + 1, j, k)}, {NodeId(size, i + 1, j + 1, k)}, {NodeId(size, i, j + 1, k)}, " +
                $"{NodeId(size, i, j, k + 1)}, {NodeId(size, i + 1, j, k + 1)}, {NodeId(size, i + 1, j + 1, k + 1)}, {NodeId(size, i, j + 1, k + 1)}\n");
        }

        // The x = 0 face: every node whose index is 1 modulo the edge length.
        builder.Append(culture, $"*NSET, NSET=FIX, GENERATE\n1, {NodeId(size, 0, size - 1, size - 1)}, {size}\n");
        builder.Append("*MATERIAL, NAME=STEEL\n*ELASTIC\n210000., 0.3\n*DENSITY\n7.85E-9\n*SOLID SECTION, ELSET=EALL, MATERIAL=STEEL\n");
        builder.Append("*BOUNDARY\nFIX, 1, 3, 0.\n*STEP\n*STATIC\n*CLOAD\n");

        // The x = 1 face, one unit of force down on every node.
        for (var k = 0; k < size; k++)
        for (var j = 0; j < size; j++)
            builder.Append(culture, $"{NodeId(size, size - 1, j, k)}, 3, -1.\n");

        builder.Append("*NODE FILE\nU\n*EL FILE\nS\n*END STEP\n");
        return builder.ToString();
    }

    #endregion

    #region Tools

    private static int NodeId(int size, int i, int j, int k)
    {
        return 1 + i + size * j + size * size * k;
    }

    #endregion
}
