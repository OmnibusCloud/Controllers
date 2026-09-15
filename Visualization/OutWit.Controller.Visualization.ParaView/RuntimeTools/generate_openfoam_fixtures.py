"""Adds the OpenFOAM fixture to an existing fixture corpus with the PINNED runtime (docs 03,
section 8.3 - the engineering-reader pattern: corpus fixture + allowlist regeneration).

    pvpython generate_openfoam_fixtures.py --corpus <corpus-dir>

Writes the synthetic case of generate_openfoam_case.py (a 4x3x2 hex block, times 0 / 0.5 / 1,
the last one moving the mesh) under <corpus>/data/openfoam/box/ with its stub box.foam, saves a
REAL state over it (states/openfoam_box.pvsm: the native OpenFOAMReader coloured by p with a
fixed range, the animation following the reader's times) with the stub path rewritten to the
package's logical path, and appends the data files, the state and its timeline to
manifest.json. The rest of the corpus is untouched - the other fixtures stay byte-identical.

The package a producing plugin builds from this state is what ParaViewCorpus.FilesOf() lists
for the state: the stub, system/, constant/ dictionaries and mesh, the first listed time
directory (static - the reader scans it at load), and the later time with its own timeline
index and its moved polyMesh/points.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import generate_openfoam_case  # noqa: E402

CASE_NAME = "box"

STATE_NAME = "openfoam_box"

TIMELINE = [0.5, 1.0]


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", required=True)
    args = parser.parse_args(argv)

    from paraview import simple as pv

    corpus = os.path.abspath(args.corpus)
    case_dir = os.path.join(corpus, "data", "openfoam", CASE_NAME)
    written = generate_openfoam_case.write_case(case_dir, cells=(4, 3, 2), times=(0.0, 0.5, 1.0), moving=True, stub=CASE_NAME + ".foam")
    stub = os.path.join(case_dir, CASE_NAME + ".foam").replace("\\", "/")
    logical_stub = "data/openfoam/%s/%s.foam" % (CASE_NAME, CASE_NAME)

    pv.ResetSession()
    view = pv.CreateRenderView()
    pv.SetActiveView(view)
    pv.AssignViewToLayout(view)
    view.ViewSize = [640, 480]
    reader = pv.OpenFOAMReader(registrationName=CASE_NAME + ".foam", FileName=stub)
    reader.MeshRegions = ["internalMesh"]
    reader.CellArrays = ["p", "U"]
    rep = pv.Show(reader, view)
    rep.SetRepresentationType("Surface With Edges")
    pv.ColorBy(rep, ("CELLS", "p"))
    lut = pv.GetColorTransferFunction("p")
    lut.RescaleTransferFunction(-1.0, 2.2)
    lut.AutomaticRescaleRangeMode = "Never"
    scene = pv.GetAnimationScene()
    scene.UpdateAnimationUsingDataTimeSteps()
    pv.ResetCamera(view)
    view.CameraPosition = [9.0, -7.0, 6.0]
    view.CameraFocalPoint = [2.0, 1.5, 1.0]
    view.CameraViewUp = [0.0, 0.0, 1.0]
    pv.Render(view)

    states = os.path.join(corpus, "states")
    state_path = os.path.join(states, STATE_NAME + ".pvsm")
    pv.SaveState(state_path)
    with open(state_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    text = text.replace(stub, logical_stub).replace(stub.replace("/", "\\"), logical_stub)
    with open(state_path, "w", encoding="utf-8") as handle:
        handle.write(text)
    if logical_stub not in text:
        raise SystemExit("the saved state does not reference the stub: %s" % state_path)

    manifest_path = os.path.join(corpus, "manifest.json")
    with open(manifest_path, "r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    data = set(manifest.get("data", []))
    data.update("data/openfoam/%s/%s" % (CASE_NAME, relative) for relative in written)
    manifest["data"] = sorted(data)
    if STATE_NAME + ".pvsm" not in manifest["states"]:
        manifest["states"].append(STATE_NAME + ".pvsm")
    manifest.setdefault("timesteps", {})[STATE_NAME] = TIMELINE
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)

    print("OpenFOAM fixture written: %d case files, state %s, ParaView %s" % (len(written), state_path, pv.GetParaViewSourceVersion()))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
