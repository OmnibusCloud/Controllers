"""OmnibusCloud ParaView task runner (controller-owned, never user-supplied).

Invoked by the ParaView controller's RenderFrame / RenderFrameBatch adapters as

    pvpython --force-offscreen-rendering --disable-registry render_task.py --task-file <task.json>

with an allowlisted environment and cwd = the task's package root. Everything the run needs is
in the task file (see ParaViewRunnerTask on the controller side, schema 2): the materialized
state, the package root every logical path resolves under, the view, the output size/format, the
optional bundled reader to load, the effective proxy allowlist plus the blocked proxy/property
lists for the post-load runtime check, and the OUTPUTS to render - one for a single-frame task,
a chunk for a batch (FrameBatch, docs 03, section 27, item 2). The state loads and validates
once per process; every output then selects its timestep, moves the camera from the state's
captured framing, renders to its own file and verifies it. A batch is all-or-nothing: one
failed output fails the process and nothing of it is published.

Responsibilities (docs 03, section 9):
  1. touch no user configuration and no plugin path other than the controller-owned one;
  2. load only the bundled reader, from the task's plugin path;
  3. resolve the state's logical file references under the package root (the state never carries
     a client path): a single-valued reference must be a materialized package file, a file series
     must have at least one, and any VTK error during load/render fails the task;
  4. select the declared view, then per output its timestep;
  4a. move the state's camera by the output's camera move (docs 03, section 27, item 1; docs 06,
      part B): revolve it by the azimuth about the orbit axis through the focal point, raise it by
      the elevation about its right axis, scale its distance to the focal point by the dolly
      factor — an output-side transform from the captured framing, never a state edit; the
      captured framing is restored before every further output;
  5. apply only controller-owned output overrides (size, transparency);
  6. render every output through SaveScreenshot;
  7. write the bounded machine-readable status document (per-output verdicts included) on EVERY
     exit path;
  8. exit non-zero on any discrepancy, publishing nothing.

Stdlib + paraview only. Compatible with the Python ParaView bundles (3.9+ syntax).
"""

import argparse
import json
import math
import os
import re
import sys
import threading
import time
import traceback
import xml.etree.ElementTree as ET

STATUS_SCHEMA = 2
TASK_SCHEMA = 2
MAX_ERROR_CHARS = 4000
DEFAULT_MAX_STATE_BYTES = 32 * 1024 * 1024
DEFAULT_MAX_LOGICAL_PATH_CHARS = 1024
INVALID_SEGMENT_CHARS = set('<>:"|?*')

STAGE_START = "start"
STAGE_LOAD_TASK = "load-task"
STAGE_RESOLVE_STATE = "resolve-state"
STAGE_IMPORT = "import-paraview"
STAGE_LOAD_PLUGIN = "load-plugin"
STAGE_LOAD_STATE = "load-state"
STAGE_VALIDATE = "validate"
STAGE_SELECT = "select"
STAGE_ORBIT = "orbit"
STAGE_RENDER = "render"
STAGE_VERIFY = "verify-output"
STAGE_DONE = "done"

EXIT_OK = 0
EXIT_FAILURE = 1
EXIT_USAGE = 2
EXIT_POLICY = 3

# Turntable axis tokens (ParaViewCameraAxes on the controller side): the state's camera view-up,
# or a world axis the camera revolves about rigidly.
CAMERA_AXIS_VIEW_UP = "view-up"
CAMERA_AXIS_VECTORS = {"x": (1.0, 0.0, 0.0), "y": (0.0, 1.0, 0.0), "z": (0.0, 0.0, 1.0)}

# The proxy groups a state can populate — the post-load check covers exactly these. Session-level
# groups (settings, *_prototypes, materials) are created by the runtime itself, are not state
# content, and are not part of the allowlist contract.
VALIDATED_PROXY_GROUPS = (
    "sources", "representations", "views", "lookup_tables", "piecewise_functions",
    "transfer_2d_functions", "animation", "misc", "implicit_functions", "textures",
    "extended_sources", "annotations", "additional_lights", "layouts", "scalar_bars",
)


class RunnerError(Exception):
    """A discrepancy that aborts the task without publishing output."""

    def __init__(self, message, exit_code=EXIT_FAILURE):
        Exception.__init__(self, message)
        self.exit_code = exit_code


