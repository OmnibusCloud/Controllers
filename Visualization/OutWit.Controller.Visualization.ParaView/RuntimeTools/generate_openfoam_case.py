"""Writes a small, complete OpenFOAM case without OpenFOAM: a structured hexahedral block
(nx x ny x nz cells) as constant/polyMesh in the ASCII format the native ParaView reader
(vtkOpenFOAMReader) parses, a system/ with the three dictionaries every case carries, and
volume fields (p scalar, U vector) at the written times. Optionally the last time moves the
mesh (its own polyMesh/points), which exercises the reader's moving-mesh lookup.

    python generate_openfoam_case.py --out <case-dir> [--cells 4 3 2] [--times 0 0.5 1] [--moving]

Plain Python (no ParaView): the corpus generator calls write_case() and the plugin's tests
build cases from it. Face ordering follows the OpenFOAM convention (internal faces first, in
upper-triangular order, then the boundary faces patch by patch, every face oriented out of
its owner), so the mesh is what blockMesh would have written for the same block.
"""
import argparse
import math
import os

HEADER = """FoamFile
{
    version     2.0;
    format      ascii;
    class       %(cls)s;
%(note)s    location    "%(location)s";
    object      %(obj)s;
}
"""

#: The patches of the block, in boundary-file order: which block face, its type.
PATCHES = (
    ("inlet", "patch", "x0"),
    ("outlet", "patch", "x1"),
    ("walls", "wall", "y"),
    ("frontAndBack", "patch", "z"),
)


def foam_file(cls, location, obj, body, note=None):
    note_line = ('    note        "%s";\n' % note) if note else ""
    return HEADER % {"cls": cls, "location": location, "obj": obj, "note": note_line} + "\n" + body + "\n"


def build_mesh(nx, ny, nz, dx=1.0, dy=1.0, dz=1.0):
    """Points, faces (point lists), owner, neighbour and patches of the block."""
    def point_index(i, j, k):
        return i + (nx + 1) * (j + (ny + 1) * k)

    def cell_index(i, j, k):
        return i + nx * (j + ny * k)

    points = [(i * dx, j * dy, k * dz)
              for k in range(nz + 1) for j in range(ny + 1) for i in range(nx + 1)]

    def x_face(i, j, k):  # the +x face of cell (i, j, k): normal +x
        return (point_index(i + 1, j, k), point_index(i + 1, j + 1, k), point_index(i + 1, j + 1, k + 1), point_index(i + 1, j, k + 1))

    def y_face(i, j, k):  # +y
        return (point_index(i, j + 1, k), point_index(i, j + 1, k + 1), point_index(i + 1, j + 1, k + 1), point_index(i + 1, j + 1, k))

    def z_face(i, j, k):  # +z
        return (point_index(i, j, k + 1), point_index(i + 1, j, k + 1), point_index(i + 1, j + 1, k + 1), point_index(i, j + 1, k + 1))

    def x0_face(j, k):  # the -x face of cell (0, j, k): normal -x
        return (point_index(0, j, k), point_index(0, j, k + 1), point_index(0, j + 1, k + 1), point_index(0, j + 1, k))

    def y0_face(i, k):  # -y
        return (point_index(i, 0, k), point_index(i + 1, 0, k), point_index(i + 1, 0, k + 1), point_index(i, 0, k + 1))

    def z0_face(i, j):  # -z
        return (point_index(i, j, 0), point_index(i, j + 1, 0), point_index(i + 1, j + 1, 0), point_index(i + 1, j, 0))

    faces = []
    owner = []
    neighbour = []
    # Internal faces in upper-triangular order: per cell, its faces to higher-numbered
    # neighbours (x+1, y+1, z+1 - increasing cell index).
    for k in range(nz):
        for j in range(ny):
            for i in range(nx):
                cell = cell_index(i, j, k)
                if i + 1 < nx:
                    faces.append(x_face(i, j, k)); owner.append(cell); neighbour.append(cell_index(i + 1, j, k))
                if j + 1 < ny:
                    faces.append(y_face(i, j, k)); owner.append(cell); neighbour.append(cell_index(i, j + 1, k))
                if k + 1 < nz:
                    faces.append(z_face(i, j, k)); owner.append(cell); neighbour.append(cell_index(i, j, k + 1))
    internal = len(faces)

    patches = []
    for name, kind, where in PATCHES:
        start = len(faces)
        if where == "x0":
            for k in range(nz):
                for j in range(ny):
                    faces.append(x0_face(j, k)); owner.append(cell_index(0, j, k))
        elif where == "x1":
            for k in range(nz):
                for j in range(ny):
                    faces.append(x_face(nx - 1, j, k)); owner.append(cell_index(nx - 1, j, k))
        elif where == "y":
            for k in range(nz):
                for i in range(nx):
                    faces.append(y0_face(i, k)); owner.append(cell_index(i, 0, k))
            for k in range(nz):
                for i in range(nx):
                    faces.append(y_face(i, ny - 1, k)); owner.append(cell_index(i, ny - 1, k))
        else:
            for j in range(ny):
                for i in range(nx):
                    faces.append(z0_face(i, j)); owner.append(cell_index(i, j, 0))
            for j in range(ny):
                for i in range(nx):
                    faces.append(z_face(i, j, nz - 1)); owner.append(cell_index(i, j, nz - 1))
        patches.append((name, kind, start, len(faces) - start))

    return {"points": points, "faces": faces, "owner": owner, "neighbour": neighbour,
            "internal": internal, "patches": patches, "cells": nx * ny * nz}


