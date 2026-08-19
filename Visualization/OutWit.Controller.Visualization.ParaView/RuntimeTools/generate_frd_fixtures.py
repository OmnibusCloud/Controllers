"""Produces the .frd fixtures that exercise the bundled reader's element mapping and time handling.

    python generate_frd_fixtures.py --ccx <path/to/ccx(.exe)> --out <dir>

Runs CalculiX on small decks — one element of every solid type ccx expands into the cgx element
numbers the reader maps (C3D20 -> he20 type 4, C3D15 -> pe15 type 5, C3D10 -> tet10 type 6,
C3D6 -> pe6 type 2, C3D4 -> tet4 type 3), shells and beams written both expanded (OUTPUT=3D,
default) and as their 2D/1D types (OUTPUT=2D: S8 -> qu8 type 10, S6 -> tr6 type 8, B32 -> be3
type 12), and a transient heat transfer whose increments give genuinely increasing time values —
and keeps the resulting .frd files (plus the decks for provenance). The reader's element
ordering is then proven geometrically by check_frd_reader.py (every mid-side node must be the
midpoint of its VTK edge, every cell valid), never by eye.
"""

import argparse
import os
import shutil
import subprocess
import sys

STEEL = """*MATERIAL, NAME=STEEL
*ELASTIC
210000., 0.3
*DENSITY
7.85E-9
"""

# Unit cube / prism / tetra node tables in CalculiX (*ELEMENT) numbering — the cgx frd numbering
# differs for the quadratic bricks/wedges, which is exactly what the reader's remap is tested on.
HEX20_NODES = """1, 0., 0., 0.
2, 1., 0., 0.
3, 1., 1., 0.
4, 0., 1., 0.
5, 0., 0., 1.
6, 1., 0., 1.
7, 1., 1., 1.
8, 0., 1., 1.
9, 0.5, 0., 0.
10, 1., 0.5, 0.
11, 0.5, 1., 0.
12, 0., 0.5, 0.
13, 0.5, 0., 1.
14, 1., 0.5, 1.
15, 0.5, 1., 1.
16, 0., 0.5, 1.
17, 0., 0., 0.5
18, 1., 0., 0.5
19, 1., 1., 0.5
20, 0., 1., 0.5
"""

WEDGE15_NODES = """1, 0., 0., 0.
2, 1., 0., 0.
3, 0., 1., 0.
4, 0., 0., 1.
5, 1., 0., 1.
6, 0., 1., 1.
7, 0.5, 0., 0.
8, 0.5, 0.5, 0.
9, 0., 0.5, 0.
10, 0.5, 0., 1.
11, 0.5, 0.5, 1.
12, 0., 0.5, 1.
13, 0., 0., 0.5
14, 1., 0., 0.5
15, 0., 1., 0.5
"""

TET10_NODES = """1, 0., 0., 0.
2, 1., 0., 0.
3, 0., 1., 0.
4, 0., 0., 1.
5, 0.5, 0., 0.
6, 0.5, 0.5, 0.
7, 0., 0.5, 0.
8, 0., 0., 0.5
9, 0.5, 0., 0.5
10, 0., 0.5, 0.5
"""


def solid_deck(title, element_type, nodes, connectivity, fixed, loaded):
    return """*HEADING
%s
*NODE, NSET=NALL
%s*ELEMENT, TYPE=%s, ELSET=EALL
1, %s
*NSET, NSET=FIX
%s
*NSET, NSET=TIP
%s
%s*SOLID SECTION, ELSET=EALL, MATERIAL=STEEL
*BOUNDARY
FIX, 1, 3, 0.
*STEP
*STATIC
*CLOAD
TIP, 3, -10.
*NODE FILE
U
*EL FILE
S
*END STEP
""" % (title, nodes, element_type, connectivity, fixed, loaded, STEEL)


def shell_deck(title, element_type, nodes, connectivity, fixed, loaded, output):
    section = "*SHELL SECTION, ELSET=EALL, MATERIAL=STEEL\n0.1\n" if element_type.startswith("S") else "*BEAM SECTION, ELSET=EALL, MATERIAL=STEEL, SECTION=RECT\n0.1, 0.1\n0., 1., 0.\n"
    return """*HEADING
%s
*NODE, NSET=NALL
%s*ELEMENT, TYPE=%s, ELSET=EALL
1, %s
*NSET, NSET=FIX
%s
*NSET, NSET=TIP
%s
%s%s*BOUNDARY
FIX, 1, 6, 0.
*STEP
*STATIC
*CLOAD
TIP, 3, -1.
*NODE FILE, OUTPUT=%s
U
*EL FILE, OUTPUT=%s
S
*END STEP
""" % (title, nodes, element_type, connectivity, fixed, loaded, STEEL, section, output, output)


TRANSIENT_HEAT = """*HEADING
Two-element bar, transient heat transfer: five increments, genuinely increasing times
*NODE, NSET=NALL
1, 0., 0., 0.
2, 1., 0., 0.
3, 2., 0., 0.
4, 0., 1., 0.
5, 1., 1., 0.
6, 2., 1., 0.
7, 0., 0., 1.
8, 1., 0., 1.
9, 2., 0., 1.
10, 0., 1., 1.
11, 1., 1., 1.
12, 2., 1., 1.
*ELEMENT, TYPE=C3D8, ELSET=EALL
1, 1, 2, 5, 4, 7, 8, 11, 10
2, 2, 3, 6, 5, 8, 9, 12, 11
*NSET, NSET=HOT
1, 4, 7, 10
*MATERIAL, NAME=SOLID
*CONDUCTIVITY
1.
*DENSITY
1.
*SPECIFIC HEAT
1.
*SOLID SECTION, ELSET=EALL, MATERIAL=SOLID
*INITIAL CONDITIONS, TYPE=TEMPERATURE
NALL, 0.
*STEP, INC=100
*HEAT TRANSFER, DIRECT
0.2, 1.0
*BOUNDARY
HOT, 11, 11, 100.
*NODE FILE
NT
*END STEP
"""