class Status(object):
    """The bounded status document (snake_case keys, mirrored by ParaViewRunnerStatus)."""

    def __init__(self):
        self.data = {
            "schema": STATUS_SCHEMA,
            "ok": False,
            "stage": STAGE_START,
            "error": "",
            "paraview_version": "",
            "reader_version": "",
            "proxy_count": 0,
            "render_seconds": 0.0,
            "width": 0,
            "height": 0,
            "backend": "",
            "vtk_errors": 0,
            "missing_references": 0,
            "outputs": [],
        }

    def stage(self, name):
        self.data["stage"] = name

    def output(self, index, ok, stage, error, render_seconds):
        """Records one output's verdict (schema 2); a batch's task-level ok needs every output ok."""
        self.data["outputs"].append({
            "index": int(index),
            "ok": bool(ok),
            "stage": stage,
            "error": (error or "")[:MAX_ERROR_CHARS],
            "render_seconds": round(float(render_seconds), 3),
        })

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
            # A status file that cannot be written is reported through the exit code alone.
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


_DRIVE = re.compile(r"^[A-Za-z]:")
_URI = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*://")


def looks_like_path(value):
    """The shared host/runner heuristic (ParaViewLogicalPath.LooksLikePath)."""
    if not value:
        return False
    return ("/" in value) or ("\\" in value) or bool(_DRIVE.match(value)) or bool(_URI.match(value))


def check_logical_path(value, max_chars=DEFAULT_MAX_LOGICAL_PATH_CHARS):
    """The shared host/runner logical path rules (ParaViewLogicalPath.Check). Returns None or a violation."""
    if not value:
        return "logical path is empty"
    if len(value) > max_chars:
        return "logical path exceeds %d characters" % max_chars
    if any(ord(c) < 32 or ord(c) == 127 for c in value):
        return "logical path contains control characters"
    if "\\" in value:
        return "logical path '%s' must use '/' separators" % value
    if value.startswith("/"):
        return "logical path '%s' is absolute" % value
    if _DRIVE.match(value):
        return "logical path '%s' starts with a drive letter" % value
    if _URI.match(value):
        return "logical path '%s' is a URI" % value
    for segment in value.split("/"):
        if segment == "":
            return "logical path '%s' contains an empty segment" % value
        if segment == "..":
            return "logical path '%s' traverses upward" % value
        if segment == ".":
            return "logical path '%s' contains a '.' segment" % value
        if segment.endswith(" ") or segment.endswith("."):
            return "logical path '%s' has a segment ending in a space or dot" % value
        if any(c in INVALID_SEGMENT_CHARS for c in segment):
            return "logical path '%s' contains a character not portable across platforms" % value
    return None


# ---------------------------------------------------------------------------------------------
# Task
# ---------------------------------------------------------------------------------------------

class Output(object):
    """One output of the task (schema 2): its timestep, camera move and file."""

    REQUIRED = ("output_path", "timestep_index")

    def __init__(self, data, position):
        if not isinstance(data, dict):
            raise RunnerError("output %d is not a JSON object" % position, EXIT_USAGE)
        for key in self.REQUIRED:
            if key not in data:
                raise RunnerError("output %d lacks '%s'" % (position, key), EXIT_USAGE)
        self.index = int(data.get("index", position))
        self.task_id = data.get("task_id", "")
        self.output_path = data["output_path"]
        self.timestep_index = int(data["timestep_index"])
        self.time_value = data.get("time_value", None)
        self.camera_azimuth = float(data.get("camera_azimuth", 0.0) or 0.0)
        self.camera_axis = str(data.get("camera_axis", CAMERA_AXIS_VIEW_UP) or CAMERA_AXIS_VIEW_UP).lower()
        self.camera_elevation = float(data.get("camera_elevation", 0.0) or 0.0)
        self.camera_dolly = float(data.get("camera_dolly", 1.0) or 1.0)

        if not math.isfinite(self.camera_azimuth):
            raise RunnerError("output %d: camera azimuth must be a finite number of degrees" % self.index, EXIT_USAGE)
        if not math.isfinite(self.camera_elevation):
            raise RunnerError("output %d: camera elevation must be a finite number of degrees" % self.index, EXIT_USAGE)
        if not math.isfinite(self.camera_dolly) or self.camera_dolly <= 0.0:
            raise RunnerError("output %d: camera dolly factor must be a positive finite number" % self.index, EXIT_USAGE)
        if self.camera_axis != CAMERA_AXIS_VIEW_UP and self.camera_axis not in CAMERA_AXIS_VECTORS:
            raise RunnerError("output %d: unsupported camera axis '%s'" % (self.index, self.camera_axis), EXIT_USAGE)

    def moves_camera(self):
        return self.camera_azimuth != 0.0 or self.camera_elevation != 0.0 or self.camera_dolly != 1.0


