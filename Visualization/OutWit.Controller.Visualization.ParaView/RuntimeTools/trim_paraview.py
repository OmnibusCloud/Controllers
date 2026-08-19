"""Trims an official ParaView distribution down to the headless pvpython runtime the controller ships.

    python trim_paraview.py --platform windows-x64|linux-x64|macos-arm64 --source <extracted-dist> --out <runtime-dir> [--dry-run]

What goes (and why):
  - the Qt client and everything only it needs: paraview(.exe), pq* and Qt libraries, Qt plugin dirs,
    translations, docs, examples, the web/ and openxrmodels/ shares;
  - vendor stacks no allowlisted pipeline reaches: NVIDIA IndeX / MDL / OptiX / VisRTX, OpenXR remoting,
    MKL (only OpenTURNS links it), Catalyst stubs, ParaView's own plugins directory (the controller loads
    only its bundled reader from a controlled path), OSPRay materials and the OSPRay MPI device (the CPU
    device stays: ParaView initialises OSPRay with every render view and complains loudly without it);
  - Windows: the duplicate copies of every VTK/ParaView DLL under Lib/site-packages (pvpython.exe lives
    in bin/, the application directory, which the loader searches first — the copies are superbuild
    artifacts; the few DLLs that exist ONLY there are moved into bin/);
  - Python packages pvpython does not need for rendering: scipy, pandas, matplotlib, sympy, cython,
    pywin32, h5py, netCDF4, openPMD, conduit/catalyst, PIL and friends. numpy stays.
What stays: pvpython (+ pvbatch), every library in the static import closure of pvpython and the Python
extension modules (RuntimeTools/pe_closure.py on Windows), the Python standard library and numpy, the
ParaView Python packages, the ParaView license and SPDX materials (share/licenses, share/paraview-x.y/spdx).

The result is VERIFIED by the controller's real-runtime tests (OUTWIT_PVPYTHON=<out>/bin/pvpython) and by
regenerating the allowlist with it — never by this script alone. Trimming is per runtime version; review the
rules when the pinned version changes.
"""

import argparse
import fnmatch
import os
import shutil
import sys

