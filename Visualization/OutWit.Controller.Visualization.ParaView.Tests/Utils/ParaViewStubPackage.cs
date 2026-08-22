namespace OutWit.Controller.Visualization.ParaView.Tests.Utils;

/// <summary>
/// Writes a stub <c>paraview</c> (+ <c>vtkmodules</c>) Python package that implements exactly the
/// surface the controller-owned runner touches: version queries, plugin/state loading (the state is
/// parsed into proxies with registration names), the proxy manager enumeration, views, the time
/// keeper and animation scene, Render, SaveScreenshot (writes a real PNG), and the VTK output
/// window observer hook. The runner tests drive the real render_task.py against it.
/// </summary>
internal static class ParaViewStubPackage
{
    #region Functions

    public static void WriteTo(string directory)
    {
        var paraview = Path.Combine(directory, "paraview");
        var vtk = Path.Combine(directory, "vtkmodules");
        Directory.CreateDirectory(paraview);
        Directory.CreateDirectory(vtk);

        File.WriteAllText(Path.Combine(paraview, "__init__.py"), "\"\"\"Stub paraview package for the runner tests.\"\"\"\n");
        File.WriteAllText(Path.Combine(paraview, "_stub.py"), STUB);
        File.WriteAllText(Path.Combine(paraview, "simple.py"), SIMPLE);
        File.WriteAllText(Path.Combine(paraview, "servermanager.py"), SERVERMANAGER);
        File.WriteAllText(Path.Combine(vtk, "__init__.py"), "\"\"\"Stub vtkmodules for the runner tests.\"\"\"\n");
        File.WriteAllText(Path.Combine(vtk, "vtkCommonCore.py"), VTK_COMMON_CORE);
    }

    #endregion

    #region Sources

    private const string STUB = """
import os
import struct
import zlib
import binascii
import xml.etree.ElementTree as ET


class Registry(object):
    proxies = []
    views = {}
    timesteps = []
    scene = None
    log_path = None
    error_callbacks = []


def log(message):
    if Registry.log_path:
        with open(Registry.log_path, "a", encoding="utf-8") as handle:
            handle.write(message + "\n")


def fire_error(text):
    for callback in list(Registry.error_callbacks):
        callback(None, "ErrorEvent", text)


class StubProperty(object):
    def __init__(self):
        self.SMProperty = None


class StubProxy(object):
    def __init__(self, group, xml_type, proxy_id, name, props, collection=None):
        self.group = group
        self.xml_type = xml_type
        self.id = proxy_id
        self.name = name
        self.props = props
        # Like ParaView: filters carry XML group "filters" but are REGISTERED in the "sources" collection.
        self.collection = collection or group

    def GetXMLGroup(self):
        return self.group

    def GetXMLName(self):
        return self.xml_type

    def ListProperties(self):
        return list(self.props.keys())

    def GetPropertyValue(self, name):
        values = self.props.get(name, [])
        if len(values) == 1:
            return values[0]
        return list(values)

    def GetProperty(self, name):
        return StubProperty()


class StubRenderWindow(object):
    def GetClassName(self):
        return "vtkStubRenderWindow"


class StubCamera(object):
    def __init__(self):
        self.position = (0.0, 0.0, 10.0)
        self.focal = (0.0, 0.0, 0.0)
        self.view_up = (0.0, 1.0, 0.0)

    def GetPosition(self):
        return self.position

    def GetFocalPoint(self):
        return self.focal

    def GetViewUp(self):
        return self.view_up

    def SetPosition(self, x, y, z):
        self.position = (x, y, z)
        log("position=%.4f,%.4f,%.4f" % (x, y, z))

    def SetViewUp(self, x, y, z):
        self.view_up = (x, y, z)
        log("view_up=%.4f,%.4f,%.4f" % (x, y, z))

    def Azimuth(self, degrees):
        log("azimuth=%r" % (degrees,))


class StubView(StubProxy):
    def __init__(self, group, xml_type, proxy_id, name, props, collection=None):
        StubProxy.__init__(self, group, xml_type, proxy_id, name, props, collection)
        self.ViewSize = [0, 0]
        self.ViewTime = 0.0
        self.camera = StubCamera()

    def GetRenderWindow(self):
        return StubRenderWindow()

    def GetActiveCamera(self):
        return self.camera


class TimeKeeper(object):
    def __init__(self, timesteps):
        self.TimestepValues = list(timesteps)


class Scene(object):
    def __init__(self):
        self._time = 0.0

    def UpdateAnimationUsingDataTimeSteps(self):
        log("update_animation")

    @property
    def AnimationTime(self):
        return self._time

    @AnimationTime.setter
    def AnimationTime(self, value):
        self._time = value
        log("animation_time=%r" % (value,))


def load_state(path):
    Registry.proxies = []
    Registry.views = {}
    Registry.timesteps = []
    Registry.scene = Scene()
    Registry.log_path = os.path.join(os.path.dirname(path), "stub.log")
    log("load_state=%s" % path)
    with open(path, "rb") as handle:
        raw = handle.read()
    root = ET.fromstring(raw)
    names = {}
    collections = {}
    for state in root.findall("ServerManagerState"):
        for collection in state.findall("ProxyCollection"):
            for item in collection.findall("Item"):
                names[item.get("id", "")] = item.get("name", "")
                collections[item.get("id", "")] = collection.get("name", "")
        for proxy in state.findall("Proxy"):
            group = proxy.get("group", "")
            xml_type = proxy.get("type", "")
            proxy_id = proxy.get("id", "")
            props = {}
            for prop in proxy.findall("Property"):
                props[prop.get("name", "")] = [e.get("value", "") for e in prop.findall("Element")]
            name = names.get(proxy_id, "%s%s" % (xml_type, proxy_id))
            collection = collections.get(proxy_id, group)
            if group == "views":
                view = StubView(group, xml_type, proxy_id, name, props, collection)
                Registry.views[name] = view
                Registry.proxies.append(view)
            else:
                Registry.proxies.append(StubProxy(group, xml_type, proxy_id, name, props, collection))
            if group == "misc" and xml_type == "TimeKeeper":
                Registry.timesteps = [float(v) for v in props.get("TimestepValues", [])]
    if b"STUB-VTK-ERROR" in raw:
        fire_error("stub reader failure requested by the state")


def write_png(path, width, height, alpha):
    channels = 4 if alpha else 3
    rows = []
    for y in range(height):
        row = bytearray([0])
        for x in range(width):
            row += bytes([(x * 7) % 256, (y * 11) % 256, 96] + ([200] if alpha else []))
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", binascii.crc32(body) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6 if alpha else 2, 0, 0, 0)
    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", ihdr))
        handle.write(chunk(b"IDAT", zlib.compress(raw)))
        handle.write(chunk(b"IEND", b""))
""";

