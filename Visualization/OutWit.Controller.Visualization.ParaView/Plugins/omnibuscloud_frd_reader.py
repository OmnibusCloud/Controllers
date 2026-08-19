# -*- coding: utf-8 -*-
"""OmnibusCloud CalculiX .frd reader for ParaView (single-file VTKPythonAlgorithm plugin).

The byte-identical file is loaded by the OmnibusCloud ParaView plugin on the desktop and by the
controller's runner on the render nodes (docs 03, section 6): one implementation serves both, stays
out of any C++ ABI matrix and tolerates ParaView releases.

Format: the cgx "Result Format" (cgx manual chapter 11) as written by CalculiX ccx — ASCII short or
long records; the binary variants (FORMAT 2/3) are rejected with a clear error (ccx never writes them).
Nodes (2C), elements (3C) with the cgx element types 1..12 mapped to VTK cells (mid-side nodes
reordered where the cgx numbering differs from VTK's: he20, pe15), nodal results (100C/100CL) as
point arrays named after the dataset (DISP, STRESS, NDTEMP, ...), one time step per result step.
Components flagged IEXIST != 0 (cgx-computed, such as the "ALL" magnitude) carry no data and are
skipped. Nodes a results block does not mention get NaN.

Time: the step VALUE (time / frequency / load factor) becomes the time step value when the values are
strictly increasing; otherwise (repeated frequencies of degenerate modes, static steps all at 1.0)
the 1-based step ordinal is used and the actual value is kept in field data "StepValue".
"""

__version__ = "1.0.0"

paraview_plugin_name = "OmnibusCloudFrdReader"
paraview_plugin_version = __version__

import os

try:
    import numpy
except ImportError:  # pragma: no cover - numpy ships with every ParaView
    numpy = None


# ---------------------------------------------------------------------------------------------------
# Element mapping (cgx manual, chapter 10 "Element Types" — node numbering of the frd file)
# ---------------------------------------------------------------------------------------------------

VTK_VERTEX = 1
VTK_POLY_VERTEX = 2
VTK_LINE = 3
VTK_TRIANGLE = 5
VTK_QUAD = 9
VTK_TETRA = 10
VTK_HEXAHEDRON = 12
VTK_WEDGE = 13
VTK_QUADRATIC_EDGE = 21
VTK_QUADRATIC_TRIANGLE = 22
VTK_QUADRATIC_QUAD = 23
VTK_QUADRATIC_TETRA = 24
VTK_QUADRATIC_HEXAHEDRON = 25
VTK_QUADRATIC_WEDGE = 26

# frd type -> (vtk cell type, node count, frd-index order producing the VTK node order or None)
#   he20 (4): cgx lists corners 1-8, bottom mid-edges 9-12, VERTICAL mid-edges 13-16, top mid-edges
#             17-20; VTK wants the top mid-edges before the vertical ones (verified on ccx output by
#             RuntimeTools/check_frd_reader.py: every mid-side node at its VTK edge midpoint).
#   pe6 (2):  cgx's base triangle is counter-clockwise seen from the top face - the orientation of
#             VTK's wedge parametric map (positive Jacobian) and of VTK's own CGNS reader (PENTA_6 ->
#             VTK_WEDGE unchanged); no corner swap. (vtkCellValidator's "faces oriented incorrectly"
#             for such wedges is a VTK quirk of vtkWedge's face table, not an inversion.)
#   pe15 (5): cgx mid-edges are bottom 7-9, VERTICAL 10-12, top 13-15 (the CGNS PENTA_15 order);
#             VTK wants bottom, top, vertical - the CGNS reader's remap.
ELEMENT_TYPES = {
    1: (VTK_HEXAHEDRON, 8, None),
    2: (VTK_WEDGE, 6, None),
    3: (VTK_TETRA, 4, None),
    4: (VTK_QUADRATIC_HEXAHEDRON, 20, (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 16, 17, 18, 19, 12, 13, 14, 15)),
    5: (VTK_QUADRATIC_WEDGE, 15, (0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 13, 14, 9, 10, 11)),
    6: (VTK_QUADRATIC_TETRA, 10, None),
    7: (VTK_TRIANGLE, 3, None),
    8: (VTK_QUADRATIC_TRIANGLE, 6, None),
    9: (VTK_QUAD, 4, None),
    10: (VTK_QUADRATIC_QUAD, 8, None),
    11: (VTK_LINE, 2, None),
    12: (VTK_QUADRATIC_EDGE, 3, None),
}