def format_list(items, formatter):
    return "%d\n(\n%s\n)" % (len(items), "\n".join(formatter(item) for item in items))


def write_polymesh(directory, mesh, location="constant/polyMesh", points=None):
    """Writes the mesh files (or only points when given: a moved mesh at a later time)."""
    os.makedirs(directory, exist_ok=True)
    point_list = points if points is not None else mesh["points"]
    write(os.path.join(directory, "points"), foam_file(
        "vectorField", location, "points",
        format_list(point_list, lambda p: "(%.6g %.6g %.6g)" % p)))
    if points is not None:
        return
    note = "nPoints:%d  nCells:%d  nFaces:%d  nInternalFaces:%d" % (
        len(mesh["points"]), mesh["cells"], len(mesh["faces"]), mesh["internal"])
    write(os.path.join(directory, "faces"), foam_file(
        "faceList", location, "faces",
        format_list(mesh["faces"], lambda f: "4(%d %d %d %d)" % f)))
    write(os.path.join(directory, "owner"), foam_file(
        "labelList", location, "owner", format_list(mesh["owner"], str), note=note))
    write(os.path.join(directory, "neighbour"), foam_file(
        "labelList", location, "neighbour", format_list(mesh["neighbour"], str), note=note))
    patches = "\n".join(
        "    %s\n    {\n        type            %s;\n        nFaces          %d;\n        startFace       %d;\n    }"
        % (name, kind, count, start) for name, kind, start, count in mesh["patches"])
    write(os.path.join(directory, "boundary"), foam_file(
        "polyBoundaryMesh", location, "boundary", "%d\n(\n%s\n)" % (len(mesh["patches"]), patches)))


def cell_centres(mesh, nx, ny, nz, points=None):
    points = points if points is not None else mesh["points"]
    centres = []
    for k in range(nz):
        for j in range(ny):
            for i in range(nx):
                corners = [i + (nx + 1) * (j + (ny + 1) * k), i + 1 + (nx + 1) * (j + (ny + 1) * k),
                           i + (nx + 1) * (j + 1 + (ny + 1) * k), i + (nx + 1) * (j + (ny + 1) * (k + 1))]
                centres.append(tuple(sum(points[c][axis] for c in corners) / 4.0 for axis in range(3)))
    return centres


def write_fields(directory, time_name, mesh, centres, time_value):
    """p = a travelling wave over x, U = a swirl - both change with the time value, so a
    frame per time differs (a render of the wrong time is visible, not silent)."""
    os.makedirs(directory, exist_ok=True)
    patches = "\n".join("    %s\n    {\n        type            zeroGradient;\n    }" % name
                        for name, _, _, _ in mesh["patches"])
    p_values = [math.sin(x - 2.0 * time_value) + 0.5 * y for x, y, _ in centres]
    write(os.path.join(directory, "p"), foam_file(
        "volScalarField", time_name, "p",
        "dimensions      [0 2 -2 0 0 0 0];\n\ninternalField   nonuniform List<scalar> %s;\n\nboundaryField\n{\n%s\n}"
        % (format_list(p_values, lambda v: "%.6g" % v), patches)))
    u_values = [(1.0 + time_value, math.cos(x) * time_value, 0.1 * z) for x, _, z in centres]
    write(os.path.join(directory, "U"), foam_file(
        "volVectorField", time_name, "U",
        "dimensions      [0 1 -1 0 0 0 0];\n\ninternalField   nonuniform List<vector> %s;\n\nboundaryField\n{\n%s\n}"
        % (format_list(u_values, lambda v: "(%.6g %.6g %.6g)" % v), patches)))


