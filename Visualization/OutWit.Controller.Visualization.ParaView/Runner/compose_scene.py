#!/usr/bin/env pvpython
"""Controller-owned composer: ONE pvpython process that turns bare data into a saved ParaView state.

Invoked by the ParaView controller's ParaView.Compose activity (docs 06, part A) with a task file
written by the controller:

    pvpython --force-offscreen-rendering --disable-registry compose_scene.py --task-file <compose.json>

Contract:
  1. read the task file (snake_case JSON; every path is absolute and inside the task workspace);
  2. load the bundled reader, open the materialized data file through it;
  3. inspect the data: the arrays it offers, the timeline the reader reports;
  4. build the presentation from the task's bounded choices — representation, colouring by a named
     (or the first) array, a colour-map preset, the scalar bar — and bake ONE colour range;
  5. frame the camera: look from the requested direction, fit the union of the data bounds over the
     timesteps the task names (all/first/last; long timelines are sampled), so every frame the
     fleet later renders shares this framing;
  6. SaveState, then rewrite the data file's absolute path to its LOGICAL path — the state must never
     carry a node path — and refuse a state that still mentions the workspace;
  7. write the bounded machine-readable status document on EVERY exit path;
  8. exit non-zero on any discrepancy, leaving no state behind.

The state this saves is validated by the host exactly like a user-saved one (proxy allowlist,
logical paths, timeline) before any frame is rendered; this script therefore creates only the
proxies the allowlist knows (the reader, its representation, a render view, a lookup table, the
scalar bar, the animation scene / time keeper ParaView adds itself).

Stdlib + paraview only. Compatible with the Python ParaView bundles (3.9+ syntax).
"""

import argparse
import json
import math
import os
import sys
import time
import traceback

STATUS_SCHEMA = 1
MAX_ERROR_CHARS = 4000

STAGE_START = "start"
STAGE_LOAD_TASK = "load-task"
STAGE_IMPORT = "import-paraview"
STAGE_LOAD_PLUGIN = "load-plugin"
STAGE_OPEN = "open-data"
STAGE_INSPECT = "inspect"
STAGE_PRESENT = "present"
STAGE_FIT = "fit"
STAGE_SAVE = "save-state"
STAGE_REWRITE = "rewrite-paths"
STAGE_VERIFY = "verify-state"
STAGE_DONE = "done"

EXIT_OK = 0
EXIT_FAILURE = 1
EXIT_USAGE = 2
EXIT_POLICY = 3

ASSOCIATION_POINTS = "POINTS"
ASSOCIATION_CELLS = "CELLS"
REPRESENTATIONS = ("Surface", "Surface With Edges", "Wireframe")
FIT_ALL = "all"
FIT_LAST = "last"
FIT_FIRST = "first"

# Camera direction tokens → (view direction the camera looks FROM, view-up). The isometric framing
# looks from the (+1, +1, +1) octant with Z up, the classic engineering view.
CAMERA_DIRECTIONS = {
    "isometric": ((1.0, 1.0, 1.0), (0.0, 0.0, 1.0)),
    "+x": ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)),
    "-x": ((-1.0, 0.0, 0.0), (0.0, 0.0, 1.0)),
    "+y": ((0.0, 1.0, 0.0), (0.0, 0.0, 1.0)),
    "-y": ((0.0, -1.0, 0.0), (0.0, 0.0, 1.0)),
    "+z": ((0.0, 0.0, 1.0), (0.0, 1.0, 0.0)),
    "-z": ((0.0, 0.0, -1.0), (0.0, 1.0, 0.0)),
}


class ComposerError(Exception):
    """A discrepancy that aborts the composition without publishing a state."""

    def __init__(self, message, exit_code=EXIT_FAILURE):
        Exception.__init__(self, message)
        self.exit_code = exit_code


class Status(object):
    """The bounded status document (snake_case keys, mirrored by ParaViewComposeStatus)."""

    def __init__(self):
        self.data = {
            "schema": STATUS_SCHEMA,
            "ok": False,
            "stage": STAGE_START,
            "error": "",
            "paraview_version": "",
            "reader_version": "",
            "timestep_values": [],
            "point_arrays": [],
            "cell_arrays": [],
            "color_array": "",
            "color_association": "",
            "color_range": [],
            "bounds": [],
            "fit_samples": 0,
            "state_bytes": 0,
            "compose_seconds": 0.0,
        }

    def stage(self, name):
        self.data["stage"] = name

    def set(self, key, value):
        self.data[key] = value

    def fail(self, message):
        self.data["ok"] = False
        self.data["error"] = (message or "")[:MAX_ERROR_CHARS]

    def write(self, path):
        if not path:
            return
        try:
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(self.data, handle, indent=2, sort_keys=True)
        except Exception:
            pass


