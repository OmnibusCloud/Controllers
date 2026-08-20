# -*- coding: utf-8 -*-
"""OmnibusCloud ParaView node benchmark: how many pixels per second this node renders.

    pvpython --force-offscreen-rendering --disable-registry benchmark_frames.py --task-file <task.json>

Builds a procedural scene in-process (no package, no network): a Wavelet of the requested extent
contoured at several values, clipped and sliced, coloured by the scalar — a representative
isosurface + geometry workload — then renders frames to PNG at the requested size while rotating the
camera. Every frame ALSO re-executes the contour+clip pipeline (one isosurface value alternates
between two fixed levels), because that is what a real render task pays in every process: without it
VTK caches the filters after the first frame and the loop times only rasterization + readback + PNG
encoding, which barely separates a 32-core node from a 2-core one (measured 48 vs 62 ms/frame; with
re-execution 95 vs 212 ms — the true hardware ratio). SaveScreenshot is included, so image readback
and encoding are part of the measurement, exactly like a task. The first frame(s) are warm-up
(initial pipeline execution, GL context); the timed loop runs until the target seconds elapse or the
frame cap is reached. Determinism: the scene, camera step and the two alternating isosurface sets are
fixed, so every node times the same frames. The status document carries frames, render seconds, the
render window class (vtkOSOpenGLRenderWindow = software), the ParaView version and the scene size;
the controller turns it into pixels/second, the unit its work estimate (ParaView.RenderFrame:
pixels + bytes/64) is expressed in, so the grid allocator can weigh nodes.

Exit codes: 0 ok, 1 failure (status.json says why), 2 usage.
"""

import argparse
import json
import os
import re
import sys
import time
import traceback

EXIT_OK = 0
EXIT_FAILURE = 1
EXIT_USAGE = 2


def load_task(path):
    with open(path, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    task = {
        "width": int(data.get("width", 512)),
        "height": int(data.get("height", 512)),
        "warmup_frames": int(data.get("warmup_frames", 1)),
        "target_seconds": float(data.get("target_seconds", 3.0)),
        "max_frames": int(data.get("max_frames", 120)),
        "extent": int(data.get("extent", 30)),
        "output_dir": data["output_dir"],
        "status_path": data["status_path"],
    }
    if task["width"] <= 0 or task["height"] <= 0 or task["max_frames"] <= 0:
        raise ValueError("width, height and max_frames must be positive")
    return task


def write_status(path, status):
    if not path:
        return
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as handle:
        json.dump(status, handle, indent=2, sort_keys=True)
    os.replace(tmp, path)


def paraview_version(simple):
    try:
        text = simple.GetParaViewSourceVersion()
        match = re.search(r"(\d+\.\d+\.\d+(?:[-.][0-9A-Za-z]+)*)", text or "")
        if match:
            return match.group(1)
        version = simple.GetParaViewVersion()
        return "%d.%d" % (version.major, version.minor)
    except Exception:
        return ""


BASE_ISOSURFACES = [120.0, 157.0, 200.0, 240.0]

# The first level alternates between base and base+ISO_STEP so every frame invalidates the contour
# (and the clip downstream) and the pipeline genuinely re-executes — a fixed, hardware-independent
# cycle of two near-identical workloads.
ISO_STEP = 1.0


def build_scene(simple, extent, width, height):
    wavelet = simple.Wavelet()
    wavelet.WholeExtent = [-extent, extent, -extent, extent, -extent, extent]
    contour = simple.Contour(Input=wavelet, ContourBy=["POINTS", "RTData"], Isosurfaces=list(BASE_ISOSURFACES))
    clip = simple.Clip(Input=contour)
    clip.ClipType = "Plane"
    clip.ClipType.Normal = [1.0, 0.3, 0.0]
    slice_ = simple.Slice(Input=wavelet)
    slice_.SliceType = "Plane"
    slice_.SliceType.Normal = [0.0, 0.0, 1.0]

    view = simple.CreateRenderView()
    view.ViewSize = [width, height]
    view.OrientationAxesVisibility = 0
    clip_display = simple.Show(clip, view)
    simple.ColorBy(clip_display, ("POINTS", "RTData"))
    slice_display = simple.Show(slice_, view)
    simple.ColorBy(slice_display, ("POINTS", "RTData"))
    simple.Show(wavelet, view).SetRepresentationType("Outline")
    simple.ResetCamera(view)
    return view, wavelet, contour


def main(argv):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--task-file", required=True)
    try:
        args = parser.parse_args(argv)
    except SystemExit:
        return EXIT_USAGE

    status = {"schema": 1, "ok": False, "stage": "load_task", "error": "", "frames": 0, "render_seconds": 0.0}
    status_path = None
    try:
        task = load_task(args.task_file)
        status_path = task["status_path"]
        status.update({"width": task["width"], "height": task["height"]})

        status["stage"] = "import"
        from paraview import simple
        status["paraview_version"] = paraview_version(simple)

        status["stage"] = "build"
        view, wavelet, contour = build_scene(simple, task["extent"], task["width"], task["height"])
        status["points"] = int(wavelet.GetDataInformation().GetNumberOfPoints())

        os.makedirs(task["output_dir"], exist_ok=True)
        output = os.path.join(task["output_dir"], "benchmark_frame.png")
        camera = view.GetActiveCamera()
        frame_counter = [0]

        def render_frame():
            # Flip the first isosurface every call: the counter starts the alternation at +ISO_STEP,
            # so the very first frame already differs from the build value and re-executes too.
            frame_counter[0] += 1
            contour.Isosurfaces = [BASE_ISOSURFACES[0] + (frame_counter[0] % 2) * ISO_STEP] + BASE_ISOSURFACES[1:]
            camera.Azimuth(360.0 / 24.0)
            simple.Render(view)
            simple.SaveScreenshot(output, view, ImageResolution=[task["width"], task["height"]])

        status["stage"] = "warmup"
        for _ in range(max(0, task["warmup_frames"])):
            render_frame()
        status["render_window"] = view.GetRenderWindow().GetClassName()

        status["stage"] = "measure"
        frames = 0
        started = time.perf_counter()
        while frames < task["max_frames"]:
            render_frame()
            frames += 1
            if time.perf_counter() - started >= task["target_seconds"]:
                break
        elapsed = time.perf_counter() - started

        status.update({"stage": "done", "ok": True, "frames": frames, "render_seconds": elapsed})
        status["output_bytes"] = os.path.getsize(output) if os.path.isfile(output) else 0
        write_status(status_path, status)
        return EXIT_OK
    except Exception as error:  # noqa: BLE001 - every failure is reported through the status document
        status["error"] = "%s: %s\n%s" % (type(error).__name__, error, traceback.format_exc()[-2000:])
        write_status(status_path, status)
        sys.stderr.write("benchmark_frames: %s\n" % status["error"])
        return EXIT_FAILURE


if __name__ == "__main__":
    code = main(sys.argv[1:])
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