ANALYSIS_TYPES = {0: "static", 1: "time step", 2: "frequency", 3: "load step", 4: "user named"}


class FrdFormatError(Exception):
    """The file is not a readable ASCII frd file."""


# ---------------------------------------------------------------------------------------------------
# Parsed model
# ---------------------------------------------------------------------------------------------------

class FrdDataset(object):
    """One -4 attribute header: a named result with its components at one step."""

    __slots__ = ("name", "components", "values")

    def __init__(self, name, components):
        self.name = name
        self.components = components  # list of (component name, ictype, iexist)
        self.values = None            # numpy (node count, data columns)

    @property
    def data_components(self):
        return [c for c in self.components if c[2] == 0]


class FrdStep(object):
    """One result step (NUMSTP): its value, analysis type and datasets."""

    __slots__ = ("number", "value", "ictype", "analysis", "datasets")

    def __init__(self, number, value, ictype, analysis):
        self.number = number
        self.value = value
        self.ictype = ictype
        self.analysis = analysis
        self.datasets = []


class FrdModel(object):
    """Nodes, elements and result steps of one frd file."""

    def __init__(self):
        self.header = []                # 1U user header strings
        self.node_numbers = None        # numpy int64 (n,)
        self.coordinates = None         # numpy float64 (n, 3)
        self.node_index = {}            # node number -> row
        self.element_numbers = []       # python lists until finalised
        self.element_types = []
        self.element_groups = []
        self.element_materials = []
        self.connectivity = []          # list of lists of node rows (VTK order)
        self.cell_types = []
        self.steps = []                 # FrdStep in file order
        self.unknown_element_types = set()

    # -- time ----------------------------------------------------------------------------------

    def timestep_values(self):
        values = [s.value for s in self.steps]
        strictly_increasing = all(values[i] < values[i + 1] for i in range(len(values) - 1))
        if strictly_increasing and values:
            return values
        return [float(i + 1) for i in range(len(values))]

    def step_for_time(self, time):
        """The step whose time step value is the greatest one not above `time` (first when None)."""
        if not self.steps:
            return None
        times = self.timestep_values()
        if time is None:
            return self.steps[0]
        chosen = 0
        for i, t in enumerate(times):
            if t <= time + 1e-12:
                chosen = i
            else:
                break
        return self.steps[chosen]

    def dataset_names(self):
        names = []
        for step in self.steps:
            for dataset in step.datasets:
                if dataset.name not in names:
                    names.append(dataset.name)
        return names


# ---------------------------------------------------------------------------------------------------
# Parser (fixed-column records; see cgx manual 11.3, 11.4, 11.6)
# ---------------------------------------------------------------------------------------------------

def _int(text, default=0):
    text = text.strip()
    return int(text) if text else default


def _float(text):
    text = text.strip()
    if not text:
        return float("nan")
    try:
        return float(text)
    except ValueError:
        # Fortran may print exponents without the 'E' (1.0-100) or NaN/Inf spellings.
        lowered = text.lower()
        if "nan" in lowered:
            return float("nan")
        if "inf" in lowered:
            return float("-inf") if lowered.startswith("-") else float("inf")
        for i in range(1, len(text)):
            if text[i] in "+-" and text[i - 1] not in "eE":
                return float(text[:i] + "E" + text[i:])
        raise


def _format_flag(header):
    """FORMAT indicator of a 2C/3C/100C header: columns 73-74 (0 short, 1 long, 2/3 binary)."""
    return _int(header[73:75]) if len(header) > 73 else 0


