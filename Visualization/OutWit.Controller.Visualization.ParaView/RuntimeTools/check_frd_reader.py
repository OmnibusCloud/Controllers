"""Proves the bundled .frd reader's element mapping on real CalculiX output (run with pvpython).

    pvpython check_frd_reader.py --plugin <omnibuscloud_frd_reader.py> <file.frd> [<file.frd> ...]
        [--expect name=vtkType,count ...] [--json <report.json>]

For every file: loads the reader plugin, reads every time step and checks
  - every cell passes vtkCellValidator (no intersecting edges, non-convexity, wrong point count),
  - every quadratic cell's mid-edge nodes sit at the midpoints of their VTK edges (the geometric proof
    that cgx's he20/pe15 ordering is remapped correctly — a wrong mid-side order fails here),
  - every 3D cell's parametric Jacobian is positive at its centre (an inverted wedge/hex/tet fails),
  - the result arrays have the expected shape (DISP 3 components, STRESS 6) and no NaN on mesh nodes,
  - time steps are strictly increasing and every step produces data.
Exit code 0 only when every check passes; --json writes the per-file report the tests compare.
"""

import argparse
import json
import math
import os
import sys


def check_file(path, expectations):
    import paraview.simple as ps
    from vtkmodules.vtkCommonDataModel import vtkCellTypes
    try:
        from vtkmodules.vtkCommonDataModel import vtkCellTypeUtilities
        type_name = vtkCellTypeUtilities.GetClassNameFromTypeId
    except ImportError:
        type_name = vtkCellTypes.GetClassNameFromTypeId
    from vtkmodules.vtkFiltersGeneral import vtkCellValidator
    from vtkmodules.util.numpy_support import vtk_to_numpy
    import numpy

    report = {"file": os.path.basename(path), "errors": [], "cells": {}, "timesteps": [], "arrays": {}}
    reader = ps.OmnibusCloudFrdReader(registrationName=os.path.basename(path), FileName=path)
    reader.UpdatePipelineInformation()
    times = reader.TimestepValues
    times = [times] if isinstance(times, (int, float)) else list(times)
    report["timesteps"] = times
    if any(times[i] >= times[i + 1] for i in range(len(times) - 1)):
        report["errors"].append("time steps are not strictly increasing: %s" % times)

    for time in times or [None]:
        if time is None:
            reader.UpdatePipeline()
        else:
            reader.UpdatePipeline(time)
        grid = ps.servermanager.Fetch(reader)
        if grid is None or grid.GetNumberOfPoints() == 0:
            report["errors"].append("no data at time %s" % time)
            continue
        points = vtk_to_numpy(grid.GetPoints().GetData())

        # cell census
        census = {}
        for c in range(grid.GetNumberOfCells()):
            cell_type = grid.GetCellType(c)
            census[type_name(cell_type)] = census.get(type_name(cell_type), 0) + 1
        report["cells"] = census

        # validity (bit 32 "FacesAreOrientedIncorrectly" is masked: vtkWedge's face table is inward in
        # VTK's own parametric frame, so correctly oriented wedges trip it; orientation is proven by
        # the Jacobian sign below instead)
        validator = vtkCellValidator()
        validator.SetInputData(grid)
        validator.Update()
        states = vtk_to_numpy(validator.GetOutput().GetCellData().GetArray("ValidityState")).astype(numpy.int64) & ~32
        invalid = [int(i) for i in numpy.nonzero(states != 0)[0]]
        if invalid:
            report["errors"].append("invalid cells at time %s: %s (states %s)" % (time, invalid[:10], [int(states[i]) for i in invalid[:10]]))

        # mid-edge nodes of quadratic cells
        for c in range(grid.GetNumberOfCells()):
            cell = grid.GetCell(c)
            if not cell.IsLinear():
                for e in range(cell.GetNumberOfEdges()):
                    edge = cell.GetEdge(e)
                    ids = [edge.GetPointId(k) for k in range(edge.GetNumberOfPoints())]
                    if len(ids) == 3:
                        p0, p1, pm = points[ids[0]], points[ids[1]], points[ids[2]]
                        if numpy.linalg.norm(pm - (p0 + p1) / 2.0) > 1e-6 * max(1.0, numpy.linalg.norm(p1 - p0)):
                            report["errors"].append("cell %d edge %d: mid-side node %d is not the edge midpoint (time %s)" % (c, e, ids[2], time))
                            break

        # orientation: the Jacobian of VTK's parametric map at the cell centre must be positive for
        # every 3D cell (a negative determinant is an inverted cell in VTK's own frame, whatever the
        # face table says)
        for c in range(grid.GetNumberOfCells()):
            cell = grid.GetCell(c)
            if cell.GetCellDimension() != 3:
                continue
            n = cell.GetNumberOfPoints()
            centre = [0.0, 0.0, 0.0]
            cell.GetParametricCenter(centre)
            derivatives = [0.0] * (3 * n)
            cell.InterpolationDerivs(centre, derivatives)
            xyz = numpy.array([cell.GetPoints().GetPoint(k) for k in range(n)], dtype=numpy.float64)
            jacobian = numpy.array(derivatives, dtype=numpy.float64).reshape(3, n) @ xyz
            determinant = float(numpy.linalg.det(jacobian))
            if not determinant > 0:
                report["errors"].append("cell %d is inverted: Jacobian determinant %r at the centre (time %s)" % (c, determinant, time))
                break

        # arrays
        point_data = grid.GetPointData()
        arrays = {}
        for a in range(point_data.GetNumberOfArrays()):
            array = point_data.GetArray(a)
            name = array.GetName()
            values = vtk_to_numpy(array)
            nan_count = int(numpy.isnan(values).sum()) if values.dtype.kind == "f" else 0
            arrays[name] = {"components": array.GetNumberOfComponents(), "nan": nan_count}
            if nan_count:
                report["errors"].append("array %s has %d NaN values at time %s" % (name, nan_count, time))
        report["arrays"] = arrays
        for name, components in (("DISP", 3), ("STRESS", 6)):
            if name in arrays and arrays[name]["components"] != components:
                report["errors"].append("%s has %d components, expected %d" % (name, arrays[name]["components"], components))

    for name, (vtk_type_name, count) in expectations.items():
        if report["cells"].get(vtk_type_name, 0) != count:
            report["errors"].append("expected %d %s cells, found %s" % (count, vtk_type_name, report["cells"]))
    ps.Delete(reader)
    return report


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--plugin", required=True)
    parser.add_argument("--expect", action="append", default=[], help="basename=vtkCellClassName,count")
    parser.add_argument("--json")
    parser.add_argument("files", nargs="+")
    args = parser.parse_args(argv)

    import paraview.simple as ps
    ps.LoadPlugin(os.path.abspath(args.plugin), remote=False, ns=globals())

    expectations = {}
    for spec in args.expect:
        key, value = spec.split("=", 1)
        type_name, count = value.split(",")
        expectations.setdefault(key, {})[type_name] = (type_name, int(count))

    reports = []
    failed = 0
    for path in args.files:
        per_file = {}
        for type_name, (t, count) in expectations.get(os.path.basename(path), {}).items():
            per_file[type_name] = (t, count)
        report = check_file(os.path.abspath(path), per_file)
        reports.append(report)
        status = "OK " if not report["errors"] else "FAIL"
        print("%s %-22s cells=%s steps=%d arrays=%s" % (status, report["file"], report["cells"], len(report["timesteps"]), sorted(report["arrays"])))
        for error in report["errors"]:
            print("     ", error)
        failed += 1 if report["errors"] else 0
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(reports, handle, indent=2)
    print("checked %d file(s), %d failed" % (len(reports), failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
