# -*- coding: utf-8 -*-
"""OmnibusCloud ParaView GPU probe: which render window and which OpenGL renderer this node gets.

    pvpython --force-offscreen-rendering --disable-registry gpu_probe.py --status-file <status.json>

Run once per node with VTK_DEFAULT_OPENGL_WINDOW set to the candidate window class (vtkEGLRenderWindow
for the GPU attempt): renders a trivial scene and reports the ACTUAL render window class and the
OpenGL renderer string. The controller accepts the candidate only when the renderer is real hardware —
Mesa's EGL can silently land on llvmpipe, which would report a GPU-looking window over a software
rasterizer. A crash or nonzero exit means the candidate is unusable (the caller falls back to OSMesa).

Exit codes: 0 ok, 1 failure (status.json says why when it could be written), 2 usage.
"""

import argparse
import json
import os
import sys
import traceback

EXIT_OK = 0
EXIT_FAILURE = 1
EXIT_USAGE = 2


def write_status(path, status):
    if not path:
        return
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as handle:
        json.dump(status, handle, indent=2, sort_keys=True)
    os.replace(tmp, path)


def main(argv):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--status-file", required=True)
    try:
        args = parser.parse_args(argv)
    except SystemExit:
        return EXIT_USAGE

    status = {"schema": 1, "ok": False, "stage": "import", "error": "", "render_window": "", "renderer": ""}
    try:
        from paraview import simple

        status["stage"] = "render"
        view = simple.CreateRenderView()
        view.ViewSize = [64, 48]
        sphere = simple.Sphere()
        simple.Show(sphere, view)
        simple.Render(view)

        window = view.GetRenderWindow()
        status["render_window"] = window.GetClassName()
        capabilities = ""
        try:
            capabilities = window.ReportCapabilities() or ""
        except Exception:
            pass
        for line in capabilities.splitlines():
            if "renderer string" in line.lower():
                status["renderer"] = line.split(":", 1)[1].strip()
                break

        status.update({"stage": "done", "ok": True})
        write_status(args.status_file, status)
        return EXIT_OK
    except Exception as error:  # noqa: BLE001 - every failure is reported through the status document
        status["error"] = "%s: %s\n%s" % (type(error).__name__, error, traceback.format_exc()[-2000:])
        write_status(args.status_file, status)
        sys.stderr.write("gpu_probe: %s\n" % status["error"])
        return EXIT_FAILURE


if __name__ == "__main__":
    code = main(sys.argv[1:])
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
