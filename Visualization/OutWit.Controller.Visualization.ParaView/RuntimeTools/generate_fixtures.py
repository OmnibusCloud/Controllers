"""Generates the ParaView fixture corpus (docs 03, sections 8.3 and 16.3) with the PINNED runtime.

Run with the bundled pvpython of the runtime under test:

    pvpython generate_fixtures.py --out <corpus-dir> [--plugin <omnibuscloud_frd_reader.py> --frd <dir-of-frd-files>]

Writes small data files and REAL states saved by this ParaView (SaveState), then rewrites the
absolute data paths inside each state to the package's logical paths (what the GUI plugin does
when it builds a package). The corpus is the input of generate_allowlist.py and of the
controller's golden validation tests:

    <corpus-dir>/
      manifest.json                       # what was generated, with which ParaView
      data/wavelet.vti                    # uniform grid field
      data/tets.vtu                       # unstructured grid (tetrahedralized wavelet)
      data/grid.vtr                       # rectilinear grid
      data/series/series.pvd + series_00N.vti   # 5-step time series behind a PVD index
      states/vti_contour.pvsm             # XMLImageDataReader + Contour
      states/vti_volume.pvsm              # volume representation
      states/vtu_slice_clip_glyph.pvsm    # XMLUnstructuredGridReader + Cut + Clip + Gradient + Glyph
      states/vtr_surface.pvsm             # XMLRectilinearGridReader surface
      states/pvd_series.pvsm              # PVDReader time series (index file + per-step pieces)
      states/file_series.pvsm             # XMLImageDataReader over FileNames (file-series reader)
      states/sphere_static.pvsm           # no files at all (static, no attachments)
      data/legacy/surface.vtk             # legacy-format polydata (what foamToVTK and postProcessing surfaces write)
      states/legacy_vtk.pvsm              # LegacyVTKReader surface coloured by elevation
      data/polydata/surface.vtp           # polydata surface (a superquadric torus with an elevation field)
      data/structured/grid.vts            # curvilinear structured grid (a quarter annulus)
      data/multiblock/blocks.vtm + blocks/  # multiblock index over a solid (.vtu) and a shell (.vtp) block
      data/parallel/tets.pvtu + tets_N.vtu  # parallel unstructured index over two pieces
      data/parallel/shell.pvtp + shell_N.vtp  # parallel polydata index over two pieces
      data/geometry/part.stl|ply|obj      # plain geometry: binary STL, binary PLY, Wavefront OBJ
      states/vtp_surface.pvsm             # XMLPolyDataReader + Clean, Triangulate, Decimate, Smooth, normals, Elevation, edges, Curvature
      states/vts_structured.pvsm          # XMLStructuredGridReader + Extract Subset, Warp By Scalar, Outline, Shrink, point-to-cell
      states/vtm_blocks.pvsm              # XMLMultiBlockDataReader + Extract Block, Merge Blocks, Group / Append Datasets
      states/parallel_pieces.pvsm         # the parallel unstructured and polydata readers over their pieces
      states/geometry_formats.pvsm        # STL + Transform, PLY + Reflect, OBJ
      states/vtu_more_filters.pvsm        # tets.vtu + cells by region, centers, mask, tessellate, resample, custom-seed streamlines, annotations, ruler
      data/frd/*.frd                     # (with --frd) CalculiX results: the CalculiX.Tests fixtures +
                                          #   generate_frd_fixtures.py output (every cgx element type)
      states/OmnibusCloudFrdReader/       # (with --plugin) states that need the bundled reader:
        frd_static.pvsm                   #   static.frd warped by DISP, coloured by STRESS
        frd_transient.pvsm                #   transient_heat.frd, 5 real time steps, fixed colour range
        frd_modes.pvsm                    #   freq.frd, 4 mode shapes as ordinal time steps
        frd_quadratic.pvsm                #   he20_c3d20.frd, a quadratic cell with edges
"""

import argparse
import json
import os
import sys


def logical(path, root):
    rel = os.path.relpath(path, root).replace(os.sep, "/")
    return rel