RULES = {
    "windows-x64": {
        "remove_dirs": [
            "doc", "examples", "translations", "kernels_nvidia_index", "materials",
            "share/paraview-*/web", "share/paraview-*/openxrmodels", "share/proj",
            "bin/platforms", "bin/imageformats", "bin/iconengines", "bin/sqldrivers", "bin/styles",
            "bin/paraview-*/plugins",
            "bin/Lib/ensurepip", "bin/Lib/idlelib", "bin/Lib/tkinter", "bin/Lib/turtledemo", "bin/Lib/test",
        ],
        "remove_files": [
            "bin/paraview.exe", "bin/vrpn_*.exe", "bin/pvdataserver.exe", "bin/pvrenderserver.exe", "bin/pvserver.exe",
            "bin/Qt6*.dll", "bin/pq*-pv*.dll", "bin/vtkGUISupportQt-pv*.dll", "bin/vtkQtTesting-pv*.dll", "bin/vtkExtensionsShaderBall-pv*.dll",
            "bin/libnvindex*.dll", "bin/libmdl_sdk.dll", "bin/libdice.dll", "bin/nv_freeimage.dll", "bin/visrtx.dll", "bin/optix*.dll",
            "bin/Microsoft.Holographic.*.dll", "bin/mkl_core.2.dll", "bin/mkl_def.2.dll", "bin/catalyst-paraview.dll", "bin/catalyst-stub.dll", "bin/dds.dll",
            # OSPRay's CPU device (ospray_module_ispc + Embree + Open VKL) STAYS: ParaView initialises the
            # OSPRay backend when a render view is created and prints "#ospray: INVALID device" on every run
            # without it; with it, a state that enables ray tracing renders through the CPU path tracer.
            # Only the MPI device module goes.
            "bin/ospray_module_mpi.dll",
        ],
        "site_packages": "bin/Lib/site-packages",
        "dll_dir": "bin",
        "dedupe_dll_dirs": ["vtkmodules", "paraview/modules", "paraview/incubator"],
        "remove_packages": [
            "scipy", "win32comext", "pandas", "catalyst_conduit", "openpmd_api", "cython", "Cython", "matplotlib", "mpl_toolkits", "sympy",
            "pythonwin", "h5py", "netCDF4", "win32", "win32com", "pywin32_system32", "PIL", "catalyst", "contourpy", "kiwisolver", "cftime",
            "pygments", "fontTools", "tzdata", "pytz", "mpmath", "pyparsing", "isapi", "adodbapi", "dateutil", "cycler.py", "pythoncom.py",
            "pywin32.pth", "versioneer.py", "pywintypes.py", "win32com.pth", "*.dist-info",
        ],
    },
    "linux-x64": {
        # Layout of the official MPI tarball: bin/ (pvpython = ELF launcher + pvpython-real), lib/ (flat:
        # VTK/ParaView + every vendor lib, ~1.1 GB that is one DT_NEEDED chain from libvtkRemotingApplication
        # and cannot shrink without rebuilding), lib/mesa (bundled llvmpipe OSMesa/GL the launcher falls back
        # to), lib/mpi (Open MPI the launcher adds to LD_LIBRARY_PATH), lib/python3.12, share/.
        "remove_dirs": [
            "share/doc", "share/paraview-*/doc", "share/paraview-*/examples", "share/paraview-*/translations", "share/paraview-*/web",
            "share/paraview-*/openxrmodels", "share/paraview-*/kernels_nvidia_index", "share/paraview-*/materials",
            "share/proj", "share/icons", "share/applications", "share/mime", "share/metainfo",
            "lib/paraview-*/plugins", "lib/qt*",
            "lib/python*/ensurepip", "lib/python*/idlelib", "lib/python*/tkinter", "lib/python*/turtledemo", "lib/python*/test",
            "plugins", "materials",
        ],
        "remove_files": [
            "bin/paraview", "bin/paraview-config", "bin/vrpn_*", "bin/pvdataserver", "bin/pvrenderserver", "bin/pvserver",
            "lib/libQt*", "lib/libpq*", "lib/libvtkGUISupportQt-pv*", "lib/libvtkQtTesting-pv*", "lib/libvtkExtensionsShaderBall-pv*",
            # Qt's ICU (nothing else in the tree links it), NVIDIA IndeX / MDL / OptiX / VisRTX (VisRTX is the only
            # library needing system libEGL/libGLX/libOpenGL), FreeImage/dds (IndeX helpers), openPMD (the Python
            # package is removed below), MKL, Catalyst stubs.
            "lib/libicu*", "lib/libnvindex*", "lib/libmdl_sdk*", "lib/libdice*", "lib/nv_freeimage*", "lib/libnv_freeimage*", "lib/dds.so*",
            "lib/libVisRTX*", "lib/libvisrtx*", "lib/liboptix*", "lib/libopenPMD*",
            "lib/libmkl_core*", "lib/libmkl_def*", "lib/libmkl_avx*", "lib/libmkl_mc*", "lib/libmkl_vml*", "lib/libcatalyst-paraview*", "lib/libcatalyst-stub*",
            # OSPRay's MPI device (the CPU device stays, see the Windows rule) and Mesa's swr rasterizer: the
            # baseline is llvmpipe (GALLIUM_DRIVER default), which the launcher selects without --backend.
            "lib/libospray_module_mpi*", "lib/mesa/libswrAVX*",
            # scipy leftovers (the package is removed below) and unreferenced vendor bits.
            "lib/libqhull_r*", "lib/libsf_error_state*", "lib/libusb-1.0*",
        ],
        "site_packages": "lib/python*/site-packages",
        "dll_dir": None,
        "dedupe_dll_dirs": [],
        "remove_packages": [
            "scipy", "pandas", "catalyst_conduit", "openpmd_api", "cython", "Cython", "matplotlib", "mpl_toolkits", "sympy", "h5py", "netCDF4",
            "PIL", "catalyst", "contourpy", "kiwisolver", "cftime", "pygments", "fontTools", "tzdata", "pytz", "mpmath", "pyparsing", "dateutil",
            "cycler.py", "versioneer.py", "*.dist-info",
            # mpi4py is imported only on multi-rank controllers (vtkmodules.numpy_interface, paraview.detail.calculator
            # guard on GetNumberOfProcesses() > 1); the single-process runner never reaches it.
            "mpi4py",
        ],
    },
    "macos-arm64": {
        # Layout of the official dmg's ParaView-6.1.1.app: Contents/bin (pvpython is a real Mach-O, rpath
        # @executable_path/../Libraries), Contents/Libraries (flat dylibs, the Python stdlib under lib/python3.12,
        # and — unlike Linux — the plugin libraries next to the core ones), Contents/Python (site-packages),
        # Contents/Frameworks (Qt only), Contents/Plugins (the .so plugin stubs), Contents/Resources.
        "remove_dirs": [
            "Contents/doc", "Contents/examples", "Contents/translations", "Contents/materials",
            "Contents/Resources/web", "Contents/Resources/qml", "Contents/Plugins", "Contents/PlugIns", "Contents/Frameworks",
            "Contents/Libraries/catalyst",
            "Contents/Libraries/lib/python*/ensurepip", "Contents/Libraries/lib/python*/idlelib", "Contents/Libraries/lib/python*/tkinter",
            "Contents/Libraries/lib/python*/turtledemo", "Contents/Libraries/lib/python*/test",
        ],
        "remove_files": [
            "Contents/MacOS/paraview", "Contents/MacOS/mpiexec", "Contents/MacOS/hydra_pmi_proxy",
            "Contents/bin/paraview", "Contents/bin/vrpn*", "Contents/bin/pvdataserver", "Contents/bin/pvrenderserver", "Contents/bin/pvserver",
            "Contents/bin/ospray_mpi_worker", "Contents/bin/mpiexec", "Contents/bin/hydra_pmi_proxy",
            "Contents/Resources/proj.db", "Contents/Resources/qt.conf", "Contents/Resources/pvIcon.icns",
            "Contents/Libraries/libpq*", "Contents/Libraries/libvtkGUISupportQt-pv*", "Contents/Libraries/libvtkQtTesting-pv*", "Contents/Libraries/libvtkExtensionsShaderBall-pv*",
            "Contents/Libraries/libnvindex*", "Contents/Libraries/libcatalyst-paraview*", "Contents/Libraries/libcatalyst-stub*",
            "Contents/Libraries/libospray_module_mpi*", "Contents/Libraries/libqhull_r*", "Contents/Libraries/libsf_error_state*",
        ],
        # Plugin libraries live next to the core ones here, so the rule set is completed by a closure prune:
        # every dylib not reachable from pvpython / the Python extension modules goes, except the dlopen'd
        # families (OSPRay CPU device and friends).
        "prune_unreachable": {
            "libraries": "Contents/Libraries",
            "roots": ["Contents/bin/pvpython", "Contents/bin/pvbatch", "Contents/Python/**/*.so", "Contents/Libraries/lib/python*/lib-dynload/*.so"],
            "keep": ["libospray*", "libopenvkl*", "libembree*", "librkcommon*", "libtbb*", "libglcommon*"],
        },
        "site_packages": "Contents/Python",
        "dll_dir": None,
        "dedupe_dll_dirs": [],
        "remove_packages": [
            "scipy", "pandas", "catalyst_conduit", "openpmd_api", "cython", "Cython", "matplotlib", "mpl_toolkits", "sympy", "h5py", "netCDF4",
            "PIL", "catalyst", "contourpy", "kiwisolver", "cftime", "pygments", "fontTools", "tzdata", "pytz", "mpmath", "pyparsing", "dateutil",
            "cycler.py", "versioneer.py", "*.dist-info",
            # mpi4py is imported only on multi-rank controllers (vtkmodules.numpy_interface, paraview.detail.calculator
            # guard on GetNumberOfProcesses() > 1); the single-process runner never reaches it.
            "mpi4py",
        ],
    },
}