def parse_frd(path):
    """Parses an ASCII frd file into an FrdModel."""
    model = FrdModel()
    with open(path, "r", encoding="latin-1", errors="replace") as handle:
        lines = handle.read().split("\n")
    _parse_lines(lines, model)
    _finalise(model)
    return model


def _parse_lines(lines, model):
    """Dispatches the record keys: ccx writes block keys right-aligned in five columns ("    1C",
    "    2C", "  100CL"), data keys in two (" -1", " -3")."""
    n = len(lines)
    i = 0
    pending_parameters = []
    while i < n:
        line = lines[i]
        head = line[:6].strip()
        if not head:
            i += 1
        elif head.startswith("1U"):
            model.header.append(line[6:].rstrip())
            i += 1
        elif head.startswith("1P"):
            pending_parameters.append(line[6:].rstrip())
            i += 1
        elif head == "2C":
            i = _parse_nodes(lines, i, model)
        elif head == "3C":
            i = _parse_elements(lines, i, model)
        elif head.startswith("100C"):
            i = _parse_results(lines, i, model, pending_parameters)
            pending_parameters = []
        elif head == "9999":
            break
        else:
            i += 1


def _parse_nodes(lines, i, model):
    header = lines[i]
    fmt = _format_flag(header)
    if fmt >= 2:
        raise FrdFormatError("binary frd node blocks (FORMAT %d) are not supported; write ASCII results" % fmt)
    long_format = fmt == 1
    id_width = 10 if long_format else 5
    numbers = []
    coordinates = []
    i += 1
    while i < len(lines):
        line = lines[i]
        record = line[1:3]
        if record == "-3":
            i += 1
            break
        if record == "-1":
            start = 3
            numbers.append(_int(line[start:start + id_width]))
            start += id_width
            coordinates.append((_float(line[start:start + 12]), _float(line[start + 12:start + 24]), _float(line[start + 24:start + 36])))
        else:
            break
        i += 1
    model.node_numbers = numpy.array(numbers, dtype=numpy.int64) if numpy is not None else numbers
    model.coordinates = numpy.array(coordinates, dtype=numpy.float64).reshape(-1, 3) if numpy is not None else coordinates
    model.node_index = {number: row for row, number in enumerate(numbers)}
    return i


def _parse_elements(lines, i, model):
    header = lines[i]
    fmt = _format_flag(header)
    if fmt >= 2:
        raise FrdFormatError("binary frd element blocks (FORMAT %d) are not supported; write ASCII results" % fmt)
    long_format = fmt == 1
    id_width = 10 if long_format else 5
    per_line = 10 if long_format else 15
    node_index = model.node_index
    i += 1
    while i < len(lines):
        line = lines[i]
        record = line[1:3]
        if record == "-3":
            i += 1
            break
        if record != "-1":
            break
        number = _int(line[3:3 + id_width])
        rest = line[3 + id_width:]
        element_type = _int(rest[0:5])
        group = _int(rest[5:10])
        material = _int(rest[10:15])
        i += 1
        nodes = []
        while i < len(lines) and lines[i][1:3] == "-2":
            body = lines[i][3:]
            for k in range(per_line):
                field = body[k * id_width:(k + 1) * id_width]
                if not field.strip():
                    continue
                nodes.append(_int(field))
            i += 1
        mapping = ELEMENT_TYPES.get(element_type)
        if mapping is None:
            model.unknown_element_types.add(element_type)
            cell_type, order = VTK_POLY_VERTEX, None
            used = nodes
        else:
            cell_type, node_count, order = mapping
            used = nodes[:node_count]
            if len(used) < node_count:
                # A truncated element cannot be a valid cell of its type; keep its points visible.
                cell_type, order = VTK_POLY_VERTEX, None
        if order is not None:
            used = [used[k] for k in order]
        try:
            rows = [node_index[n] for n in used]
        except KeyError as missing:
            raise FrdFormatError("element %d references unknown node %s" % (number, missing))
        model.element_numbers.append(number)
        model.element_types.append(element_type)
        model.element_groups.append(group)
        model.element_materials.append(material)
        model.connectivity.append(rows)
        model.cell_types.append(cell_type)
    return i


