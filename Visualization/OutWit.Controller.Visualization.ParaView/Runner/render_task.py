"""OmnibusCloud ParaView task runner (controller-owned, never user-supplied).

Invoked by the ParaView controller's RenderFrame adapter as

    pvpython --force-offscreen-rendering --disable-registry render_task.py --task-file <task.json>

with an allowlisted environment and cwd = the task's package root. Everything the run needs is
in the task file (see ParaViewRunnerTask on the controller side): the materialized state, the
package root every logical path resolves under, the view, the timestep, the output size/format/
path, the optional bundled reader to load, and the effective proxy allowlist plus the blocked
proxy/property lists for the post-load runtime check.

Responsibilities (docs 03, section 9):
  1. touch no user configuration and no plugin path other than the controller-owned one;
  2. load only the bundled reader, from the task's plugin path;
  3. resolve the state's logical file references under the package root (the state never carries
     a client path): a single-valued reference must be a materialized package file, a file series
     must have at least one, and any VTK error during load/render fails the task;
  4. select the declared view and timestep;
  5. apply only controller-owned output overrides (size, transparency);
  6. render through SaveScreenshot;
  7. write the bounded machine-readable status document on EVERY exit path;
  8. exit non-zero on any discrepancy, publishing nothing.

Stdlib + paraview only. Compatible with the Python ParaView bundles (3.9+ syntax).
"""

import argparse
import json
import os
import re
import sys
import time
import traceback
import xml.etree.ElementTree as ET

STATUS_SCHEMA = 1
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
STAGE_RENDER = "render"
STAGE_VERIFY = "verify-output"
STAGE_DONE = "done"

EXIT_OK = 0
EXIT_FAILURE = 1
EXIT_USAGE = 2
EXIT_POLICY = 3

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

class Task(object):
    """The parsed task file."""

    REQUIRED = (
        "schema", "state_path", "package_root", "work_dir", "output_path", "status_path",
        "view_id", "timestep_index", "width", "height", "format",
    )

    def __init__(self, data):
        for key in self.REQUIRED:
            if key not in data:
                raise RunnerError("task file lacks '%s'" % key, EXIT_USAGE)
        if data["schema"] != 1:
            raise RunnerError("unsupported task file schema %r" % (data["schema"],), EXIT_USAGE)

        self.task_id = data.get("task_id", "")
        self.state_path = data["state_path"]
        self.package_root = data["package_root"]
        self.work_dir = data["work_dir"]
        self.output_path = data["output_path"]
        self.status_path = data["status_path"]
        self.view_id = data["view_id"]
        self.timestep_index = int(data["timestep_index"])
        self.time_value = data.get("time_value", None)
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
        if not os.path.isdir(self.package_root):
            raise RunnerError("package root '%s' is not a directory" % self.package_root, EXIT_USAGE)
        if not os.path.isdir(self.work_dir):
            raise RunnerError("work dir '%s' is not a directory" % self.work_dir, EXIT_USAGE)
        if not os.path.isfile(self.state_path):
            raise RunnerError("state file '%s' does not exist" % self.state_path, EXIT_USAGE)
        if not is_inside(self.output_path, self.work_dir):
            raise RunnerError("output path escapes the work directory", EXIT_POLICY)
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

    if root.tag != "ParaView":
        raise RunnerError("state root element must be <ParaView>", EXIT_POLICY)

    rewritten = 0
    missing = []
    for state in root.findall("ServerManagerState"):
        if state.find("CustomProxyDefinitions") is not None:
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
        try:
            window = vtkOutputWindow.GetInstance()
            window.AddObserver("ErrorEvent", self._on_error)
            return True
        except Exception:
            return False

    def _on_error(self, caller, event, *args):
        try:
            text = ""
            if args and isinstance(args[0], str):
                text = args[0]
            elif hasattr(caller, "GetText"):
                text = caller.GetText() or ""
            self.errors.append((text or "vtk error").strip()[:500])
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


def select_timestep(simple, task, view):
    """Selects the task's timestep. The live TimeKeeper is authoritative when it carries a timeline:
    the index must be in range and agree with the task's time value. When it carries none (a
    static scene, or a runtime that does not expose the timeline) the task's time value, resolved
    host-side from the producer's timeline, is applied as the animation time."""
    keeper = simple.GetTimeKeeper()
    try:
        times = list(keeper.TimestepValues) if keeper is not None else []
    except Exception:
        times = []
    if times:
        if task.timestep_index < 0 or task.timestep_index >= len(times):
            raise RunnerError("timestep index %d is outside the timeline of %d timestep(s)" % (task.timestep_index, len(times)), EXIT_POLICY)
        value = float(times[task.timestep_index])
        if task.time_value is not None and abs(float(task.time_value) - value) > 1e-9 * max(1.0, abs(value)):
            raise RunnerError("timestep index %d has time %r in the state but the task expects %r" % (task.timestep_index, value, float(task.time_value)), EXIT_POLICY)
    else:
        if task.timestep_index != 0 and task.time_value is None:
            raise RunnerError("the state has no timeline but timestep index %d was requested" % task.timestep_index, EXIT_POLICY)
        value = None if task.time_value is None else float(task.time_value)
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


def render(simple, task, view):
    view.ViewSize = [task.width, task.height]
    started = time.time()
    simple.Render(view)
    kwargs = {"ImageResolution": [task.width, task.height]}
    if task.format == "png":
        kwargs["TransparentBackground"] = 1 if task.transparent_background else 0
    simple.SaveScreenshot(task.output_path, view, **kwargs)
    return time.time() - started


def backend_name(view):
    try:
        window = view.GetRenderWindow()
        return window.GetClassName() if window is not None else ""
    except Exception:
        return ""


def verify_output(task):
    if not os.path.isfile(task.output_path):
        raise RunnerError("no output file was written")
    size = os.path.getsize(task.output_path)
    if size <= 0:
        raise RunnerError("the output file is empty")
    with open(task.output_path, "rb") as handle:
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
    select_timestep(simple, task, view)

    status.stage(STAGE_RENDER)
    seconds = render(simple, task, view)
    status.set("render_seconds", round(seconds, 3))
    status.set("backend", backend_name(view))

    status.stage(STAGE_VERIFY)
    status.set("vtk_errors", len(observer.errors))
    if observer.errors:
        raise RunnerError("VTK reported %d error(s) during load/render; first: %s" % (len(observer.errors), observer.errors[0]))
    verify_output(task)
    status.set("width", task.width)
    status.set("height", task.height)

    status.stage(STAGE_DONE)
    status.set("ok", True)


def main(argv):
    parser = argparse.ArgumentParser(prog="render_task.py", add_help=True)
    parser.add_argument("--task-file", required=True, help="path of the task JSON written by the controller")
    try:
        args = parser.parse_args(argv)
    except SystemExit:
        return EXIT_USAGE

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
    sys.exit(main(sys.argv[1:]))