def size_of(path):
    total = 0
    for dirpath, _, files in os.walk(path):
        for f in files:
            full = os.path.join(dirpath, f)
            if os.path.islink(full):
                continue
            try:
                total += os.path.getsize(full)
            except OSError:
                pass
    return total


def expand(root, patterns, want_dirs):
    """Glob-like expansion of rule patterns relative to root (each segment may carry wildcards)."""
    results = []
    for pattern in patterns:
        parts = pattern.split("/")
        candidates = [root]
        for part in parts:
            next_candidates = []
            for candidate in candidates:
                if not os.path.isdir(candidate):
                    continue
                for name in os.listdir(candidate):
                    if fnmatch.fnmatch(name, part):
                        next_candidates.append(os.path.join(candidate, name))
            candidates = next_candidates
        for candidate in candidates:
            if os.path.isdir(candidate) == want_dirs:
                results.append(candidate)
    return sorted(set(results))


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--platform", required=True, choices=sorted(RULES))
    parser.add_argument("--source", required=True, help="extracted official distribution root (the directory holding bin/ or Contents/)")
    parser.add_argument("--out", required=True, help="target runtime directory (created; must not exist unless --force)")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    rules = RULES[args.platform]
    source = os.path.abspath(args.source)
    out = os.path.abspath(args.out)
    if os.path.exists(out):
        if not args.force:
            print("refusing to overwrite", out, "(use --force)")
            return 2
        shutil.rmtree(out)

    before = size_of(source)
    print("copying %s (%.0f MB) -> %s" % (source, before / 1e6, out))
    if not args.dry_run:
        shutil.copytree(source, out, symlinks=True)
    root = out if not args.dry_run else source

    removed = 0

    def remove_path(path):
        nonlocal removed
        # Symlinks (Linux/macOS trees) count as zero: their target is removed by its own rule, and a
        # link whose target is already gone must not make getsize fail.
        if os.path.islink(path):
            size = 0
        elif os.path.isdir(path):
            size = size_of(path)
        else:
            size = os.path.getsize(path)
        removed += size
        print("  - %-90s %8.1f MB" % (os.path.relpath(path, root), size / 1e6))
        if args.dry_run:
            return
        if os.path.isdir(path) and not os.path.islink(path):
            shutil.rmtree(path)
        else:
            os.remove(path)

    # Dedupe the site-packages DLL copies FIRST, against the untouched bin/: a copy whose original is
    # removed by the rules below must not be mistaken for a unique DLL and moved back.
    site_packages = expand(root, [rules["site_packages"]], want_dirs=True)
    for sp in site_packages:
        if rules["dll_dir"] and rules["dedupe_dll_dirs"]:
            dll_dir = os.path.join(root, rules["dll_dir"])
            present = {name.lower() for name in os.listdir(dll_dir) if name.lower().endswith(".dll")}
            for sub in rules["dedupe_dll_dirs"]:
                folder = os.path.join(sp, *sub.split("/"))
                if not os.path.isdir(folder):
                    continue
                for name in sorted(os.listdir(folder)):
                    if not name.lower().endswith(".dll"):
                        continue
                    path = os.path.join(folder, name)
                    if name.lower() in present:
                        remove_path(path)
                    else:
                        print("  > keeping unique DLL %s -> %s" % (os.path.relpath(path, root), rules["dll_dir"]))
                        if not args.dry_run:
                            shutil.move(path, os.path.join(dll_dir, name))
                        present.add(name.lower())

    for path in expand(root, rules["remove_dirs"], want_dirs=True):
        remove_path(path)
    for path in expand(root, rules["remove_files"], want_dirs=False):
        remove_path(path)

    for sp in site_packages:
        for pattern in rules["remove_packages"]:
            for name in sorted(os.listdir(sp)):
                if fnmatch.fnmatch(name, pattern):
                    remove_path(os.path.join(sp, name))

    prune = rules.get("prune_unreachable")
    if prune:
        import glob
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        from macho_closure import loads_of_file
        lib_dir = os.path.join(root, *prune["libraries"].split("/"))
        available = {}
        for name in os.listdir(lib_dir):
            path = os.path.join(lib_dir, name)
            if os.path.isfile(path) and not os.path.islink(path) and ".dylib" in name:
                available[name] = path
        roots = []
        for pattern in prune["roots"]:
            roots.extend(glob.glob(os.path.join(root, *pattern.split("/")), recursive=True))
        seen, queue = set(), list(roots)
        while queue:
            for name in loads_of_file(queue.pop()):
                if name in available and name not in seen:
                    seen.add(name)
                    queue.append(available[name])
        print("closure prune: %d of %d libraries reachable from %d roots" % (len(seen), len(available), len(roots)))
        for name in sorted(available):
            if name in seen or any(fnmatch.fnmatch(name, keep) for keep in prune["keep"]):
                continue
            remove_path(available[name])
            # Aliases (symlinks) of a pruned library go with it.
            for alias in os.listdir(lib_dir):
                alias_path = os.path.join(lib_dir, alias)
                if os.path.islink(alias_path) and not os.path.exists(alias_path):
                    remove_path(alias_path)

    after = size_of(root) if not args.dry_run else before - removed
    print("removed %.0f MB; runtime %.0f MB -> %.0f MB (%.0f%%)" % (removed / 1e6, before / 1e6, after / 1e6, 100.0 * after / max(1, before)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