def _parse_results(lines, i, model, parameters):
    header = lines[i]
    # (1X,' 100','C',6A1,E12.5,I12,20A1,I2,I5,10A1,I2): value [12:24], numnod [24:36], text [36:56],
    # ictype [56:58], numstp [58:63], analys [63:73], format [73:75]
    value = _float(header[12:24])
    ictype = _int(header[56:58])
    step_number = _int(header[58:63])
    analysis = header[63:73].strip()
    fmt = _format_flag(header)
    if fmt >= 2:
        raise FrdFormatError("binary frd result blocks (FORMAT %d) are not supported; write ASCII results" % fmt)
    long_format = fmt == 1
    id_width = 10 if long_format else 5
    i += 1
    # -4 attribute header
    if i >= len(lines) or lines[i][1:3] != "-4":
        raise FrdFormatError("result block at line %d has no -4 attribute header" % (i + 1))
    name = lines[i][5:13].strip()
    ncomps = _int(lines[i][13:18])
    irtype = _int(lines[i][18:23])
    i += 1
    components = []
    while i < len(lines) and lines[i][1:3] == "-5":
        line = lines[i]
        component_name = line[5:13].strip()
        component_type = _int(line[18:23])
        iexist = _int(line[33:38]) if len(line) >= 34 else 0
        components.append((component_name, component_type, iexist))
        i += 1
    while i < len(lines) and lines[i][1:3] == "-6":
        i += 1
    dataset = FrdDataset(name, components)
    columns = len(dataset.data_components)
    node_count = len(model.node_index)
    values = numpy.full((node_count, max(columns, 1)), numpy.nan, dtype=numpy.float64)
    node_index = model.node_index
    material_dependent = irtype == 2
    while i < len(lines):
        line = lines[i]
        record = line[1:3]
        if record == "-3":
            i += 1
            break
        if record != "-1":
            break
        node = _int(line[3:3 + id_width])
        row = node_index.get(node)
        if material_dependent:
            # -1 NODENR NMATS, then -2 MAT values... : take the first material's values
            i += 1
            data = []
            while i < len(lines) and lines[i][1:3] == "-2" and len(data) < columns:
                body = lines[i][3 + id_width:]
                data.extend(_float(body[k * 12:(k + 1) * 12]) for k in range(min(6, columns - len(data))))
                i += 1
        else:
            body = line[3 + id_width:]
            data = [_float(body[k * 12:(k + 1) * 12]) for k in range(min(6, columns))]
            i += 1
            while len(data) < columns and i < len(lines) and lines[i][1:3] == "-2":
                body = lines[i][3 + id_width:]
                data.extend(_float(body[k * 12:(k + 1) * 12]) for k in range(min(6, columns - len(data))))
                i += 1
        if row is not None and columns:
            values[row, :len(data)] = data[:columns]
    dataset.values = values
    step = None
    for existing in model.steps:
        if existing.number == step_number and existing.value == value:
            step = existing
            break
    if step is None:
        step = FrdStep(step_number, value, ictype, analysis)
        model.steps.append(step)
    step.datasets.append(dataset)
    return i


def _finalise(model):
    if model.node_numbers is None:
        model.node_numbers = numpy.zeros((0,), dtype=numpy.int64)
        model.coordinates = numpy.zeros((0, 3), dtype=numpy.float64)


# ---------------------------------------------------------------------------------------------------
# VTK construction
# ---------------------------------------------------------------------------------------------------