# ---------------------------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------------------------

def _norm(path):
    return os.path.normcase(os.path.realpath(os.path.abspath(path)))


def is_inside(path, root):
    """True when path resolves strictly inside root (after symlink resolution)."""
    root_n = _norm(root)
    path_n = _norm(path)
    if not root_n.endswith(os.sep):
        root_n += os.sep
    return path_n.startswith(root_n)


# ---------------------------------------------------------------------------------------------
# Task
# ---------------------------------------------------------------------------------------------

class Task(object):
    """The parsed task file."""

    REQUIRED = (
        "schema", "package_root", "work_dir", "state_path", "status_path",
        "data_path", "data_logical_path", "plugin_path",
    )

    def __init__(self, data):
        for key in self.REQUIRED:
            if key not in data:
                raise ComposerError("task file lacks '%s'" % key, EXIT_USAGE)
        if data["schema"] != 1:
            raise ComposerError("unsupported task file schema %r" % (data["schema"],), EXIT_USAGE)

        self.package_root = data["package_root"]
        self.work_dir = data["work_dir"]
        self.state_path = data["state_path"]
        self.status_path = data["status_path"]
        self.data_path = data["data_path"]
        self.data_logical_path = data["data_logical_path"]
        self.registration_name = str(data.get("registration_name") or os.path.basename(self.data_path))
        self.plugin_path = data["plugin_path"]
        self.color_array_name = str(data.get("color_array_name") or "")
        self.color_association = str(data.get("color_association") or ASSOCIATION_POINTS).upper()
        self.color_component = int(data.get("color_component", -1))
        self.colormap_preset = str(data.get("colormap_preset") or "")
        self.representation = str(data.get("representation") or "Surface")
        self.show_scalar_bar = bool(data.get("show_scalar_bar", True))
        self.camera_direction = str(data.get("camera_direction") or "isometric").lower()
        self.fit_to = str(data.get("fit_to") or FIT_ALL).lower()
        self.view_width = int(data.get("view_width", 1920) or 1920)
        self.view_height = int(data.get("view_height", 1080) or 1080)
        self.max_fit_samples = int(data.get("max_fit_samples", 25) or 25)

        if self.color_association not in (ASSOCIATION_POINTS, ASSOCIATION_CELLS):
            raise ComposerError("unsupported colour association '%s'" % self.color_association, EXIT_USAGE)
        if self.color_component < -1:
            raise ComposerError("colour component must be -1 (magnitude) or a zero-based index", EXIT_USAGE)
        if self.representation not in REPRESENTATIONS:
            raise ComposerError("unsupported representation '%s'" % self.representation, EXIT_USAGE)
        if self.camera_direction not in CAMERA_DIRECTIONS:
            raise ComposerError("unsupported camera direction '%s'" % self.camera_direction, EXIT_USAGE)
        if self.fit_to not in (FIT_ALL, FIT_LAST, FIT_FIRST):
            raise ComposerError("unsupported fit '%s'" % self.fit_to, EXIT_USAGE)
        if self.view_width < 1 or self.view_height < 1:
            raise ComposerError("view size must be positive", EXIT_USAGE)
        if not os.path.isdir(self.package_root):
            raise ComposerError("package root '%s' is not a directory" % self.package_root, EXIT_USAGE)
        if not os.path.isdir(self.work_dir):
            raise ComposerError("work dir '%s' is not a directory" % self.work_dir, EXIT_USAGE)
        if not os.path.isfile(self.data_path):
            raise ComposerError("data file '%s' does not exist" % self.data_path, EXIT_USAGE)
        if not is_inside(self.data_path, self.package_root):
            raise ComposerError("data file escapes the package root", EXIT_POLICY)
        if not is_inside(self.state_path, self.work_dir):
            raise ComposerError("state path escapes the work directory", EXIT_POLICY)
        if os.path.exists(self.state_path):
            raise ComposerError("state path '%s' already exists" % self.state_path, EXIT_POLICY)
        if not is_inside(self.status_path, self.work_dir):
            raise ComposerError("status path escapes the work directory", EXIT_POLICY)
        if not is_inside(self.plugin_path, self.work_dir):
            raise ComposerError("plugin path escapes the work directory", EXIT_POLICY)
        if not os.path.isfile(self.plugin_path):
            raise ComposerError("plugin file '%s' does not exist" % self.plugin_path, EXIT_USAGE)


