# -*- coding: utf-8 -*-
"""Serious corpus for the visual video check: a 120-timestep PVD wavelet series at richer detail.

    pvpython --force-offscreen-rendering --disable-registry generate_serious_corpus.py --out <WitCloud/@Data/paraview>

A fixed-amplitude Wavelet (31^3 points) whose center orbits the volume, contoured at two levels and
sliced; the reader surface stays hidden so the moving isosurfaces read clearly. 1280x720 view saved
in the state. Same layout conventions as the scale corpus (pieces named series_NNN.vti, one PVD
index), under data/serious + states/pvd_serious.pvsm.
"""

import argparse
import math
import os
import sys

STEPS = 120
EXTENT = 15


def fwd(path):
    return os.path.abspath(path).replace("\\", "/")


def rewrite_state(state_path, mapping):
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

    out = os.path.abspath(args.out)
    serious_dir = os.path.join(out, "data", "serious")
    states = os.path.join(out, "states")
    os.makedirs(serious_dir, exist_ok=True)
    os.makedirs(states, exist_ok=True)

    # The center orbits an ellipse and bobs vertically: continuous, visually obvious motion at any
    # step, constant per-frame cost.
    pieces = []
    for step in range(STEPS):
        phase = 2.0 * math.pi * step / STEPS
        w = pv.Wavelet()
        w.WholeExtent = [-EXTENT, EXTENT, -EXTENT, EXTENT, -EXTENT, EXTENT]
        w.Center = [6.0 * math.cos(phase), 4.0 * math.sin(phase), 3.0 * math.sin(2.0 * phase)]
        piece = fwd(os.path.join(serious_dir, "series_%03d.vti" % step))
        pv.SaveData(piece, proxy=w)
        pieces.append(piece)
        pv.Delete(w)
        del w

    pvd = fwd(os.path.join(serious_dir, "series.pvd"))
    with open(pvd, "w", encoding="utf-8") as handle:
        handle.write('<?xml version="1.0"?>\n<VTKFile type="Collection" version="0.1" byte_order="LittleEndian">\n  <Collection>\n')
        for step, piece in enumerate(pieces):
            handle.write('    <DataSet timestep="%g" group="" part="0" file="%s"/>\n' % (step / 24.0, os.path.basename(piece)))
        handle.write('  </Collection>\n</VTKFile>\n')

    mapping = {pvd: "data/serious/series.pvd"}
    for piece in pieces:
        mapping[piece] = "data/serious/" + os.path.basename(piece)

    pv.ResetSession()
    view = pv.CreateRenderView()
    pv.SetActiveView(view)
    pv.AssignViewToLayout(view)
    view.ViewSize = [1280, 720]
    view.OrientationAxesVisibility = 0
    reader = pv.PVDReader(registrationName="series.pvd", FileName=pvd)
    outline = pv.Show(reader, view)
    outline.SetRepresentationType("Outline")
    contour = pv.Contour(registrationName="Contour1", Input=reader, ContourBy=["POINTS", "RTData"], Isosurfaces=[160.0, 220.0])
    contour_display = pv.Show(contour, view)
    pv.ColorBy(contour_display, ("POINTS", "RTData"))
    slice_ = pv.Slice(registrationName="Slice1", Input=reader)
    slice_.SliceType = "Plane"
    slice_.SliceType.Normal = [0.0, 0.0, 1.0]
    slice_display = pv.Show(slice_, view)
    pv.ColorBy(slice_display, ("POINTS", "RTData"))
    slice_display.Opacity = 0.55
    scene = pv.GetAnimationScene()
    scene.UpdateAnimationUsingDataTimeSteps()
    pv.ResetCamera(view)
    camera = view.GetActiveCamera()
    camera.Elevation(20.0)
    camera.Azimuth(25.0)
    pv.Render(view)
    state_path = os.path.join(states, "pvd_serious.pvsm")
    pv.SaveState(state_path)
    rewrite_state(state_path, mapping)

    total = sum(os.path.getsize(p) for p in pieces)
    print("pieces: %d, total %.1f MB, state: %s" % (len(pieces), total / 1e6, state_path))
    return 0


if __name__ == "__main__":
    code = main(sys.argv[1:])
    sys.stdout.flush()
    os._exit(code)