DECKS = {
    "he20_c3d20": solid_deck("Single C3D20 brick (frd he20, type 4)", "C3D20", HEX20_NODES,
                             "1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,\n16, 17, 18, 19, 20",
                             "1, 2, 3, 4, 9, 10, 11, 12", "5, 6, 7, 8, 13, 14, 15, 16"),
    "pe15_c3d15": solid_deck("Single C3D15 wedge (frd pe15, type 5)", "C3D15", WEDGE15_NODES,
                             "1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15",
                             "1, 2, 3, 7, 8, 9", "4, 5, 6, 10, 11, 12"),
    "tet10_c3d10": solid_deck("Single C3D10 tetrahedron (frd tet10, type 6)", "C3D10", TET10_NODES,
                              "1, 2, 3, 4, 5, 6, 7, 8, 9, 10", "1, 2, 3, 5, 6, 7", "4"),
    "pe6_c3d6": solid_deck("Single C3D6 wedge (frd pe6, type 2)", "C3D6", "".join(WEDGE15_NODES.splitlines(True)[:6]),
                           "1, 2, 3, 4, 5, 6", "1, 2, 3", "4, 5, 6"),
    "tet4_c3d4": solid_deck("Single C3D4 tetrahedron (frd tet4, type 3)", "C3D4", "".join(TET10_NODES.splitlines(True)[:4]),
                            "1, 2, 3, 4", "1, 2, 3", "4"),
    "shell_s8_3d": shell_deck("Single S8 shell, expanded (frd he20, type 4)", "S8",
                              "1, 0., 0., 0.\n2, 1., 0., 0.\n3, 1., 1., 0.\n4, 0., 1., 0.\n5, 0.5, 0., 0.\n6, 1., 0.5, 0.\n7, 0.5, 1., 0.\n8, 0., 0.5, 0.\n",
                              "1, 2, 3, 4, 5, 6, 7, 8", "1, 4, 8", "2, 3, 6", "3D"),
    "shell_s8_2d": shell_deck("Single S8 shell, 2D output (frd qu8, type 10)", "S8",
                              "1, 0., 0., 0.\n2, 1., 0., 0.\n3, 1., 1., 0.\n4, 0., 1., 0.\n5, 0.5, 0., 0.\n6, 1., 0.5, 0.\n7, 0.5, 1., 0.\n8, 0., 0.5, 0.\n",
                              "1, 2, 3, 4, 5, 6, 7, 8", "1, 4, 8", "2, 3, 6", "2D"),
    "shell_s6_3d": shell_deck("Single S6 shell, expanded (frd pe15, type 5)", "S6",
                              "1, 0., 0., 0.\n2, 1., 0., 0.\n3, 0., 1., 0.\n4, 0.5, 0., 0.\n5, 0.5, 0.5, 0.\n6, 0., 0.5, 0.\n",
                              "1, 2, 3, 4, 5, 6", "1, 3, 6", "2", "3D"),
    "shell_s6_2d": shell_deck("Single S6 shell, 2D output (frd tr6, type 8)", "S6",
                              "1, 0., 0., 0.\n2, 1., 0., 0.\n3, 0., 1., 0.\n4, 0.5, 0., 0.\n5, 0.5, 0.5, 0.\n6, 0., 0.5, 0.\n",
                              "1, 2, 3, 4, 5, 6", "1, 3, 6", "2", "2D"),
    "beam_b32_3d": shell_deck("Single B32 beam, expanded (frd he20, type 4)", "B32",
                              "1, 0., 0., 0.\n2, 0.5, 0., 0.\n3, 1., 0., 0.\n", "1, 2, 3", "1", "3", "3D"),
    "beam_b32_2d": shell_deck("Single B32 beam, 2D output (frd be3, type 12)", "B32",
                              "1, 0., 0., 0.\n2, 0.5, 0., 0.\n3, 1., 0., 0.\n", "1, 2, 3", "1", "3", "2D"),
    "transient_heat": TRANSIENT_HEAT,
}


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--ccx", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)
    out = os.path.abspath(args.out)
    work = os.path.join(out, "_work")
    os.makedirs(work, exist_ok=True)
    failures = 0
    for name, deck in DECKS.items():
        deck_path = os.path.join(work, name + ".inp")
        with open(deck_path, "w", encoding="ascii", newline="\n") as handle:
            handle.write(deck)
        env = dict(os.environ)
        env.setdefault("OMP_NUM_THREADS", "1")
        result = subprocess.run([args.ccx, "-i", name], cwd=work, env=env, capture_output=True, text=True, timeout=600)
        frd = os.path.join(work, name + ".frd")
        if result.returncode != 0 or not os.path.isfile(frd):
            failures += 1
            print("FAILED", name, "exit", result.returncode)
            print(result.stdout[-1500:])
            print(result.stderr[-800:])
            continue
        shutil.copy(frd, os.path.join(out, name + ".frd"))
        shutil.copy(deck_path, os.path.join(out, name + ".inp"))
        print("ok", name, os.path.getsize(frd), "bytes")
    shutil.rmtree(work, ignore_errors=True)
    print("fixtures in", out, "failures:", failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