class Task(object):
    """The parsed task file: the shared inputs and the outputs to render in order."""

    REQUIRED = (
        "schema", "state_path", "package_root", "work_dir", "status_path",
        "view_id", "width", "height", "format", "outputs",
    )

    def __init__(self, data):
        for key in self.REQUIRED:
            if key not in data:
                raise RunnerError("task file lacks '%s'" % key, EXIT_USAGE)
        if data["schema"] != TASK_SCHEMA:
            raise RunnerError("unsupported task file schema %r" % (data["schema"],), EXIT_USAGE)

        self.task_id = data.get("task_id", "")
        self.state_path = data["state_path"]
        self.package_root = data["package_root"]
        self.work_dir = data["work_dir"]
        self.status_path = data["status_path"]
        self.view_id = data["view_id"]
        self.width = int(data["width"])
        self.height = int(data["height"])
        self.format = str(data["format"]).lower()
        self.transparent_background = bool(data.get("transparent_background", False))
        self.plugin_path = data.get("plugin_path", None)
        self.allowed_proxies = set(data.get("allowed_proxies", []))
        self.blocked_proxy_types = set(data.get("blocked_proxy_types", []))
        self.blocked_property_names = set(data.get("blocked_property_names", []))
        self.file_property_names = set(data.get("file_property_names", []))
        self.file_reference_groups = set(data.get("file_reference_groups", []))
        self.max_state_bytes = int(data.get("max_state_bytes", 0) or DEFAULT_MAX_STATE_BYTES)
        self.max_logical_path_chars = int(data.get("max_logical_path_chars", 0) or DEFAULT_MAX_LOGICAL_PATH_CHARS)

        if self.width < 1 or self.height < 1:
            raise RunnerError("output dimensions must be positive", EXIT_USAGE)
        if self.format not in ("png", "jpeg"):
            raise RunnerError("unsupported output format '%s'" % self.format, EXIT_USAGE)
        outputs = data["outputs"]
        if not isinstance(outputs, list) or not outputs:
            raise RunnerError("task file lists no outputs", EXIT_USAGE)
        self.outputs = [Output(entry, position) for position, entry in enumerate(outputs)]
        if not os.path.isdir(self.package_root):
            raise RunnerError("package root '%s' is not a directory" % self.package_root, EXIT_USAGE)
        if not os.path.isdir(self.work_dir):
            raise RunnerError("work dir '%s' is not a directory" % self.work_dir, EXIT_USAGE)
        if not os.path.isfile(self.state_path):
            raise RunnerError("state file '%s' does not exist" % self.state_path, EXIT_USAGE)
        seen = set()
        for output in self.outputs:
            if not is_inside(output.output_path, self.work_dir):
                raise RunnerError("output %d: output path escapes the work directory" % output.index, EXIT_POLICY)
            key = _norm(output.output_path)
            if key in seen:
                raise RunnerError("output %d: output path is used twice in the task" % output.index, EXIT_POLICY)
            seen.add(key)
        if not is_inside(self.status_path, self.work_dir):
            raise RunnerError("status path escapes the work directory", EXIT_POLICY)
        if self.plugin_path is not None:
            if not is_inside(self.plugin_path, self.work_dir):
                raise RunnerError("plugin path escapes the work directory", EXIT_POLICY)
            if not os.path.isfile(self.plugin_path):
                raise RunnerError("plugin file '%s' does not exist" % self.plugin_path, EXIT_USAGE)

    def is_file_property(self, group, name, has_file_domain=False):
        """The shared host/runner rule (ParaViewProxyPolicy.IsFileProperty): a file-reading group and a
        known file property name, a *FileName(s) name, or ParaView's files domain. Values are never
        inspected — a Calculator function "p/rho" is not a path."""
        if group not in self.file_reference_groups:
            return False
        if has_file_domain:
            return True
        return name in self.file_property_names or name.endswith("FileName") or name.endswith("FileNames")


