# -*- coding: utf-8 -*-
"""Scale corpus for the WitCloud I4 live tests: a 60-timestep PVD wavelet series + a contour state.

    pvpython --force-offscreen-rendering --disable-registry generate_scale_series.py --out <WitCloud/@Data/paraview>

Follows the golden corpus recipe (generate_fixtures.py): wavelet amplitude grows per step so the
contoured isosurface (and every rendered frame) is visibly different — a task silently rendering its
anchor piece instead of its own timestep produces duplicate digests and fails the test. Pieces are
named series_NNN.vti so the live upload helper's series detection ("/series_") picks them up.
"""

import argparse
import os
import sys

STEPS = 60
EXTENT = 12  # 25^3 = 15,625 points per piece: small uploads, non-trivial contour


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
    scale_dir = os.path.join(out, "data", "scale")
    states = os.path.join(out, "states")
    os.makedirs(scale_dir, exist_ok=True)
    os.makedirs(states, exist_ok=True)

    # A moving wavelet: fixed amplitude (a growing one saturates the colour map after ~14 steps and
    # the frames stop changing), the center walks a diagonal across the volume, so the contoured
    # isosurface translates every step - every frame is guaranteed visually distinct and every frame
    # costs the same to render.
    pieces = []
    for step in range(STEPS):
        w = pv.Wavelet()
        w.WholeExtent = [-EXTENT, EXTENT, -EXTENT, EXTENT, -EXTENT, EXTENT]
        offset = -6.0 + 12.0 * step / (STEPS - 1)
        w.Center = [offset, offset * 0.5, -offset * 0.35]
        piece = fwd(os.path.join(scale_dir, "series_%03d.vti" % step))
        pv.SaveData(piece, proxy=w)
        pieces.append(piece)
        pv.Delete(w)
        del w

    pvd = fwd(os.path.join(scale_dir, "series.pvd"))
    with open(pvd, "w", encoding="utf-8") as handle:
        handle.write('<?xml version="1.0"?>\n<VTKFile type="Collection" version="0.1" byte_order="LittleEndian">\n  <Collection>\n')
        for step, piece in enumerate(pieces):
            handle.write('    <DataSet timestep="%g" group="" part="0" file="%s"/>\n' % (step * 0.1, os.path.basename(piece)))
        handle.write('  </Collection>\n</VTKFile>\n')

    mapping = {pvd: "data/scale/series.pvd"}
    for piece in pieces:
        mapping[piece] = "data/scale/" + os.path.basename(piece)

    pv.ResetSession()
    view = pv.CreateRenderView()
    pv.SetActiveView(view)
    pv.AssignViewToLayout(view)
    view.ViewSize = [640, 480]
    reader = pv.PVDReader(registrationName="series.pvd", FileName=pvd)
    rep = pv.Show(reader, view)
    pv.ColorBy(rep, ("POINTS", "RTData"))
    contour = pv.Contour(registrationName="Contour1", Input=reader, ContourBy=["POINTS", "RTData"], Isosurfaces=[200.0])
    pv.Show(contour, view)
    scene = pv.GetAnimationScene()
    scene.UpdateAnimationUsingDataTimeSteps()
    pv.ResetCamera(view)
    pv.Render(view)
    state_path = os.path.join(states, "pvd_scale.pvsm")
    pv.SaveState(state_path)
    rewrite_state(state_path, mapping)

    total = sum(os.path.getsize(p) for p in pieces)
    print("pieces: %d, total %.1f MB, state: %s" % (len(pieces), total / 1e6, state_path))
    return 0


if __name__ == "__main__":
    code = main(sys.argv[1:])
    sys.stdout.flush()
    os._exit(code)