    private const string SIMPLE = """
from . import _stub


class _Version(object):
    major = 6
    minor = 1


def GetParaViewSourceVersion():
    return "paraview version 6.1.1-stub"


def GetParaViewVersion():
    return _Version()


def _DisableFirstRenderCameraReset():
    _stub.log("disable_first_render_camera_reset")


def LoadPlugin(path, remote=False, ns=None):
    _stub.log("load_plugin=%s" % path)


def LoadState(path, *args, **kwargs):
    _stub.load_state(path)


def FindView(name):
    return _stub.Registry.views.get(name)


def GetTimeKeeper():
    return _stub.TimeKeeper(_stub.Registry.timesteps)


def GetAnimationScene():
    return _stub.Registry.scene


def Render(view=None):
    _stub.log("render view=%s" % (getattr(view, "name", "?"),))


def SaveScreenshot(path, view=None, ImageResolution=None, TransparentBackground=0, **kwargs):
    width, height = ImageResolution if ImageResolution else (view.ViewSize[0], view.ViewSize[1])
    _stub.log("save_screenshot=%s view=%s size=%dx%d transparent=%d" % (path, getattr(view, "name", "?"), width, height, int(TransparentBackground)))
    _stub.write_png(path, width, height, bool(TransparentBackground))
""";

    private const string SERVERMANAGER = """
from . import _stub


class _ProxyManager(object):
    def GetProxyGroups(self):
        groups = []
        for proxy in _stub.Registry.proxies:
            if proxy.group not in groups:
                groups.append(proxy.group)
        return groups

    def GetProxiesInGroup(self, group):
        return {(proxy.name, proxy.id): proxy for proxy in _stub.Registry.proxies if proxy.collection == group}

    def GetProxy(self, group, name):
        for proxy in _stub.Registry.proxies:
            if proxy.collection == group and proxy.name == name:
                return proxy
        return None


def ProxyManager():
    return _ProxyManager()


def _getPyProxy(proxy):
    return proxy
""";

    private const string VTK_COMMON_CORE = """
from paraview import _stub


class vtkOutputWindow(object):
    _instance = None

    @classmethod
    def GetInstance(cls):
        if cls._instance is None:
            cls._instance = vtkOutputWindow()
        return cls._instance

    def AddObserver(self, event, callback):
        if event == "ErrorEvent":
            _stub.Registry.error_callbacks.append(callback)
        return len(_stub.Registry.error_callbacks)
""";

    #endregion
}