def load_task(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except Exception as e:
        raise RunnerError("cannot read task file: %s" % e, EXIT_USAGE)
    if not isinstance(data, dict):
        raise RunnerError("task file is not a JSON object", EXIT_USAGE)
    return Task(data)


# ---------------------------------------------------------------------------------------------
# State resolution (logical paths -> package root) — before ParaView touches the file
# ---------------------------------------------------------------------------------------------

def resolve_state(task):
    """Rewrites every file reference of the state to its materialized path under the package root.

    Refuses DTD/entity constructs, oversized states, non-logical references, and references to
    files the package did not materialize. Returns the path of the resolved state copy (inside the
    work directory) and the number of references rewritten.
    """
    size = os.path.getsize(task.state_path)
    if size > task.max_state_bytes:
        raise RunnerError("state is %d bytes, over the %d byte limit" % (size, task.max_state_bytes), EXIT_POLICY)

    with open(task.state_path, "rb") as handle:
        raw = handle.read()
    head = raw[:4096].lower()
    if b"<!doctype" in head or b"<!entity" in raw.lower():
        raise RunnerError("state declares a DTD or entities", EXIT_POLICY)

    try:
        root = ET.fromstring(raw)
    except ET.ParseError as e:
        raise RunnerError("state is not well-formed XML: %s" % e, EXIT_POLICY)

    # The GUI saves <ParaView>, pvpython saves <GenericParaViewApplication>; the contract is the
    # ServerManagerState child, not the root tag.
    states_found = root.findall("ServerManagerState")
    if not states_found:
        raise RunnerError("state carries no <ServerManagerState> element", EXIT_POLICY)

    rewritten = 0
    missing = []
    for state in states_found:
        definitions = state.find("CustomProxyDefinitions")
        if definitions is not None and len(list(definitions)) > 0:
            raise RunnerError("state embeds custom proxy definitions", EXIT_POLICY)
        for proxy in state.findall("Proxy"):
            group = proxy.get("group", "")
            ptype = proxy.get("type", "")
            if ptype in task.blocked_proxy_types:
                raise RunnerError("state instantiates blocked proxy '%s/%s'" % (group, ptype), EXIT_POLICY)
            for prop in proxy.findall("Property"):
                name = prop.get("name", "")
                elements = prop.findall("Element")
                values = [e.get("value", "") for e in elements]
                if name in task.blocked_property_names and any(v.strip() for v in values):
                    raise RunnerError("proxy '%s/%s' carries executable property '%s'" % (group, ptype, name), EXIT_POLICY)
                has_file_domain = any(d.get("name") == "files" for d in prop.findall("Domain"))
                if not task.is_file_property(group, name, has_file_domain):
                    continue
                present = 0
                referenced = 0
                for element in elements:
                    value = element.get("value", "")
                    if not value:
                        continue
                    referenced += 1
                    violation = check_logical_path(value, task.max_logical_path_chars)
                    if violation is not None:
                        raise RunnerError("proxy '%s/%s' property '%s': %s" % (group, ptype, name, violation), EXIT_POLICY)
                    target = os.path.join(task.package_root, *value.split("/"))
                    if not is_inside(target, task.package_root):
                        raise RunnerError("reference '%s' escapes the package root" % value, EXIT_POLICY)
                    if os.path.isfile(target):
                        present += 1
                    else:
                        missing.append(value)
                    element.set("value", target)
                    rewritten += 1
                # A single-valued reference must be materialized. A multi-valued one (a file series)
                # legitimately lists every file of the series while this task carries only its own
                # piece(s): at least one must exist; whether ParaView needs another is decided by the
                # VTK error observer at load/render time, never by a blank frame.
                if referenced > 0 and present == 0:
                    raise RunnerError("proxy '%s/%s' property '%s' references no materialized package file (%s)" % (group, ptype, name, ", ".join(v.get("value", "") for v in elements[:3])), EXIT_POLICY)
                if referenced == 1 and present != 1:
                    raise RunnerError("reference '%s' is not a materialized package file" % elements[0].get("value", ""), EXIT_POLICY)

    resolved_path = os.path.join(task.work_dir, "state.resolved.pvsm")
    ET.ElementTree(root).write(resolved_path, encoding="utf-8", xml_declaration=True)
    return resolved_path, rewritten, missing


# ---------------------------------------------------------------------------------------------
# ParaView
# ---------------------------------------------------------------------------------------------

def import_paraview():
    try:
        import paraview  # noqa: F401
        from paraview import simple  # noqa: F401
        from paraview import servermanager  # noqa: F401
    except Exception as e:
        raise RunnerError("cannot import paraview.simple: %s" % e)
    return simple, servermanager


class VtkErrorObserver(object):
    """Counts VTK error events (reader failures, missing files, GL failures) so a task that
    ParaView could not execute fully fails loudly instead of publishing a blank or partial frame."""

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
            # The error text travels as call data; VTK hands it to Python only when the observer
            # declares the call data type.
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
    if not plugin_path:
        return ""
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
    if not task.plugin_path:
        return
    try:
        simple.LoadPlugin(task.plugin_path, remote=False, ns=globals())
    except Exception as e:
        raise RunnerError("cannot load the bundled reader: %s" % e)


def runtime_validate(task, servermanager):
    """Post-load check (docs 03, section 8.2): only allowlisted proxies exist, no blocked proxy or
    property slipped through an unexpected encoding, every reader input is a package file."""
    pxm = servermanager.ProxyManager()
    count = 0
    proxies = []
    for group in VALIDATED_PROXY_GROUPS:
        try:
            for (_, _), proxy in pxm.GetProxiesInGroup(group).items():
                proxies.append((group, proxy))
        except Exception:
            continue

    for group, proxy in proxies:
        count += 1
        xml_group = proxy.GetXMLGroup() or group
        xml_name = proxy.GetXMLName() or ""
        key = "%s/%s" % (xml_group, xml_name)
        if xml_name in task.blocked_proxy_types:
            raise RunnerError("blocked proxy '%s' exists after load" % key, EXIT_POLICY)
        if task.allowed_proxies and key not in task.allowed_proxies:
            raise RunnerError("proxy '%s' exists after load but is not allowlisted" % key, EXIT_POLICY)
        if xml_group in task.file_reference_groups:
            for name in proxy.ListProperties():
                try:
                    value = proxy.GetPropertyValue(name)
                except Exception:
                    continue
                values = value if isinstance(value, (list, tuple)) else [value]
                values = [v for v in values if isinstance(v, str)]
                if not values:
                    continue
                if name in task.blocked_property_names and any(v.strip() for v in values):
                    raise RunnerError("proxy '%s' carries executable property '%s' after load" % (key, name), EXIT_POLICY)
                if task.is_file_property(xml_group, name, has_file_list_domain(proxy, name)):
                    for v in values:
                        if v and not is_inside(v, task.package_root):
                            raise RunnerError("proxy '%s' property '%s' references '%s' outside the package root" % (key, name, v), EXIT_POLICY)
    return count


def has_file_list_domain(proxy, name):
    """Whether a loaded proxy property carries ParaView's file list domain."""
    try:
        prop = proxy.GetProperty(name)
        sm = getattr(prop, "SMProperty", None)
        if sm is None:
            return False
        return sm.FindDomain("vtkSMFileListDomain") is not None
    except Exception:
        return False


def find_view(simple, servermanager, view_id):
    view = None
    try:
        view = simple.FindView(view_id)
    except Exception:
        view = None
    if view is None:
        try:
            proxy = servermanager.ProxyManager().GetProxy("views", view_id)
            if proxy is not None:
                view = servermanager._getPyProxy(proxy)
        except Exception:
            view = None
    if view is None:
        raise RunnerError("view '%s' is not registered in the loaded state" % view_id, EXIT_POLICY)
    return view


def select_timestep(simple, output, view):
    """Selects the output's timestep. The live TimeKeeper is authoritative when it carries a timeline:
    the index must be in range and agree with the output's time value. When it carries none (a
    static scene, or a runtime that does not expose the timeline) the output's time value, resolved
    host-side from the producer's timeline, is applied as the animation time."""
    keeper = simple.GetTimeKeeper()
    try:
        times = list(keeper.TimestepValues) if keeper is not None else []
    except Exception:
        times = []
    if times:
        if output.timestep_index < 0 or output.timestep_index >= len(times):
            raise RunnerError("timestep index %d is outside the timeline of %d timestep(s)" % (output.timestep_index, len(times)), EXIT_POLICY)
        value = float(times[output.timestep_index])
        if output.time_value is not None and abs(float(output.time_value) - value) > 1e-9 * max(1.0, abs(value)):
            raise RunnerError("timestep index %d has time %r in the state but the task expects %r" % (output.timestep_index, value, float(output.time_value)), EXIT_POLICY)
    else:
        if output.timestep_index != 0 and output.time_value is None:
            raise RunnerError("the state has no timeline but timestep index %d was requested" % output.timestep_index, EXIT_POLICY)
        value = None if output.time_value is None else float(output.time_value)
    if value is None:
        return None
    scene = simple.GetAnimationScene()
    try:
        scene.UpdateAnimationUsingDataTimeSteps()
    except Exception:
        pass
    scene.AnimationTime = value
    try:
        view.ViewTime = value
    except Exception:
        pass
    return value


def rotate_about_axis(vector, axis, degrees):
    """Rodrigues rotation of a vector about a unit axis, right-hand rule (vtkTransform.RotateWXYZ
    convention, so the world-axis orbit turns the same way vtkCamera.Azimuth does)."""
    angle = math.radians(degrees)
    cos_a = math.cos(angle)
    sin_a = math.sin(angle)
    ax, ay, az = axis
    vx, vy, vz = vector
    dot = ax * vx + ay * vy + az * vz
    cross = (ay * vz - az * vy, az * vx - ax * vz, ax * vy - ay * vx)
    return (
        vx * cos_a + cross[0] * sin_a + ax * dot * (1.0 - cos_a),
        vy * cos_a + cross[1] * sin_a + ay * dot * (1.0 - cos_a),
        vz * cos_a + cross[2] * sin_a + az * dot * (1.0 - cos_a),
    )


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _normalized(v):
    length = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    if length <= 0.0:
        return None
    return (v[0] / length, v[1] / length, v[2] / length)


def capture_camera(view):
    """The state's captured framing (position, focal point, view-up, parallel scale) - what every
    output's camera move starts from. None when the view exposes no camera; attributes the runtime
    (or the test stub) lacks are simply not captured."""
    try:
        camera = view.GetActiveCamera()
    except Exception:
        return None
    if camera is None:
        return None
    snapshot = {}
    for key, getter in (("position", "GetPosition"), ("focal", "GetFocalPoint"), ("view_up", "GetViewUp")):
        try:
            snapshot[key] = tuple(getattr(camera, getter)())
        except Exception:
            pass
    try:
        snapshot["parallel_scale"] = float(camera.GetParallelScale())
    except Exception:
        pass
    return snapshot


def restore_camera(view, snapshot):
    """Puts the camera back to the captured framing before the next output's move (a batch renders
    several outputs from one loaded state; the moves are absolute, never cumulative)."""
    if not snapshot:
        return
    camera = view.GetActiveCamera()
    for key, setter in (("position", "SetPosition"), ("focal", "SetFocalPoint"), ("view_up", "SetViewUp")):
        if key in snapshot and hasattr(camera, setter):
            getattr(camera, setter)(*snapshot[key])
    if "parallel_scale" in snapshot and hasattr(camera, "SetParallelScale"):
        camera.SetParallelScale(snapshot["parallel_scale"])


def orbit_camera(task, view):
    """The camera move (turntable generalised): revolves the view's camera about the orbit axis
    through its focal point by the output's azimuth, raises it about its own right axis by the
    elevation, and scales its distance to the focal point by the dolly factor. The state's camera
    (position, focal point, view-up) is the starting frame. About the camera's own view-up the
    azimuth is vtkCamera.Azimuth; about a world axis the camera is rotated rigidly (position AND
    view-up), so a tilted camera keeps its tilt, the horizon stays where the user left it, and a
    camera looking straight down the axis rolls instead of degenerating (no view-up reset). The
    elevation is always a rigid rotation about the right axis (view-up x view direction), so the
    view-up follows and the framing never flips at the pole. Returns True when the camera moved.
    `task` is the output here (its camera_* fields)."""
    if not task.moves_camera():
        return False
    try:
        camera = view.GetActiveCamera()
    except Exception as e:
        raise RunnerError("the view exposes no camera to move: %s" % e, EXIT_POLICY)
    if camera is None:
        raise RunnerError("the view exposes no camera to move", EXIT_POLICY)

    if task.camera_azimuth != 0.0:
        if task.camera_axis == CAMERA_AXIS_VIEW_UP:
            camera.Azimuth(task.camera_azimuth)
        else:
            axis = CAMERA_AXIS_VECTORS[task.camera_axis]
            position = tuple(camera.GetPosition())
            focal = tuple(camera.GetFocalPoint())
            view_up = tuple(camera.GetViewUp())
            offset = rotate_about_axis((position[0] - focal[0], position[1] - focal[1], position[2] - focal[2]), axis, task.camera_azimuth)
            camera.SetPosition(focal[0] + offset[0], focal[1] + offset[1], focal[2] + offset[2])
            camera.SetViewUp(*rotate_about_axis(view_up, axis, task.camera_azimuth))

    if task.camera_elevation != 0.0:
        position = tuple(camera.GetPosition())
        focal = tuple(camera.GetFocalPoint())
        view_up = tuple(camera.GetViewUp())
        direction = (focal[0] - position[0], focal[1] - position[1], focal[2] - position[2])
        right = _normalized(_cross(view_up, direction))
        if right is None:
            raise RunnerError("the camera looks along its own view-up; no right axis to raise it about", EXIT_POLICY)
        offset = rotate_about_axis((position[0] - focal[0], position[1] - focal[1], position[2] - focal[2]), right, task.camera_elevation)
        camera.SetPosition(focal[0] + offset[0], focal[1] + offset[1], focal[2] + offset[2])
        camera.SetViewUp(*rotate_about_axis(view_up, right, task.camera_elevation))

    if task.camera_dolly != 1.0:
        position = tuple(camera.GetPosition())
        focal = tuple(camera.GetFocalPoint())
        camera.SetPosition(
            focal[0] + (position[0] - focal[0]) * task.camera_dolly,
            focal[1] + (position[1] - focal[1]) * task.camera_dolly,
            focal[2] + (position[2] - focal[2]) * task.camera_dolly)
        try:
            if camera.GetParallelProjection():
                camera.SetParallelScale(camera.GetParallelScale() * task.camera_dolly)
        except Exception:
            pass
    return True


def render(simple, task, output, view):
    view.ViewSize = [task.width, task.height]
    started = time.time()
    simple.Render(view)
    kwargs = {"ImageResolution": [task.width, task.height]}
    if task.format == "png":
        kwargs["TransparentBackground"] = 1 if task.transparent_background else 0
    simple.SaveScreenshot(output.output_path, view, **kwargs)
    return time.time() - started


def backend_name(view):
    try:
        window = view.GetRenderWindow()
        return window.GetClassName() if window is not None else ""
    except Exception:
        return ""


def verify_output(task, output):
    if not os.path.isfile(output.output_path):
        raise RunnerError("no output file was written")
    size = os.path.getsize(output.output_path)
    if size <= 0:
        raise RunnerError("the output file is empty")
    with open(output.output_path, "rb") as handle:
        head = handle.read(8)
    if task.format == "png" and head[:8] != b"\x89PNG\r\n\x1a\n":
        raise RunnerError("the output is not a PNG")
    if task.format == "jpeg" and head[:2] != b"\xff\xd8":
        raise RunnerError("the output is not a JPEG")
    return size


# ---------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------

def run(task, status):
    status.stage(STAGE_RESOLVE_STATE)
    resolved_state, _rewritten, missing = resolve_state(task)
    status.set("missing_references", len(missing))

    status.stage(STAGE_IMPORT)
    simple, servermanager = import_paraview()
    status.set("paraview_version", paraview_version(simple))
    simple._DisableFirstRenderCameraReset()
    observer = VtkErrorObserver()
    observer.install()

    status.stage(STAGE_LOAD_PLUGIN)
    load_plugin(simple, task)
    status.set("reader_version", reader_version(task.plugin_path))

    status.stage(STAGE_LOAD_STATE)
    try:
        simple.LoadState(resolved_state)
    except Exception as e:
        raise RunnerError("cannot load the state: %s" % e)

    status.stage(STAGE_VALIDATE)
    status.set("proxy_count", runtime_validate(task, servermanager))

    status.stage(STAGE_SELECT)
    view = find_view(simple, servermanager, task.view_id)
    if observer.errors:
        status.set("vtk_errors", len(observer.errors))
        raise RunnerError("VTK reported %d error(s) during load; first: %s" % (len(observer.errors), observer.errors[0]))

    # Every output starts from the state's captured framing: a batch renders several outputs from
    # one loaded state, and the camera moves are absolute, not cumulative.
    home = capture_camera(view)
    moved = False
    total_seconds = 0.0
    total = len(task.outputs)
    for output in task.outputs:
        stage = STAGE_SELECT
        errors_before = len(observer.errors)
        seconds = 0.0
        try:
            status.stage(STAGE_SELECT)
            if moved:
                restore_camera(view, home)
                moved = False
            select_timestep(simple, output, view)

            stage = STAGE_ORBIT
            status.stage(STAGE_ORBIT)
            moved = orbit_camera(output, view)

            stage = STAGE_RENDER
            status.stage(STAGE_RENDER)
            seconds = render(simple, task, output, view)
            total_seconds += seconds
            status.set("render_seconds", round(total_seconds, 3))
            status.set("backend", backend_name(view))

            stage = STAGE_VERIFY
            status.stage(STAGE_VERIFY)
            status.set("vtk_errors", len(observer.errors))
            new_errors = observer.errors[errors_before:]
            if new_errors:
                raise RunnerError("VTK reported %d error(s) during render; first: %s" % (len(new_errors), new_errors[0]))
            verify_output(task, output)
        except RunnerError as e:
            status.output(output.index, False, stage, str(e), seconds)
            raise RunnerError("output %d of %d (timestep %d): %s" % (output.index + 1, total, output.timestep_index, e), e.exit_code)
        status.output(output.index, True, STAGE_DONE, "", seconds)
        sys.stdout.write("render_task: output %d/%d (timestep %d) done in %.2f s\n" % (output.index + 1, total, output.timestep_index, seconds))
        sys.stdout.flush()

    status.set("width", task.width)
    status.set("height", task.height)

    status.stage(STAGE_DONE)
    status.set("ok", True)


def start_parent_watchdog(interval_seconds=2.0):
    """Off Windows the controller cannot tie this process to its own lifetime (the kill-on-close job
    object is a Windows facility), so a node process that crashes or is SIGKILLed mid-task would leave
    a pvpython tree pinning the node (audit C-H2). A daemon thread watches the parent pid: when it
    changes (the parent died and we were re-parented to init or a subreaper) the process leaves at
    once through os._exit — no interpreter shutdown, which has been seen to deadlock on Linux."""
    if os.name == "nt":
        return
    parent = os.getppid()

    def watch():
        while True:
            time.sleep(interval_seconds)
            if os.getppid() != parent:
                sys.stderr.write("render_task: parent process %d is gone; exiting\n" % parent)
                sys.stderr.flush()
                os._exit(EXIT_FAILURE)

    thread = threading.Thread(target=watch, name="parent-watchdog")
    thread.daemon = True
    thread.start()


def main(argv):
    parser = argparse.ArgumentParser(prog="render_task.py", add_help=True)
    parser.add_argument("--task-file", required=True, help="path of the task JSON written by the controller")
    try:
        args = parser.parse_args(argv)
    except SystemExit:
        return EXIT_USAGE

    start_parent_watchdog()
    status = Status()
    status_path = None
    exit_code = EXIT_FAILURE
    try:
        status.stage(STAGE_LOAD_TASK)
        task = load_task(args.task_file)
        status_path = task.status_path
        run(task, status)
        exit_code = EXIT_OK
    except RunnerError as e:
        status.fail(str(e))
        exit_code = e.exit_code
    except Exception as e:  # noqa: BLE001 — every exception is a discrepancy
        status.fail("%s: %s\n%s" % (type(e).__name__, e, traceback.format_exc()[-2000:]))
        exit_code = EXIT_FAILURE
    finally:
        status.write(status_path)

    if exit_code != EXIT_OK:
        sys.stderr.write("render_task: %s\n" % status.data.get("error", "failed"))
    return exit_code


if __name__ == "__main__":
    code = main(sys.argv[1:])
    # Leave through os._exit: every artifact (the frame, status.json) is already on disk, and the
    # regular interpreter shutdown — ParaView session teardown, the OSMesa/llvmpipe thread pool,
    # MPI finalisation — has been seen to deadlock on Linux after a completed render, which would
    # hold the task until the controller's wall-clock limit.
    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(code)