def load_task(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except Exception as e:
        raise ComposerError("cannot read the task file: %s" % e, EXIT_USAGE)
    if not isinstance(data, dict):
        raise ComposerError("task file is not a JSON object", EXIT_USAGE)
    return Task(data)


# ---------------------------------------------------------------------------------------------
# ParaView
# ---------------------------------------------------------------------------------------------

def import_paraview():
    try:
        import paraview  # noqa: F401
        from paraview import simple  # noqa: F401
        from paraview import servermanager  # noqa: F401
    except Exception as e:
        raise ComposerError("cannot import paraview.simple: %s" % e)
    return simple, servermanager


class VtkErrorObserver(object):
    """Counts VTK error events so a data file the reader could not fully digest fails the
    composition loudly instead of producing a state around a half-read mesh."""

    def __init__(self):
        self.errors = []

    def install(self):
        try:
            from vtkmodules.vtkCommonCore import vtkOutputWindow
        except Exception:
            try:
                from paraview.vtk import vtkOutputWindow  # type: ignore
            except Exception:
                return False
        callback = self._on_error
        try:
            from vtkmodules.util.misc import calldata_type
            from vtkmodules.vtkCommonCore import VTK_STRING
            callback = calldata_type(VTK_STRING)(self._on_error_with_text)
        except Exception:
            pass
        try:
            window = vtkOutputWindow.GetInstance()
            window.AddObserver("ErrorEvent", callback)
            return True
        except Exception:
            return False

    def _on_error_with_text(self, caller, event, text):
        self._record(text)

    def _on_error(self, caller, event, *args):
        text = args[0] if args and isinstance(args[0], str) else ""
        self._record(text)

    def _record(self, text):
        try:
            self.errors.append((str(text) if text else "vtk error").strip()[:500])
        except Exception:
            self.errors.append("vtk error")


def paraview_version(simple):
    import re
    try:
        text = simple.GetParaViewSourceVersion()
        match = re.search(r"(\d+\.\d+\.\d+(?:[-.][0-9A-Za-z]+)*)", text or "")
        if match:
            return match.group(1)
        version = simple.GetParaViewVersion()
        return "%d.%d" % (version.major, version.minor)
    except Exception:
        return ""


def reader_version(plugin_path):
    try:
        with open(plugin_path, "r", encoding="utf-8") as handle:
            for line in handle:
                stripped = line.strip()
                if stripped.startswith("__version__"):
                    return stripped.split("=", 1)[1].strip().strip("\"'")
    except Exception:
        pass
    return ""


def load_plugin(simple, task):
    try:
        simple.LoadPlugin(task.plugin_path, remote=False, ns=globals())
    except Exception as e:
        raise ComposerError("cannot load the bundled reader: %s" % e)


def open_data(simple, task):
    """Instantiates the bundled reader on the data file, registered under the data file's name (the
    registration name the corpus states use, so the saved state looks like a user's)."""
    factory = getattr(simple, "OmnibusCloudFrdReader", None)
    if factory is None:
        factory = globals().get("OmnibusCloudFrdReader")
    if factory is None:
        raise ComposerError("the bundled reader registered no OmnibusCloudFrdReader proxy")
    try:
        reader = factory(registrationName=task.registration_name, FileName=task.data_path)
    except Exception as e:
        raise ComposerError("cannot open '%s' through the bundled reader: %s" % (task.data_logical_path, e))
    if reader is None:
        raise ComposerError("the bundled reader returned no proxy for '%s'" % task.data_logical_path)
    return reader


def timeline_of(reader):
    try:
        values = reader.TimestepValues
    except Exception:
        return []
    if values is None:
        return []
    if isinstance(values, (int, float)):
        return [float(values)]
    try:
        return [float(v) for v in values]
    except TypeError:
        return []


def array_names(data_information):
    names = []
    try:
        for index in range(data_information.GetNumberOfArrays()):
            names.append(data_information.GetArrayInformation(index).GetName())
    except Exception:
        pass
    return names


def inspect(simple, reader, task, status):
    """Updates the reader once and records the arrays and the timeline it offers."""
    times = timeline_of(reader)
    try:
        if times:
            reader.UpdatePipeline(times[-1])
        else:
            reader.UpdatePipeline()
    except Exception as e:
        raise ComposerError("the reader could not execute on '%s': %s" % (task.data_logical_path, e))
    info = reader.GetDataInformation()
    points = array_names(info.GetPointDataInformation())
    cells = array_names(info.GetCellDataInformation())
    status.set("point_arrays", points)
    status.set("cell_arrays", cells)
    try:
        if info.GetNumberOfPoints() <= 0:
            raise ComposerError("'%s' contains no points" % task.data_logical_path, EXIT_POLICY)
    except AttributeError:
        pass
    return times, points, cells


def choose_color_array(task, points, cells):
    """The array to colour by: the named one (which must exist in its association) or the first
    point array, then the first cell array; None for a solid colour."""
    if task.color_array_name:
        available = points if task.color_association == ASSOCIATION_POINTS else cells
        if task.color_array_name not in available:
            raise ComposerError(
                "'%s' carries no %s array '%s' (point arrays: %s; cell arrays: %s)"
                % (task.data_logical_path, task.color_association.lower(), task.color_array_name,
                   ", ".join(points) or "none", ", ".join(cells) or "none"),
                EXIT_POLICY)
        return task.color_association, task.color_array_name
    if points:
        return ASSOCIATION_POINTS, points[0]
    if cells:
        return ASSOCIATION_CELLS, cells[0]
    return None, ""


def present(simple, reader, view, task, association, array, status):
    """Show → representation type → colouring → preset → scalar bar."""
    rep = simple.Show(reader, view)
    try:
        rep.SetRepresentationType(task.representation)
    except Exception as e:
        raise ComposerError("cannot set representation '%s': %s" % (task.representation, e))
    if array:
        try:
            simple.ColorBy(rep, (association, array))
        except Exception as e:
            raise ComposerError("cannot colour by %s/%s: %s" % (association, array, e))
        lut = simple.GetColorTransferFunction(array)
        try:
            if task.color_component >= 0:
                lut.VectorMode = "Component"
                lut.VectorComponent = task.color_component
            else:
                lut.VectorMode = "Magnitude"
        except Exception:
            pass
        if task.colormap_preset:
            try:
                lut.ApplyPreset(task.colormap_preset, True)
            except Exception as e:
                raise ComposerError("cannot apply colour-map preset '%s': %s" % (task.colormap_preset, e))
        if task.show_scalar_bar:
            try:
                rep.SetScalarBarVisibility(view, True)
            except Exception:
                pass
        status.set("color_array", array)
        status.set("color_association", association)
        return rep, lut
    status.set("color_array", "")
    status.set("color_association", "")
    return rep, None


def fit_samples(times, fit_to, max_samples):
    """The timesteps the fit inspects: none for static data, one for first/last, an even sample of
    the timeline (always including the last) for 'all'."""
    if not times:
        return []
    if fit_to == FIT_FIRST:
        return [times[0]]
    if fit_to == FIT_LAST:
        return [times[-1]]
    count = len(times)
    if count <= max_samples:
        return list(times)
    step = (count - 1) / float(max(1, max_samples - 1))
    picked = []
    for index in range(max_samples):
        position = int(round(index * step))
        if position >= count:
            position = count - 1
        if not picked or picked[-1] != times[position]:
            picked.append(times[position])
    if picked[-1] != times[-1]:
        picked.append(times[-1])
    return picked


def union_bounds(accumulated, bounds):
    if bounds is None or len(bounds) != 6 or any(not math.isfinite(v) for v in bounds) or bounds[0] > bounds[1]:
        return accumulated
    if accumulated is None:
        return list(bounds)
    return [
        min(accumulated[0], bounds[0]), max(accumulated[1], bounds[1]),
        min(accumulated[2], bounds[2]), max(accumulated[3], bounds[3]),
        min(accumulated[4], bounds[4]), max(accumulated[5], bounds[5]),
    ]


def array_range(reader, association, array, component):
    try:
        info = reader.GetDataInformation()
        collection = info.GetPointDataInformation() if association == ASSOCIATION_POINTS else info.GetCellDataInformation()
        array_info = collection.GetArrayInformation(array)
        if array_info is None:
            return None
        if component >= 0:
            if component >= array_info.GetNumberOfComponents():
                raise ComposerError("array '%s' has %d component(s); component %d does not exist"
                                    % (array, array_info.GetNumberOfComponents(), component), EXIT_POLICY)
            low, high = array_info.GetComponentRange(component)
        else:
            low, high = array_info.GetComponentRange(-1 if array_info.GetNumberOfComponents() > 1 else 0)
        if not (math.isfinite(low) and math.isfinite(high)):
            return None
        return [float(low), float(high)]
    except ComposerError:
        raise
    except Exception:
        return None


def fit(simple, reader, view, lut, task, times, association, array, status):
    """Frames the camera from the requested direction over the union of the data bounds at the
    sampled timesteps, and bakes the matching colour range so every rendered frame shares both."""
    samples = fit_samples(times, task.fit_to, task.max_fit_samples)
    bounds = None
    color_low = None
    color_high = None
    for value in (samples or [None]):
        try:
            if value is None:
                reader.UpdatePipeline()
            else:
                reader.UpdatePipeline(value)
        except Exception as e:
            raise ComposerError("the reader could not execute at time %r: %s" % (value, e))
        try:
            bounds = union_bounds(bounds, reader.GetDataInformation().GetBounds())
        except Exception:
            pass
        if array:
            rng = array_range(reader, association, array, task.color_component)
            if rng is not None:
                color_low = rng[0] if color_low is None else min(color_low, rng[0])
                color_high = rng[1] if color_high is None else max(color_high, rng[1])

    status.set("fit_samples", len(samples))
    if bounds is None:
        raise ComposerError("'%s' has no finite bounds to frame" % task.data_logical_path, EXIT_POLICY)
    status.set("bounds", [float(v) for v in bounds])

    direction, view_up = CAMERA_DIRECTIONS[task.camera_direction]
    center = [(bounds[0] + bounds[1]) / 2.0, (bounds[2] + bounds[3]) / 2.0, (bounds[4] + bounds[5]) / 2.0]
    extent = max(bounds[1] - bounds[0], bounds[3] - bounds[2], bounds[5] - bounds[4], 1e-6)
    view.ViewSize = [task.view_width, task.view_height]
    view.CameraFocalPoint = center
    view.CameraPosition = [center[0] + direction[0] * extent * 3.0, center[1] + direction[1] * extent * 3.0, center[2] + direction[2] * extent * 3.0]
    view.CameraViewUp = list(view_up)
    # Fit the union bounds (a deformed last step must not be clipped). ResetCamera(bounds) is the
    # proxy API; older wrappers only fit the current data — then the last sampled step is what it fits.
    fitted = False
    try:
        view.ResetCamera(list(bounds))
        fitted = True
    except Exception:
        fitted = False
    if not fitted:
        try:
            simple.ResetCamera(view)
        except Exception as e:
            raise ComposerError("cannot fit the camera: %s" % e)

    if lut is not None and color_low is not None and color_high is not None:
        if color_high <= color_low:
            color_high = color_low + 1e-9 * max(1.0, abs(color_low))
        try:
            lut.RescaleTransferFunction(color_low, color_high)
            lut.AutomaticRescaleRangeMode = "Never"
        except Exception:
            pass
        status.set("color_range", [color_low, color_high])


def bake_timeline(simple, times, task, status):
    """Lets the animation scene adopt the reader's timeline (the TimeKeeper the state carries) and
    parks the scene at the fitted time; reports the TimeKeeper's values — the list the host will
    compare the saved state against."""
    scene = simple.GetAnimationScene()
    try:
        scene.UpdateAnimationUsingDataTimeSteps()
    except Exception:
        pass
    keeper = simple.GetTimeKeeper()
    try:
        keeper_times = [float(v) for v in keeper.TimestepValues] if keeper is not None else []
    except Exception:
        keeper_times = []
    if not keeper_times:
        keeper_times = list(times)
    if keeper_times:
        park = keeper_times[0] if task.fit_to == FIT_FIRST else keeper_times[-1]
        try:
            scene.AnimationTime = park
        except Exception:
            pass
    status.set("timestep_values", keeper_times)


def save_state(simple, task):
    try:
        simple.SaveState(task.state_path)
    except Exception as e:
        raise ComposerError("cannot save the state: %s" % e)
    if not os.path.isfile(task.state_path) or os.path.getsize(task.state_path) == 0:
        raise ComposerError("SaveState wrote no state file")


def rewrite_paths(task):
    """Replaces the data file's absolute path (both separator styles) with its logical path."""
    with open(task.state_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    absolute = task.data_path
    variants = {absolute, absolute.replace("\\", "/"), absolute.replace("/", "\\")}
    rewritten = 0
    for variant in sorted(variants, key=len, reverse=True):
        if variant and variant in text:
            rewritten += text.count(variant)
            text = text.replace(variant, task.data_logical_path)
    if rewritten == 0:
        raise ComposerError("the saved state does not reference the data file '%s'" % task.data_path)
    with open(task.state_path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return rewritten


def verify_state(task):
    """The saved state must reference the logical path and nothing inside the workspace."""
    with open(task.state_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    if task.data_logical_path not in text:
        raise ComposerError("the saved state does not reference the logical path '%s'" % task.data_logical_path)
    for root in (task.work_dir, task.package_root):
        for variant in {root, root.replace("\\", "/"), root.replace("/", "\\")}:
            if variant and variant in text:
                raise ComposerError("the saved state still mentions the workspace path '%s'" % variant, EXIT_POLICY)
    if "<ServerManagerState" not in text:
        raise ComposerError("the saved state carries no ServerManagerState element")
    return os.path.getsize(task.state_path)


# ---------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------

def run(task, status):
    started = time.time()

    status.stage(STAGE_IMPORT)
    simple, servermanager = import_paraview()
    status.set("paraview_version", paraview_version(simple))
    simple._DisableFirstRenderCameraReset()
    observer = VtkErrorObserver()
    observer.install()

    status.stage(STAGE_LOAD_PLUGIN)
    load_plugin(simple, task)
    status.set("reader_version", reader_version(task.plugin_path))

    status.stage(STAGE_OPEN)
    # A fresh, explicitly registered view: SaveState saves the views of the "views" collection.
    view = simple.CreateRenderView()
    simple.SetActiveView(view)
    try:
        simple.AssignViewToLayout(view)
    except Exception:
        pass
    reader = open_data(simple, task)

    status.stage(STAGE_INSPECT)
    times, points, cells = inspect(simple, reader, task, status)
    association, array = choose_color_array(task, points, cells)

    status.stage(STAGE_PRESENT)
    _rep, lut = present(simple, reader, view, task, association, array, status)

    status.stage(STAGE_FIT)
    fit(simple, reader, view, lut, task, times, association, array, status)
    bake_timeline(simple, times, task, status)

    if observer.errors:
        raise ComposerError("VTK reported %d error(s) while reading '%s'; first: %s"
                            % (len(observer.errors), task.data_logical_path, observer.errors[0]))

    status.stage(STAGE_SAVE)
    save_state(simple, task)

    status.stage(STAGE_REWRITE)
    rewrite_paths(task)

    status.stage(STAGE_VERIFY)
    status.set("state_bytes", verify_state(task))
    status.set("compose_seconds", round(time.time() - started, 3))

    status.stage(STAGE_DONE)
    status.set("ok", True)


def main(argv):
    parser = argparse.ArgumentParser(prog="compose_scene.py", add_help=True)
    parser.add_argument("--task-file", required=True, help="path of the compose task JSON written by the controller")
    try:
        args = parser.parse_args(argv)
    except SystemExit:
        return EXIT_USAGE

    status = Status()
    status_path = None
    state_path = None
    exit_code = EXIT_FAILURE
    try:
        status.stage(STAGE_LOAD_TASK)
        task = load_task(args.task_file)
        status_path = task.status_path
        state_path = task.state_path
        run(task, status)
        exit_code = EXIT_OK
    except ComposerError as e:
        status.fail(str(e))
        exit_code = e.exit_code
    except Exception as e:  # noqa: BLE001 — every exception is a discrepancy
        status.fail("%s: %s\n%s" % (type(e).__name__, e, traceback.format_exc()[-2000:]))
        exit_code = EXIT_FAILURE
    finally:
        if exit_code != EXIT_OK and state_path and os.path.isfile(state_path):
            try:
                os.remove(state_path)
            except Exception:
                pass
        status.write(status_path)

    if exit_code != EXIT_OK:
        sys.stderr.write("compose_scene: %s\n" % status.data.get("error", "failed"))
    return exit_code


if __name__ == "__main__":
    code = main(sys.argv[1:])
    # Leave through os._exit like the render runner: the state and status are on disk, and the
    # interpreter shutdown (session teardown, the OSMesa thread pool) has deadlocked on Linux.
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