def build_grid(model, step, selected_names=None):
    """Builds a vtkUnstructuredGrid for one step (or the mesh only when step is None)."""
    from vtkmodules.vtkCommonCore import vtkPoints, vtkStringArray
    from vtkmodules.vtkCommonDataModel import vtkCellArray, vtkUnstructuredGrid
    from vtkmodules.util.numpy_support import numpy_to_vtk, numpy_to_vtkIdTypeArray

    grid = vtkUnstructuredGrid()
    points = vtkPoints()
    points.SetData(numpy_to_vtk(numpy.ascontiguousarray(model.coordinates, dtype=numpy.float64), deep=1))
    grid.SetPoints(points)

    cell_count = len(model.connectivity)
    if cell_count:
        sizes = numpy.fromiter((len(c) for c in model.connectivity), dtype=numpy.int64, count=cell_count)
        offsets = numpy.zeros(cell_count + 1, dtype=numpy.int64)
        numpy.cumsum(sizes, out=offsets[1:])
        connectivity = numpy.fromiter((row for cell in model.connectivity for row in cell), dtype=numpy.int64, count=int(offsets[-1]))
        cells = vtkCellArray()
        cells.SetData(numpy_to_vtkIdTypeArray(offsets, deep=1), numpy_to_vtkIdTypeArray(connectivity, deep=1))
        types = numpy_to_vtk(numpy.array(model.cell_types, dtype=numpy.uint8), deep=1)
        grid.SetCells(types, cells)

    point_data = grid.GetPointData()
    node_numbers = numpy_to_vtkIdTypeArray(numpy.ascontiguousarray(model.node_numbers, dtype=numpy.int64), deep=1)
    node_numbers.SetName("NodeNumber")
    point_data.AddArray(node_numbers)

    cell_data = grid.GetCellData()
    for name, values in (("ElementNumber", model.element_numbers), ("ElementType", model.element_types),
                         ("ElementGroup", model.element_groups), ("Material", model.element_materials)):
        array = numpy_to_vtk(numpy.array(values, dtype=numpy.int32), deep=1)
        array.SetName(name)
        cell_data.AddArray(array)

    field_data = grid.GetFieldData()
    if step is not None:
        for dataset in step.datasets:
            if selected_names is not None and dataset.name not in selected_names:
                continue
            columns = dataset.data_components
            if not columns or dataset.values is None:
                continue
            array = numpy_to_vtk(numpy.ascontiguousarray(dataset.values[:, :len(columns)]), deep=1)
            array.SetName(dataset.name)
            for k, (component_name, _, _) in enumerate(columns):
                array.SetComponentName(k, component_name)
            point_data.AddArray(array)
        step_number = numpy_to_vtk(numpy.array([step.number], dtype=numpy.int32), deep=1)
        step_number.SetName("StepNumber")
        field_data.AddArray(step_number)
        step_value = numpy_to_vtk(numpy.array([step.value], dtype=numpy.float64), deep=1)
        step_value.SetName("StepValue")
        field_data.AddArray(step_value)
        analysis = vtkStringArray()
        analysis.SetName("AnalysisType")
        analysis.InsertNextValue(step.analysis or ANALYSIS_TYPES.get(step.ictype, str(step.ictype)))
        field_data.AddArray(analysis)
    return grid


# ---------------------------------------------------------------------------------------------------
# ParaView plugin surface
# ---------------------------------------------------------------------------------------------------

try:
    from paraview.util.vtkAlgorithm import smproxy, smproperty, smdomain, smhint, VTKPythonAlgorithmBase
    _PARAVIEW = True
except ImportError:  # plain python (tests of the parser)
    _PARAVIEW = False