def write_system(directory, times):
    os.makedirs(directory, exist_ok=True)
    end_time = max(times) if times else 0
    write(os.path.join(directory, "controlDict"), foam_file(
        "dictionary", "system", "controlDict",
        "application     simpleFoam;\n\nstartFrom       startTime;\n\nstartTime       0;\n\nstopAt          endTime;\n\n"
        "endTime         %g;\n\ndeltaT          0.5;\n\nwriteControl    timeStep;\n\nwriteInterval   1;\n\n"
        "writeFormat     ascii;\n\nwritePrecision  6;\n\ntimeFormat      general;\n\ntimePrecision   6;\n" % end_time))
    write(os.path.join(directory, "fvSchemes"), foam_file(
        "dictionary", "system", "fvSchemes",
        "ddtSchemes\n{\n    default         steadyState;\n}\n\ngradSchemes\n{\n    default         Gauss linear;\n}\n\n"
        "divSchemes\n{\n    default         none;\n}\n\nlaplacianSchemes\n{\n    default         Gauss linear corrected;\n}\n"))
    write(os.path.join(directory, "fvSolution"), foam_file(
        "dictionary", "system", "fvSolution",
        "solvers\n{\n    p\n    {\n        solver          GAMG;\n        tolerance       1e-06;\n        relTol          0.1;\n    }\n}\n"))


def write_constant(directory):
    os.makedirs(directory, exist_ok=True)
    write(os.path.join(directory, "transportProperties"), foam_file(
        "dictionary", "constant", "transportProperties",
        "transportModel  Newtonian;\n\nnu              [0 2 -1 0 0 0 0] 1e-05;\n"))


def time_name(value):
    return ("%g" % value)


def write_case(root, cells=(4, 3, 2), times=(0.0, 0.5, 1.0), moving=True, stub="box.foam"):
    """Writes the whole case under root; returns the relative paths written."""
    nx, ny, nz = cells
    mesh = build_mesh(nx, ny, nz)
    written = []

    def record(path):
        written.append(os.path.relpath(path, root).replace(os.sep, "/"))

    write_system(os.path.join(root, "system"), times)
    write_constant(os.path.join(root, "constant"))
    write_polymesh(os.path.join(root, "constant", "polyMesh"), mesh)
    centres = cell_centres(mesh, nx, ny, nz)
    for value in times:
        name = time_name(value)
        directory = os.path.join(root, name)
        if moving and value == max(times) and len(times) > 1:
            # The last time moves the mesh: the block stretches along z.
            moved = [(x, y, z * 1.5) for x, y, z in mesh["points"]]
            write_polymesh(os.path.join(directory, "polyMesh"), mesh, location=name + "/polyMesh", points=moved)
            centres_now = cell_centres(mesh, nx, ny, nz, moved)
        else:
            centres_now = centres
        write_fields(directory, name, mesh, centres_now, value)
    if stub:
        # Not empty: the cloud refuses an empty blob, and a real client's plugin replaces an
        # empty stub with a one-line stand-in - the corpus carries what actually travels.
        write(os.path.join(root, stub), "OpenFOAM case stub (fixture); the reader opens the case beside it.
")

    for directory, _, names in os.walk(root):
        for name in names:
            record(os.path.join(directory, name))
    return sorted(written)


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--cells", nargs=3, type=int, default=(4, 3, 2))
    parser.add_argument("--times", nargs="+", type=float, default=(0.0, 0.5, 1.0))
    parser.add_argument("--moving", action="store_true")
    parser.add_argument("--stub", default="box.foam")
    args = parser.parse_args(argv)
    written = write_case(os.path.abspath(args.out), tuple(args.cells), tuple(args.times), args.moving, args.stub)
    for path in written:
        print(path)
    return 0


if __name__ == "__main__":
    import sys
    sys.exit(main(sys.argv[1:]))