def rewrite_state(state_path, mapping):
    """Replaces every absolute data path in a saved state with its logical path."""
    with open(state_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    for absolute, logical_path in mapping.items():
        text = text.replace(absolute, logical_path)
        text = text.replace(absolute.replace("/", "\\"), logical_path)
    with open(state_path, "w", encoding="utf-8") as handle:
        handle.write(text)


#: VTK data type names -> VTK XML type attributes (the parallel indexes declare their arrays).
XML_TYPES = {"float": "Float32", "double": "Float64"}


def write_wider_formats(data, fwd):
    """The data behind the wider reader coverage (controller 1.2.0), written straight through VTK so
    every file stays small and byte-reproducible. Returns the reader input of every format and
    "files" - every file written, the pieces an index names included."""
    import math
    from vtkmodules.vtkCommonCore import vtkFloatArray, vtkIdList, vtkPoints
    from vtkmodules.vtkCommonDataModel import vtkCompositeDataSet, vtkMultiBlockDataSet, vtkStructuredGrid, vtkUnstructuredGrid
    from vtkmodules.vtkFiltersCore import vtkElevationFilter, vtkTriangleFilter
    from vtkmodules.vtkFiltersGeneral import vtkDataSetTriangleFilter
    from vtkmodules.vtkFiltersSources import vtkConeSource, vtkCylinderSource, vtkSphereSource, vtkSuperquadricSource
    from vtkmodules.vtkImagingCore import vtkRTAnalyticSource
    from vtkmodules.vtkIOGeometry import vtkSTLWriter
    from vtkmodules.vtkIOPLY import vtkPLYWriter
    from vtkmodules.vtkIOXML import vtkXMLMultiBlockDataWriter, vtkXMLPolyDataWriter, vtkXMLStructuredGridWriter, vtkXMLUnstructuredGridWriter
    try:
        from vtkmodules.vtkFiltersCore import vtkExtractCells
    except ImportError:
        from vtkmodules.vtkFiltersExtraction import vtkExtractCells

    written = []

    def folder(name):
        path = os.path.join(data, name)
        os.makedirs(path, exist_ok=True)
        return path

    def keep_arrays(dataset, names):
        for attributes in (dataset.GetPointData(), dataset.GetCellData()):
            for index in reversed(range(attributes.GetNumberOfArrays())):
                if attributes.GetArrayName(index) not in names:
                    attributes.RemoveArray(index)
        return dataset

    def elevated(algorithm, low, high):
        elevation = vtkElevationFilter()
        elevation.SetInputConnection(algorithm.GetOutputPort())
        elevation.SetLowPoint(*low)
        elevation.SetHighPoint(*high)
        elevation.Update()
        result = elevation.GetOutput().NewInstance()
        result.DeepCopy(elevation.GetOutput())
        return keep_arrays(result, ("Elevation",))

    def write_xml(writer, dataset, path):
        writer.SetFileName(path)
        writer.SetInputData(dataset)
        writer.Write()
        written.append(path)
        return path

    def write_parallel_index(path, kind, pieces, array):
        dataset = pieces[0][1]
        values = dataset.GetPointData().GetArray(array)
        lines = [
            '<?xml version="1.0"?>',
            '<VTKFile type="%s" version="1.0" byte_order="LittleEndian" header_type="UInt64">' % kind,
            '  <%s GhostLevel="0">' % kind,
            '    <PPointData Scalars="%s">' % array,
            '      <PDataArray type="%s" Name="%s"/>' % (XML_TYPES[values.GetDataTypeAsString()], array),
            '    </PPointData>',
            '    <PPoints>',
            '      <PDataArray type="%s" Name="Points" NumberOfComponents="3"/>' % XML_TYPES[dataset.GetPoints().GetData().GetDataTypeAsString()],
            '    </PPoints>',
        ]
        lines += ['    <Piece Source="%s"/>' % os.path.basename(piece_path) for piece_path, _ in pieces]
        lines += ['  </%s>' % kind, '</VTKFile>', '']
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write("\n".join(lines))
        written.append(path)
        return path

    # A polydata surface: a superquadric torus with an elevation field.
    torus = vtkSuperquadricSource()
    torus.SetToroidal(1)
    torus.SetThickness(0.4)
    torus.SetSize(2.0)
    torus.SetThetaResolution(48)
    torus.SetPhiResolution(24)
    vtp = write_xml(vtkXMLPolyDataWriter(), elevated(torus, (0.0, 0.0, -1.0), (0.0, 0.0, 1.0)),
                    fwd(os.path.join(folder("polydata"), "surface.vtp")))

    # A curvilinear structured grid: a quarter annulus, a pressure-like field over it.
    grid = vtkStructuredGrid()
    grid.SetDimensions(9, 7, 5)
    points = vtkPoints()
    points.SetDataTypeToFloat()
    points.GetData().SetName("Points")
    pressure = vtkFloatArray()
    pressure.SetName("pressure")
    for k in range(5):
        for j in range(7):
            theta = 0.5 * math.pi * j / 6.0
            for i in range(9):
                radius = 1.0 + i / 8.0
                points.InsertNextPoint(radius * math.cos(theta), radius * math.sin(theta), 0.25 * k)
                pressure.InsertNextValue(radius * math.cos(theta) + 0.25 * k)
    grid.SetPoints(points)
    grid.GetPointData().SetScalars(pressure)
    vts = write_xml(vtkXMLStructuredGridWriter(), grid, fwd(os.path.join(folder("structured"), "grid.vts")))

    # A multiblock index over a solid block (a tetrahedralized wavelet) and a shell block (a sphere).
    wavelet = vtkRTAnalyticSource()
    wavelet.SetWholeExtent(-3, 3, -3, 3, -3, 3)
    tetrahedra = vtkDataSetTriangleFilter()
    tetrahedra.SetInputConnection(wavelet.GetOutputPort())
    tetrahedra.Update()
    solid = vtkUnstructuredGrid()
    solid.DeepCopy(tetrahedra.GetOutput())
    keep_arrays(solid, ("RTData",))
    sphere = vtkSphereSource()
    sphere.SetRadius(4.0)
    sphere.SetThetaResolution(24)
    sphere.SetPhiResolution(16)
    shell = elevated(sphere, (0.0, 0.0, -4.0), (0.0, 0.0, 4.0))
    blocks = vtkMultiBlockDataSet()
    blocks.SetNumberOfBlocks(2)
    blocks.SetBlock(0, solid)
    blocks.GetMetaData(0).Set(vtkCompositeDataSet.NAME(), "solid")
    blocks.SetBlock(1, shell)
    blocks.GetMetaData(1).Set(vtkCompositeDataSet.NAME(), "shell")
    multiblock_dir = folder("multiblock")
    vtm = fwd(os.path.join(multiblock_dir, "blocks.vtm"))
    multiblock_writer = vtkXMLMultiBlockDataWriter()
    multiblock_writer.SetFileName(vtm)
    multiblock_writer.SetInputData(blocks)
    multiblock_writer.Write()
    for directory, _, names in os.walk(multiblock_dir):
        written.extend(fwd(os.path.join(directory, name)) for name in sorted(names))

    # Parallel indexes over two pieces each: the solid split in halves, two spheres side by side.
    parallel_dir = folder("parallel")
    halves = []
    cell_count = solid.GetNumberOfCells()
    for half, (first, last) in enumerate(((0, cell_count // 2), (cell_count // 2, cell_count))):
        ids = vtkIdList()
        for cell in range(first, last):
            ids.InsertNextId(cell)
        extract = vtkExtractCells()
        extract.SetInputData(solid)
        extract.SetCellList(ids)
        extract.Update()
        piece = keep_arrays(extract.GetOutput(), ("RTData",))
        halves.append((write_xml(vtkXMLUnstructuredGridWriter(), piece, fwd(os.path.join(parallel_dir, "tets_%d.vtu" % half))), piece))
    pvtu = write_parallel_index(fwd(os.path.join(parallel_dir, "tets.pvtu")), "PUnstructuredGrid", halves, "RTData")
    spheres = []
    for index, center in enumerate((-1.5, 1.5)):
        ball = vtkSphereSource()
        ball.SetCenter(center, 0.0, 0.0)
        ball.SetThetaResolution(16)
        ball.SetPhiResolution(12)
        piece = elevated(ball, (-3.0, 0.0, 0.0), (3.0, 0.0, 0.0))
        spheres.append((write_xml(vtkXMLPolyDataWriter(), piece, fwd(os.path.join(parallel_dir, "shell_%d.vtp" % index))), piece))
    pvtp = write_parallel_index(fwd(os.path.join(parallel_dir, "shell.pvtp")), "PPolyData", spheres, "Elevation")

    # Plain geometry: a binary STL cylinder, a binary PLY cone, a Wavefront OBJ cube (written by
    # hand: the OBJ writer's text stream would write CRLF on Windows).
    geometry_dir = folder("geometry")
    cylinder = vtkCylinderSource()
    cylinder.SetResolution(24)
    triangles = vtkTriangleFilter()
    triangles.SetInputConnection(cylinder.GetOutputPort())
    stl = fwd(os.path.join(geometry_dir, "part.stl"))
    stl_writer = vtkSTLWriter()
    stl_writer.SetInputConnection(triangles.GetOutputPort())
    stl_writer.SetFileTypeToBinary()
    stl_writer.SetHeader("OmnibusCloud ParaView corpus: a cylinder")
    stl_writer.SetFileName(stl)
    stl_writer.Write()
    written.append(stl)
    cone = vtkConeSource()
    cone.SetResolution(24)
    cone_triangles = vtkTriangleFilter()
    cone_triangles.SetInputConnection(cone.GetOutputPort())
    ply = fwd(os.path.join(geometry_dir, "part.ply"))
    ply_writer = vtkPLYWriter()
    ply_writer.SetInputConnection(cone_triangles.GetOutputPort())
    ply_writer.SetFileTypeToBinary()
    ply_writer.SetDataByteOrderToLittleEndian()
    ply_writer.SetFileName(ply)
    ply_writer.Write()
    written.append(ply)
    obj = fwd(os.path.join(geometry_dir, "part.obj"))
    corners = [(x, y, z) for x in (-1.0, 1.0) for y in (-1.0, 1.0) for z in (-1.0, 1.0)]
    faces = [(1, 3, 4), (1, 4, 2), (5, 6, 8), (5, 8, 7), (1, 2, 6), (1, 6, 5),
             (3, 7, 8), (3, 8, 4), (1, 5, 7), (1, 7, 3), (2, 4, 8), (2, 8, 6)]
    with open(obj, "w", encoding="ascii", newline="\n") as handle:
        handle.write("# OmnibusCloud ParaView corpus: a cube\n")
        handle.writelines("v %g %g %g\n" % corner for corner in corners)
        handle.writelines("f %d %d %d\n" % face for face in faces)
    written.append(obj)

    return {"vtp": vtp, "vts": vts, "vtm": vtm, "pvtu": pvtu, "pvtp": pvtp, "stl": stl, "ply": ply, "obj": obj,
            "files": sorted(set(written))}


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--plugin", help="path of the bundled reader plugin; enables the reader states")
    parser.add_argument("--frd", help="directory whose *.frd files become data/frd/ (requires --plugin)")
    args = parser.parse_args(argv)
    if bool(args.plugin) != bool(args.frd):
        parser.error("--plugin and --frd go together")

    from paraview import simple as pv
    from paraview import servermanager

    out = os.path.abspath(args.out)
    data = os.path.join(out, "data")
    series_dir = os.path.join(data, "series")
    states = os.path.join(out, "states")
    frd_dir = os.path.join(data, "frd")
    plugin_states = os.path.join(states, "OmnibusCloudFrdReader")
    for d in (data, series_dir, states) + ((frd_dir, plugin_states) if args.plugin else ()):
        os.makedirs(d, exist_ok=True)

    version = pv.GetParaViewSourceVersion()
    produced = []

    def fwd(path):
        return os.path.abspath(path).replace("\\", "/")

    # ------------------------------------------------------------------ data
    wavelet = pv.Wavelet()
    wavelet.WholeExtent = [-5, 5, -5, 5, -5, 5]
    vti = fwd(os.path.join(data, "wavelet.vti"))
    pv.SaveData(vti, proxy=wavelet)

    tets = pv.Tetrahedralize(Input=wavelet)
    vtu = fwd(os.path.join(data, "tets.vtu"))
    pv.SaveData(vtu, proxy=tets)

    # Rectilinear grid straight from VTK.
    from vtkmodules.vtkCommonCore import vtkDoubleArray
    from vtkmodules.vtkCommonDataModel import vtkRectilinearGrid
    from vtkmodules.vtkIOXML import vtkXMLRectilinearGridWriter
    grid = vtkRectilinearGrid()
    grid.SetDimensions(9, 7, 5)
    for setter, name, n, scale in ((grid.SetXCoordinates, "x", 9, 1.0), (grid.SetYCoordinates, "y", 7, 1.5), (grid.SetZCoordinates, "z", 5, 2.0)):
        coords = vtkDoubleArray()
        coords.SetName(name)  # unnamed arrays are written under their pointer address: not reproducible
        for i in range(n):
            coords.InsertNextValue(i * i * scale * 0.1)
        setter(coords)
    values = vtkDoubleArray()
    values.SetName("height")
    for k in range(5):
        for j in range(7):
            for i in range(9):
                values.InsertNextValue(i + j * 0.5 + k * 0.25)
    grid.GetPointData().SetScalars(values)
    vtr = fwd(os.path.join(data, "grid.vtr"))
    writer = vtkXMLRectilinearGridWriter()
    writer.SetFileName(vtr)
    writer.SetInputData(grid)
    writer.Write()

    # Legacy VTK: the format OpenFOAM's foamToVTK and the postProcessing surface writers produce, which
    # users open beside a case (ParaView reads it through LegacyVTKReader).
    from vtkmodules.vtkFiltersCore import vtkElevationFilter
    from vtkmodules.vtkFiltersSources import vtkSphereSource
    from vtkmodules.vtkIOLegacy import vtkPolyDataWriter
    legacy_dir = os.path.join(data, "legacy")
    os.makedirs(legacy_dir, exist_ok=True)
    sphere_source = vtkSphereSource()
    sphere_source.SetRadius(2.0)
    sphere_source.SetThetaResolution(24)
    sphere_source.SetPhiResolution(16)
    elevation = vtkElevationFilter()
    elevation.SetInputConnection(sphere_source.GetOutputPort())
    elevation.SetLowPoint(0.0, 0.0, -2.0)
    elevation.SetHighPoint(0.0, 0.0, 2.0)
    legacy = fwd(os.path.join(legacy_dir, "surface.vtk"))
    legacy_writer = vtkPolyDataWriter()
    legacy_writer.SetInputConnection(elevation.GetOutputPort())
    legacy_writer.SetFileTypeToASCII()
    # Through a string: the writer's own file stream is text-mode and would write CRLF on Windows, while
    # data files are committed byte-exact - LF on every platform.
    legacy_writer.WriteToOutputStringOn()
    legacy_writer.Write()
    with open(legacy, "w", encoding="ascii", newline="\n") as handle:
        handle.write(legacy_writer.GetOutputString())

    wider = write_wider_formats(data, fwd)

    # Time series: 5 wavelets of growing amplitude + a PVD index.
    pieces = []
    for step in range(5):
        w = pv.Wavelet()
        w.WholeExtent = [-5, 5, -5, 5, -5, 5]
        w.Maximum = 255.0 * (1.0 + 0.2 * step)
        piece = fwd(os.path.join(series_dir, "series_%03d.vti" % step))
        pv.SaveData(piece, proxy=w)
        pieces.append(piece)
        pv.Delete(w)
    pvd = fwd(os.path.join(series_dir, "series.pvd"))
    with open(pvd, "w", encoding="utf-8") as handle:
        handle.write('<?xml version="1.0"?>\n<VTKFile type="Collection" version="0.1" byte_order="LittleEndian">\n  <Collection>\n')
        for step, piece in enumerate(pieces):
            handle.write('    <DataSet timestep="%g" group="" part="0" file="%s"/>\n' % (step * 0.5, os.path.basename(piece)))
        handle.write('  </Collection>\n</VTKFile>\n')

    mapping = {
        vti: "data/wavelet.vti",
        vtu: "data/tets.vtu",
        vtr: "data/grid.vtr",
        pvd: "data/series/series.pvd",
        legacy: "data/legacy/surface.vtk",
    }
    for piece in pieces:
        mapping[piece] = "data/series/" + os.path.basename(piece)
    for path in wider["files"]:
        mapping[path] = logical(path, out)

    # CalculiX results for the bundled reader: copied verbatim, one logical path each.
    import shutil
    frd_files = {}
    if args.frd:
        for name in sorted(os.listdir(args.frd)):
            if name.lower().endswith(".frd"):
                target = fwd(os.path.join(frd_dir, name))
                shutil.copy(os.path.join(args.frd, name), target)
                frd_files[os.path.splitext(name)[0]] = target
                mapping[target] = "data/frd/" + name

    # ------------------------------------------------------------------ states
    def save(name, build, folder=None):
        # A fresh session per state; the view must be created explicitly after ResetSession so it is
        # registered in the new session's "views" collection (GetActiveViewOrCreate would hand back a
        # stale view and SaveState would save no view at all).
        pv.ResetSession()
        if args.plugin:
            pv.LoadPlugin(os.path.abspath(args.plugin), remote=False, ns=globals())
        view = pv.CreateRenderView()
        pv.SetActiveView(view)
        pv.AssignViewToLayout(view)
        view.ViewSize = [640, 480]
        build(view)
        pv.ResetCamera(view)
        pv.Render(view)
        path = os.path.join(folder or states, name + ".pvsm")
        pv.SaveState(path)
        rewrite_state(path, mapping)
        produced.append(name + ".pvsm" if folder is None else os.path.basename(folder) + "/" + name + ".pvsm")
        print("state:", name)

    def vti_contour(view):
        reader = pv.XMLImageDataReader(registrationName="wavelet.vti", FileName=[vti])
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "RTData"))
        contour = pv.Contour(registrationName="Contour1", Input=reader, ContourBy=["POINTS", "RTData"], Isosurfaces=[157.0])
        pv.Show(contour, view)

    def vti_volume(view):
        reader = pv.XMLImageDataReader(registrationName="wavelet.vti", FileName=[vti])
        rep = pv.Show(reader, view)
        rep.SetRepresentationType("Volume")
        pv.ColorBy(rep, ("POINTS", "RTData"))

    def vtu_slice_clip_glyph(view):
        reader = pv.XMLUnstructuredGridReader(registrationName="tets.vtu", FileName=[vtu])
        pv.Show(reader, view)
        slice_ = pv.Slice(registrationName="Slice1", Input=reader)
        slice_.SliceType = "Plane"
        slice_.SliceType.Normal = [1.0, 0.0, 0.0]
        pv.Show(slice_, view)
        clip = pv.Clip(registrationName="Clip1", Input=reader)
        clip.ClipType = "Plane"
        clip.ClipType.Normal = [0.0, 1.0, 0.0]
        pv.Show(clip, view)
        gradient = pv.Gradient(registrationName="Gradient1", Input=reader)
        gradient.ScalarArray = ["POINTS", "RTData"]
        glyph = pv.Glyph(registrationName="Glyph1", Input=gradient, GlyphType="Arrow")
        glyph.OrientationArray = ["POINTS", "Gradient"]
        glyph.ScaleArray = ["POINTS", "No scale array"]
        glyph.ScaleFactor = 0.5
        pv.Show(glyph, view)

    def vtr_surface(view):
        reader = pv.XMLRectilinearGridReader(registrationName="grid.vtr", FileName=[vtr])
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "height"))

    # The series scenes contour the wavelet at a fixed value: the amplitude grows with every step, so
    # the isosurface (and the frame) changes per timestep. Outline/surface renders of the same box would
    # look identical across steps and could not tell a silently failed time switch (a task rendering its
    # anchor piece instead of its own) from a correct one.
    def pvd_series(view):
        reader = pv.PVDReader(registrationName="series.pvd", FileName=pvd)
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "RTData"))
        contour = pv.Contour(registrationName="Contour1", Input=reader, ContourBy=["POINTS", "RTData"], Isosurfaces=[200.0])
        pv.Show(contour, view)
        scene = pv.GetAnimationScene()
        scene.UpdateAnimationUsingDataTimeSteps()

    def file_series(view):
        reader = pv.XMLImageDataReader(registrationName="series_*.vti", FileName=pieces)
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "RTData"))
        contour = pv.Contour(registrationName="Contour1", Input=reader, ContourBy=["POINTS", "RTData"], Isosurfaces=[200.0])
        pv.Show(contour, view)
        scene = pv.GetAnimationScene()
        scene.UpdateAnimationUsingDataTimeSteps()

    def sphere_static(view):
        sphere = pv.Sphere(registrationName="Sphere1", Radius=2.0)
        pv.Show(sphere, view)

    def legacy_vtk(view):
        reader = pv.LegacyVTKReader(registrationName="surface.vtk", FileNames=[legacy])
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "Elevation"))

    save("vti_contour", vti_contour)
    save("vti_volume", vti_volume)
    save("vtu_slice_clip_glyph", vtu_slice_clip_glyph)
    save("vtr_surface", vtr_surface)
    save("pvd_series", pvd_series)
    save("file_series", file_series)
    save("sphere_static", sphere_static)
    save("legacy_vtk", legacy_vtk)

    # Wider reader and filter coverage (controller 1.2.0): one scene per format family, every common
    # filter used at least once (hidden ones too - a state saves every proxy of the pipeline).
    def vtp_surface(view):
        reader = pv.XMLPolyDataReader(registrationName="surface.vtp", FileName=[wider["vtp"]])
        clean = pv.Clean(registrationName="Clean1", Input=reader)
        triangulate = pv.Triangulate(registrationName="Triangulate1", Input=clean)
        decimate = pv.Decimate(registrationName="Decimate1", Input=triangulate, TargetReduction=0.5)
        smooth = pv.Smooth(registrationName="Smooth1", Input=decimate, NumberofIterations=20)
        normals = pv.SurfaceNormals(registrationName="GenerateSurfaceNormals1", Input=smooth)
        elevation = pv.Elevation(registrationName="Elevation1", Input=normals, LowPoint=[0.0, 0.0, -1.0], HighPoint=[0.0, 0.0, 1.0])
        rep = pv.Show(elevation, view)
        pv.ColorBy(rep, ("POINTS", "Elevation"))
        pv.Show(pv.FeatureEdges(registrationName="FeatureEdges1", Input=reader), view)
        pv.Curvature(registrationName="Curvature1", Input=triangulate)

    def vts_structured(view):
        reader = pv.XMLStructuredGridReader(registrationName="grid.vts", FileName=[wider["vts"]])
        subset = pv.ExtractSubset(registrationName="ExtractSubset1", Input=reader, VOI=[0, 8, 0, 6, 0, 2])
        warp = pv.WarpByScalar(registrationName="WarpByScalar1", Input=subset, Scalars=["POINTS", "pressure"], ScaleFactor=0.2)
        rep = pv.Show(warp, view)
        pv.ColorBy(rep, ("POINTS", "pressure"))
        pv.Show(pv.Outline(registrationName="Outline1", Input=reader), view)
        shrink = pv.Shrink(registrationName="Shrink1", Input=reader, ShrinkFactor=0.8)
        cells = pv.PointDatatoCellData(registrationName="PointDatatoCellData1", Input=shrink)
        cells_rep = pv.Show(cells, view)
        pv.ColorBy(cells_rep, ("CELLS", "pressure"))

    def vtm_blocks(view):
        reader = pv.XMLMultiBlockDataReader(registrationName="blocks.vtm", FileName=[wider["vtm"]])
        shell = pv.ExtractBlock(registrationName="ExtractBlock1", Input=reader, Selectors=["/Root/shell"])
        shell_rep = pv.Show(shell, view)
        pv.ColorBy(shell_rep, ("POINTS", "Elevation"))
        solid = pv.ExtractBlock(registrationName="ExtractBlock2", Input=reader, Selectors=["/Root/solid"])
        merged = pv.MergeBlocks(registrationName="MergeBlocks1", Input=solid)
        merged_rep = pv.Show(merged, view)
        pv.ColorBy(merged_rep, ("POINTS", "RTData"))
        pv.GroupDatasets(registrationName="GroupDatasets1", Input=[shell, merged])
        shell_merged = pv.MergeBlocks(registrationName="MergeBlocks2", Input=shell)
        pv.AppendDatasets(registrationName="AppendDatasets1", Input=[merged, shell_merged])

    def parallel_pieces(view):
        tets = pv.XMLPartitionedUnstructuredGridReader(registrationName="tets.pvtu", FileName=[wider["pvtu"]])
        tets_rep = pv.Show(tets, view)
        pv.ColorBy(tets_rep, ("POINTS", "RTData"))
        shell = pv.XMLPartitionedPolydataReader(registrationName="shell.pvtp", FileName=[wider["pvtp"]])
        shell_rep = pv.Show(shell, view)
        pv.ColorBy(shell_rep, ("POINTS", "Elevation"))

    def geometry_formats(view):
        stl = pv.STLReader(registrationName="part.stl", FileNames=[wider["stl"]])
        moved = pv.Transform(registrationName="Transform1", Input=stl)
        moved.Transform.Translate = [3.0, 0.0, 0.0]
        pv.Show(moved, view)
        ply = pv.PLYReader(registrationName="part.ply", FileNames=[wider["ply"]])
        pv.Show(pv.AxisAlignedReflect(registrationName="Reflect1", Input=ply), view)
        pv.Show(pv.WavefrontOBJReader(registrationName="part.obj", FileName=wider["obj"]), view)

    def vtu_more_filters(view):
        reader = pv.XMLUnstructuredGridReader(registrationName="tets.vtu", FileName=[vtu])
        region = pv.ExtractCellsByRegion(registrationName="ExtractCellsByRegion1", Input=reader, IntersectWith="Sphere")
        region.IntersectWith.Radius = 3.0
        pv.Show(region, view).SetRepresentationType("Wireframe")
        centers = pv.CellCenters(registrationName="CellCenters1", Input=region)
        pv.Show(pv.MaskPoints(registrationName="MaskPoints1", Input=centers, OnRatio=3, GenerateVertices=1), view)
        pv.Tessellate(registrationName="Tessellate1", Input=reader)
        plane = pv.Plane(registrationName="Plane1", Origin=[-5.0, -5.0, 0.0], Point1=[5.0, -5.0, 0.0], Point2=[-5.0, 5.0, 0.0], XResolution=20, YResolution=20)
        resampled = pv.ResampleWithDataset(registrationName="ResampleWithDataset1", SourceDataArrays=reader, DestinationMesh=plane)
        resampled_rep = pv.Show(resampled, view)
        pv.ColorBy(resampled_rep, ("POINTS", "RTData"))
        gradient = pv.Gradient(registrationName="Gradient1", Input=reader)
        gradient.ScalarArray = ["POINTS", "RTData"]
        seeds = pv.Line(registrationName="Line1", Point1=[-4.0, -4.0, -4.0], Point2=[4.0, 4.0, 4.0], Resolution=10)
        tracer = pv.StreamTracerWithCustomSource(registrationName="StreamTracerWithCustomSource1", Input=gradient, SeedSource=seeds)
        tracer.Vectors = ["POINTS", "Gradient"]
        tracer.MaximumStreamlineLength = 10.0
        pv.Show(tracer, view)
        pv.Show(pv.AnnotateAttributeData(registrationName="AnnotateAttributeData1", Input=reader, SelectInputArray=["POINTS", "RTData"], Prefix="RTData: "), view)
        pv.Show(pv.Ruler(registrationName="Ruler1", Point1=[-5.0, -5.0, -5.0], Point2=[5.0, -5.0, -5.0]), view)
        pv.Show(pv.SliceWithPlane(registrationName="SliceWithPlane1", Input=reader), view)
        pv.CellSize(registrationName="CellSize1", Input=reader)
        pv.MeshQuality(registrationName="MeshQuality1", Input=reader)
        pv.ExtractEdges(registrationName="ExtractEdges1", Input=region)

    save("vtp_surface", vtp_surface)
    save("vts_structured", vts_structured)
    save("vtm_blocks", vtm_blocks)
    save("parallel_pieces", parallel_pieces)
    save("geometry_formats", geometry_formats)
    save("vtu_more_filters", vtu_more_filters)

    # ------------------------------------------------------------------ reader states
    plugin_version = None
    if args.plugin:
        with open(args.plugin, "r", encoding="utf-8") as handle:
            for line in handle:
                if line.strip().startswith("__version__"):
                    plugin_version = line.split("=", 1)[1].strip().strip("\"'")
                    break

        def frd_static(view):
            reader = pv.OmnibusCloudFrdReader(registrationName="static.frd", FileName=frd_files["static"])
            warp = pv.WarpByVector(registrationName="WarpByVector1", Input=reader, Vectors=["POINTS", "DISP"], ScaleFactor=20.0)
            rep = pv.Show(warp, view)
            pv.ColorBy(rep, ("POINTS", "STRESS", "Magnitude"))

        def frd_transient(view):
            reader = pv.OmnibusCloudFrdReader(registrationName="transient_heat.frd", FileName=frd_files["transient_heat"])
            rep = pv.Show(reader, view)
            pv.ColorBy(rep, ("POINTS", "NDTEMP"))
            lut = pv.GetColorTransferFunction("NDTEMP")
            lut.RescaleTransferFunction(0.0, 100.0)
            lut.AutomaticRescaleRangeMode = "Never"
            scene = pv.GetAnimationScene()
            scene.UpdateAnimationUsingDataTimeSteps()

        def frd_modes(view):
            reader = pv.OmnibusCloudFrdReader(registrationName="freq.frd", FileName=frd_files["freq"])
            warp = pv.WarpByVector(registrationName="WarpByVector1", Input=reader, Vectors=["POINTS", "DISP"], ScaleFactor=1.0e-4)
            rep = pv.Show(warp, view)
            pv.ColorBy(rep, ("POINTS", "DISP", "Magnitude"))
            scene = pv.GetAnimationScene()
            scene.UpdateAnimationUsingDataTimeSteps()

        def frd_quadratic(view):
            reader = pv.OmnibusCloudFrdReader(registrationName="he20_c3d20.frd", FileName=frd_files["he20_c3d20"])
            rep = pv.Show(reader, view)
            rep.SetRepresentationType("Surface With Edges")
            pv.ColorBy(rep, ("POINTS", "STRESS", "Magnitude"))

        save("frd_static", frd_static, plugin_states)
        save("frd_transient", frd_transient, plugin_states)
        save("frd_modes", frd_modes, plugin_states)
        save("frd_quadratic", frd_quadratic, plugin_states)

    manifest = {
        "paraview": version,
        "data": sorted(v for v in mapping.values()),
        "states": produced,
        "timesteps": {"pvd_series": [0.0, 0.5, 1.0, 1.5, 2.0], "file_series": [0, 1, 2, 3, 4]},
    }
    if args.plugin:
        manifest["plugins"] = {"OmnibusCloudFrdReader": plugin_version}
        manifest["timesteps"]["OmnibusCloudFrdReader/frd_transient"] = [0.2, 0.4, 0.6, 0.8, 1.0]
        manifest["timesteps"]["OmnibusCloudFrdReader/frd_modes"] = [1.0, 2.0, 3.0, 4.0]
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    print("fixtures written to", out, "with", version)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