if _PARAVIEW:
    from vtkmodules.vtkCommonCore import vtkDataArraySelection
    from vtkmodules.vtkCommonDataModel import vtkUnstructuredGrid
    from vtkmodules.vtkCommonExecutionModel import vtkStreamingDemandDrivenPipeline

    def _modified_callback(algorithm):
        import weakref
        weak = weakref.ref(algorithm)

        def callback(caller, event):
            target = weak()
            if target is not None:
                target.Modified()
        return callback

    # The label also names the paraview.simple constructor (ParaView derives it from the label), so it
    # spells the proxy name: OmnibusCloudFrdReader everywhere — XML type, plugin name, allowlist key.
    @smproxy.reader(name="OmnibusCloudFrdReader", label="OmnibusCloud Frd Reader",
                    extensions="frd", file_description="CalculiX result files (OmnibusCloud reader)")
    class OmnibusCloudFrdReader(VTKPythonAlgorithmBase):
        """Reads CalculiX .frd results as a time-dependent unstructured grid."""

        def __init__(self):
            VTKPythonAlgorithmBase.__init__(self, nInputPorts=0, nOutputPorts=1, outputType="vtkUnstructuredGrid")
            self._filename = None
            self._model = None
            self._arrays = vtkDataArraySelection()
            self._arrays.AddObserver("ModifiedEvent", _modified_callback(self))

        # -- properties ------------------------------------------------------------------------

        @smproperty.stringvector(name="FileName", panel_visibility="never")
        @smdomain.filelist()
        @smhint.filechooser(extensions="frd", file_description="CalculiX result files")
        def SetFileName(self, name):
            if self._filename != name:
                self._filename = name
                self._model = None
                self.Modified()

        @smproperty.doublevector(name="TimestepValues", information_only="1", si_class="vtkSITimeStepsProperty")
        def GetTimestepValues(self):
            model = self._load()
            return model.timestep_values() if model is not None else []

        @smproperty.dataarrayselection(name="PointArrays")
        def GetDataArraySelection(self):
            self._load()
            return self._arrays

        # -- pipeline --------------------------------------------------------------------------

        def RequestInformation(self, request, inInfoVec, outInfoVec):
            executive = self.GetExecutive()
            outInfo = outInfoVec.GetInformationObject(0)
            outInfo.Remove(executive.TIME_STEPS())
            outInfo.Remove(executive.TIME_RANGE())
            model = self._load()
            if model is None:
                return 0
            times = model.timestep_values()
            if times:
                for t in times:
                    outInfo.Append(executive.TIME_STEPS(), t)
                outInfo.Append(executive.TIME_RANGE(), times[0])
                outInfo.Append(executive.TIME_RANGE(), times[-1])
            return 1

        def RequestData(self, request, inInfoVec, outInfoVec):
            from vtkmodules.vtkCommonDataModel import vtkDataObject
            model = self._load()
            if model is None:
                return 0
            outInfo = outInfoVec.GetInformationObject(0)
            output = vtkUnstructuredGrid.GetData(outInfoVec, 0)
            time = None
            if outInfo.Has(vtkStreamingDemandDrivenPipeline.UPDATE_TIME_STEP()):
                time = outInfo.Get(vtkStreamingDemandDrivenPipeline.UPDATE_TIME_STEP())
            step = model.step_for_time(time)
            selected = [name for name in model.dataset_names() if self._arrays.ArrayIsEnabled(name)]
            grid = build_grid(model, step, selected)
            output.ShallowCopy(grid)
            if step is not None:
                times = model.timestep_values()
                output.GetInformation().Set(vtkDataObject.DATA_TIME_STEP(), times[model.steps.index(step)])
            return 1

        # -- tools -----------------------------------------------------------------------------

        def _load(self):
            if self._model is not None:
                return self._model
            if not self._filename or not os.path.isfile(self._filename):
                return None
            try:
                model = parse_frd(self._filename)
            except FrdFormatError as error:
                from paraview import print_error
                print_error("OmnibusCloudFrdReader: %s: %s" % (self._filename, error))
                return None
            if model.unknown_element_types:
                from paraview import print_warning
                print_warning("OmnibusCloudFrdReader: unknown frd element type(s) %s kept as poly-vertices" % sorted(model.unknown_element_types))
            for name in model.dataset_names():
                if not self._arrays.ArrayExists(name):
                    self._arrays.AddArray(name)
            self._model = model
            return model
