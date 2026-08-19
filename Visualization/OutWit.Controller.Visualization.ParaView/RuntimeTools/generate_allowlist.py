"""Generates the proxy allowlist (docs 03, section 8.3) from the fixture corpus with the PINNED runtime.

    pvpython generate_allowlist.py --corpus <corpus-dir> --out ../Allowlists/paraview-<major.minor>.json
        [--plugin <name>=<path/to/plugin.py> ...]

For every state of the corpus the allowlist takes the UNION of two views, because the host and the
node validate different universes of the same state:
  - the proxies the state file DECLARES (every <Proxy group type> element, including the helper
    proxies ParaView saves: implicit functions, glyph sources, animation helpers, settings) — what
    ParaView.Validate checks on the host;
  - the proxies a fresh session REGISTERS after LoadState in the groups the runner's post-load
    check covers — what render_task.py checks on the node.
Plugin-contributed proxies are recorded under pluginProxies by loading each named plugin and
diffing against the core set. The output is a reviewed artifact: regenerate on every runtime bump
and commit the diff.
"""

import argparse
import json
import os
import re
import sys

# Must match render_task.py VALIDATED_PROXY_GROUPS.
VALIDATED_PROXY_GROUPS = (
    "sources", "representations", "views", "lookup_tables", "piecewise_functions",
    "transfer_2d_functions", "animation", "misc", "implicit_functions", "textures",
    "extended_sources", "annotations", "additional_lights", "layouts", "scalar_bars",
)

BLOCKED_PROXY_TYPES = {
    "ProgrammableSource", "ProgrammableFilter", "ProgrammableAnnotation", "LiveProgrammableSource",
    "PythonCalculator", "PythonAnnotation", "PythonAnimationCue", "PythonScriptView", "PythonView",
}


DECLARED_PROXY = re.compile(r'<Proxy\s+group="([^"]+)"\s+type="([^"]+)"')


def declared_keys(state_path):
    with open(state_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    return set("%s/%s" % (g, t) for g, t in DECLARED_PROXY.findall(text))


def live_keys(servermanager):
    pxm = servermanager.ProxyManager()
    keys = set()
    for group in VALIDATED_PROXY_GROUPS:
        try:
            proxies = pxm.GetProxiesInGroup(group)
        except Exception:
            continue
        for (_, _), proxy in proxies.items():
            keys.add("%s/%s" % (proxy.GetXMLGroup() or group, proxy.GetXMLName() or ""))
    return keys


def builtin_definitions(servermanager):
    """Every group/type ParaView defines before any plugin is loaded (plugins stay registered for
    the rest of the process, so this snapshot is taken once, first)."""
    keys = set()
    session_pxm = servermanager.vtkSMProxyManager.GetProxyManager().GetActiveSessionProxyManager()
    definitions = session_pxm.GetProxyDefinitionManager()
    iterator = definitions.NewIterator()
    iterator.InitTraversal()
    while not iterator.IsDoneWithTraversal():
        keys.add("%s/%s" % (iterator.GetGroupName(), iterator.GetProxyName()))
        iterator.GoToNextItem()
    return keys


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--plugin", action="append", default=[], help="name=path of an allowlisted plugin; its fixture states are states/<name>/*.pvsm")
    args = parser.parse_args(argv)

    from paraview import simple as pv
    from paraview import servermanager

    version = pv.GetParaViewVersion()
    series = "%d.%d" % (version.major, version.minor)

    # Logical paths inside the states resolve against the corpus root.
    corpus = os.path.abspath(args.corpus)
    out_path = os.path.abspath(args.out)
    os.chdir(corpus)
    states_dir = os.path.join(corpus, "states")
    core = set()
    state_files = sorted(f for f in os.listdir(states_dir) if f.endswith(".pvsm"))
    # states/gui/: the same scenes (and GUI-native ones) saved by the ParaView GUI — see
    # generate_gui_states.py. Core fixtures too: what a real client's state carries.
    gui_dir = os.path.join(states_dir, "gui")
    if os.path.isdir(gui_dir):
        state_files += sorted("gui/" + f for f in os.listdir(gui_dir) if f.endswith(".pvsm"))
    per_state = {}

    # A fresh session's baseline (what exists before any state) is NOT part of a state's contribution
    # but IS legitimately present after load — include it: the runner sees it too.
    pv.ResetSession()
    baseline = live_keys(servermanager)
    core |= baseline
    builtin = builtin_definitions(servermanager)

    for name in state_files:
        pv.ResetSession()
        path = os.path.join(states_dir, *name.split("/"))
        pv.LoadState(path)
        keys = live_keys(servermanager) | declared_keys(path)
        per_state[name] = sorted(keys)
        core |= keys
        print("%s: %d proxies" % (name, len(keys)))

    # Plugin states: whatever they instantiate that is NOT a built-in ParaView proxy is the plugin's
    # contribution; a built-in proxy a plugin state happens to use (WarpByVector around the reader)
    # joins the core list instead — it would be allowlisted for every package, which is what it is.
    plugin_proxies = {}
    plugin_fixtures = {}
    for spec in args.plugin:
        plugin_name, plugin_path = spec.split("=", 1)
        plugin_states = os.path.join(states_dir, plugin_name)
        contributed = set()
        plugin_fixtures[plugin_name] = sorted(f for f in os.listdir(plugin_states) if f.endswith(".pvsm"))
        for name in plugin_fixtures[plugin_name]:
            pv.ResetSession()
            pv.LoadPlugin(os.path.abspath(plugin_path), remote=False, ns=globals())
            plugin_state = os.path.join(plugin_states, name)
            pv.LoadState(plugin_state)
            contributed |= live_keys(servermanager) | declared_keys(plugin_state)
            print("%s/%s: %d proxies" % (plugin_name, name, len(contributed)))
        own = set()
        for key in sorted(contributed - core):
            if key in builtin:
                core.add(key)
            else:
                own.add(key)
        plugin_proxies[plugin_name] = sorted(own)

    leaked = sorted(k for k in core if k.split("/")[1] in BLOCKED_PROXY_TYPES)
    if leaked:
        print("ERROR: the corpus instantiates blocked proxy types:", leaked)
        return 1

    document = {
        "schemaVersion": 1,
        "paraview": series,
        "origin": "generated",
        "note": "Generated by RuntimeTools/generate_allowlist.py from the fixture corpus (pvpython- and GUI-saved states) with %s; a diff here is a reviewed change, never a silent one." % pv.GetParaViewSourceVersion(),
        "fixtures": state_files + ["%s/%s" % (plugin, f) for plugin, files in plugin_fixtures.items() for f in files],
        "proxies": sorted(core),
        "pluginProxies": plugin_proxies,
    }
    with open(out_path, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print("allowlist: %d proxies -> %s" % (len(core), out_path))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
