"""Generates the ParaView fixture corpus (docs 03, sections 8.3 and 16.3) with the PINNED runtime.

Run with the bundled pvpython of the runtime under test:

    pvpython generate_fixtures.py --out <corpus-dir>

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


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)

    from paraview import simple as pv
    from paraview import servermanager

    out = os.path.abspath(args.out)
    data = os.path.join(out, "data")
    series_dir = os.path.join(data, "series")
    states = os.path.join(out, "states")
    for d in (data, series_dir, states):
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
    for setter, n, scale in ((grid.SetXCoordinates, 9, 1.0), (grid.SetYCoordinates, 7, 1.5), (grid.SetZCoordinates, 5, 2.0)):
        coords = vtkDoubleArray()
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
    }
    for piece in pieces:
        mapping[piece] = "data/series/" + os.path.basename(piece)

    # ------------------------------------------------------------------ states
    def save(name, build):
        # A fresh session per state; the view must be created explicitly after ResetSession so it is
        # registered in the new session's "views" collection (GetActiveViewOrCreate would hand back a
        # stale view and SaveState would save no view at all).
        pv.ResetSession()
        view = pv.CreateRenderView()
        pv.SetActiveView(view)
        pv.AssignViewToLayout(view)
        view.ViewSize = [640, 480]
        build(view)
        pv.ResetCamera(view)
        pv.Render(view)
        path = os.path.join(states, name + ".pvsm")
        pv.SaveState(path)
        rewrite_state(path, mapping)
        produced.append(name + ".pvsm")
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

    def pvd_series(view):
        reader = pv.PVDReader(registrationName="series.pvd", FileName=pvd)
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "RTData"))
        scene = pv.GetAnimationScene()
        scene.UpdateAnimationUsingDataTimeSteps()

    def file_series(view):
        reader = pv.XMLImageDataReader(registrationName="series_*.vti", FileName=pieces)
        rep = pv.Show(reader, view)
        pv.ColorBy(rep, ("POINTS", "RTData"))
        scene = pv.GetAnimationScene()
        scene.UpdateAnimationUsingDataTimeSteps()

    def sphere_static(view):
        sphere = pv.Sphere(registrationName="Sphere1", Radius=2.0)
        pv.Show(sphere, view)

    save("vti_contour", vti_contour)
    save("vti_volume", vti_volume)
    save("vtu_slice_clip_glyph", vtu_slice_clip_glyph)
    save("vtr_surface", vtr_surface)
    save("pvd_series", pvd_series)
    save("file_series", file_series)
    save("sphere_static", sphere_static)

    manifest = {
        "paraview": version,
        "data": sorted(v for v in mapping.values()),
        "states": produced,
        "timesteps": {"pvd_series": [0.0, 0.5, 1.0, 1.5, 2.0], "file_series": [0, 1, 2, 3, 4]},
    }
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    print("fixtures written to", out, "with", version)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
