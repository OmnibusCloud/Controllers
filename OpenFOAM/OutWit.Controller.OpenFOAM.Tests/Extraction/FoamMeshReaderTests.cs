using System.IO.Compression;
using System.Text;
using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

/// <summary>
/// The size of the mesh a run ended with, read from the note OpenFOAM writes
/// into the header of every <c>owner</c> it writes: the case's own mesh, or
/// the processor meshes summed when the solve ran decomposed - a case meshed
/// on its decomposed form keeps the background mesh in its root.
/// </summary>
[TestFixture]
public class FoamMeshReaderTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-mesh");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private static string Header(long cells, string format = "binary")
    {
        return "FoamFile\n{\n    version     2.0;\n    format      " + format + ";\n    class       labelList;\n" +
               $"    note        \"nPoints:2002  nCells:{cells}  nFaces:3700  nInternalFaces:1700\";\n" +
               "    object      owner;\n}\n// * * * //\n\n";
    }

    private void WriteOwner(string relativeDirectory, long cells)
    {
        var directory = Path.Combine(m_case, relativeDirectory);
        Directory.CreateDirectory(directory);

        // A binary body after the header, as a binary-format mesh has it.
        var bytes = Encoding.ASCII.GetBytes(Header(cells) + "3700\n(").Concat(new byte[] { 0, 0, 0, 0, 0xFF, 0xFE, 0x0A, 0x7D }).ToArray();
        File.WriteAllBytes(Path.Combine(directory, "owner"), bytes);
    }

    #endregion

    #region Serial Tests

    [Test]
    public void TheCasesMeshIsReadFromItsOwnerHeaderTest()
    {
        WriteOwner("constant/polyMesh", 185237);

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: false), Is.EqualTo(185237));
    }

    [Test]
    public void AMeshWrittenAtALaterTimeIsTheOneTheRunEndedWithTest()
    {
        WriteOwner("constant/polyMesh", 5000);
        WriteOwner("0.5/polyMesh", 7200);
        WriteOwner("2/polyMesh", 9100);
        Directory.CreateDirectory(Path.Combine(m_case, "10"));

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: false), Is.EqualTo(9100), "the latest time that carries a mesh");
    }

    [Test]
    public void ACompressedOwnerIsReadTest()
    {
        var directory = Path.Combine(m_case, "constant", "polyMesh");
        Directory.CreateDirectory(directory);
        using (var file = File.Create(Path.Combine(directory, "owner.gz")))
        using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            zip.Write(Encoding.ASCII.GetBytes(Header(43066, "ascii") + "3700\n(\n0\n0\n)\n"));

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: false), Is.EqualTo(43066));
    }

    [Test]
    public void AnOwnerWithoutTheNoteOrNoMeshIsUnknownTest()
    {
        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: false), Is.EqualTo(0), "no mesh");

        var directory = Path.Combine(m_case, "constant", "polyMesh");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "owner"), "FoamFile\n{\n    format ascii;\n    class labelList;\n    object owner;\n}\n43066\n(\n0\n)\n");

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: false), Is.EqualTo(0), "a mesh written without the note, as older tools shipped some");
    }

    #endregion

    #region Decomposed Tests

    [Test]
    public void ADecomposedSolveCountsTheProcessorMeshesNotTheBackgroundTest()
    {
        WriteOwner("constant/polyMesh", 5000);
        WriteOwner("processor0/constant/polyMesh", 92000);
        WriteOwner("processor1/constant/polyMesh", 93237);

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: true), Is.EqualTo(185237));
        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: false), Is.EqualTo(5000));
    }

    [Test]
    public void AProcessorWithoutAReadableMeshMakesTheSumUnknownTest()
    {
        WriteOwner("constant/polyMesh", 5000);
        WriteOwner("processor0/constant/polyMesh", 92000);
        Directory.CreateDirectory(Path.Combine(m_case, "processor1", "constant", "polyMesh"));

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: true), Is.EqualTo(0), "half a sum is not the mesh; the root's is the background's");
    }

    [Test]
    public void ADecomposedSolveWithoutProcessorDirectoriesIsUnknownTest()
    {
        WriteOwner("constant/polyMesh", 5000);
        Directory.CreateDirectory(Path.Combine(m_case, "processors2"));

        Assert.That(FoamMeshReader.CellCount(m_case, decomposed: true), Is.EqualTo(0), "the collated layout is not read");
    }

    #endregion
}
