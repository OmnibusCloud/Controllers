"""Saves the corpus scenes from the ParaView GUI (the client the plugin lives in), not from pvpython.

    paraview --disable-registry --script=generate_gui_states.py     (with OUTWIT_GUI_CORPUS=<corpus-dir>[, OUTWIT_GUI_PLUGIN=<reader.py>])

(--script runs a Python file in the GUI's embedded shell; --test-script does not execute plain Python.
 The script ends the GUI process itself once every state is written.)

pvpython-saved states are what the corpus generator produces; a real client's package carries a state
the GUI saved, which additionally contains the GUI's own proxies (settings, layouts, animation helpers,
selection/colour legend scaffolding). This script runs INSIDE the GUI and, for every corpus state (core
and reader), loads it and saves it again — core states as <corpus>/states/gui/<name>.pvsm, reader
states as <corpus>/states/OmnibusCloudFrdReader/gui_<name>.pvsm — plus scenes built from scratch in
the GUI the way users build them (states/gui/gui_native, gui_filters, gui_chart: colour legend, text
and time annotations, the common filters, a second chart view in a split layout). The allowlist
generator treats states/gui/ as core fixtures, so whatever the GUI adds is allowlisted by generation,
not by guesswork; the corpus validation and real-runtime tests then prove GUI-saved states pass and
render. Needs the full distribution (paraview.exe is trimmed from the runtime) and a desktop session.
"""

import os
import sys
import traceback


def rewrite_paths(state_path, corpus):
    """Absolute corpus data paths the GUI wrote back -> the package's logical paths."""
    with open(state_path, "r", encoding="utf-8") as handle:
        text = handle.read()
    forward = corpus.replace("\\", "/").rstrip("/") + "/"
    backward = forward.replace("/", "\\")
    text = text.replace(forward, "").replace(backward, "")
    with open(state_path, "w", encoding="utf-8") as handle:
        handle.write(text)


def main():
    from paraview import simple as pv
    corpus = os.environ.get("OUTWIT_GUI_CORPUS")
    if not corpus:
        print("generate_gui_states: OUTWIT_GUI_CORPUS is not set")
        return 2
    corpus = os.path.abspath(corpus)
    os.chdir(corpus)
    states_dir = os.path.join(corpus, "states")
    out_dir = os.path.join(states_dir, "gui")
    reader_dir = os.path.join(states_dir, "OmnibusCloudFrdReader")
    os.makedirs(out_dir, exist_ok=True)
    plugin = os.environ.get("OUTWIT_GUI_PLUGIN")
    wavelet_file = os.path.join(corpus, "data", "wavelet.vti").replace("\\", "/")
    log = open(os.path.join(out_dir, "generate_gui_states.log"), "w", encoding="utf-8")

    def say(*parts):
        text = " ".join(str(p) for p in parts)
        print(text)
        log.write(text + "\n")
        log.flush()

    try:
        say("ParaView", pv.GetParaViewSourceVersion(), "corpus", corpus)
        sources = []
        for name in sorted(f for f in os.listdir(states_dir) if f.endswith(".pvsm")):
            sources.append((name, os.path.join(states_dir, name), False))
        if plugin and os.path.isdir(reader_dir):
            pv.LoadPlugin(os.path.abspath(plugin), remote=False, ns=globals())
            for name in sorted(f for f in os.listdir(reader_dir) if f.endswith(".pvsm") and not f.startswith("gui_")):
                sources.append((name, os.path.join(reader_dir, name), True))

        for name, path, uses_reader in sources:
            try:
                pv.ResetSession()
                if uses_reader and plugin:
                    pv.LoadPlugin(os.path.abspath(plugin), remote=False, ns=globals())
                pv.LoadState(path)
                view = pv.GetActiveViewOrCreate("RenderView")
                pv.Render(view)
                target = os.path.join(reader_dir, "gui_" + name) if uses_reader else os.path.join(out_dir, name)
                pv.SaveState(target)
                rewrite_paths(target, corpus)
                say("saved", target)
            except Exception as error:  # noqa: BLE001 - report every failure, keep going
                say("FAILED", name, error)
                say(traceback.format_exc())

        # Scenes built natively in the GUI session, the way users build them.
        def gui_native(view):
            wavelet = pv.Wavelet(registrationName="Wavelet1")
            wavelet.WholeExtent = [-5, 5, -5, 5, -5, 5]
            contour = pv.Contour(registrationName="Contour1", Input=wavelet, ContourBy=["POINTS", "RTData"], Isosurfaces=[157.0])
            display = pv.Show(contour, view)
            pv.ColorBy(display, ("POINTS", "RTData"))
            display.SetScalarBarVisibility(view, True)
            pv.Show(wavelet, view).SetRepresentationType("Outline")

        def gui_filters(view):
            reader = pv.XMLImageDataReader(registrationName="wavelet.vti", FileName=[wavelet_file])
            threshold = pv.Threshold(registrationName="Threshold1", Input=reader, Scalars=["POINTS", "RTData"], LowerThreshold=100.0, UpperThreshold=220.0)
            surface = pv.ExtractSurface(registrationName="ExtractSurface1", Input=threshold)
            surface_display = pv.Show(surface, view)
            pv.ColorBy(surface_display, ("POINTS", "RTData"))
            surface_display.SetScalarBarVisibility(view, True)
            calculator = pv.Calculator(registrationName="Calculator1", Input=reader, ResultArrayName="Doubled", Function="RTData*2")
            slice_ = pv.Slice(registrationName="Slice1", Input=calculator)
            slice_.SliceType = "Plane"
            slice_.SliceType.Normal = [0.0, 0.0, 1.0]
            slice_display = pv.Show(slice_, view)
            pv.ColorBy(slice_display, ("POINTS", "Doubled"))
            gradient = pv.Gradient(registrationName="Gradient1", Input=reader)
            gradient.ScalarArray = ["POINTS", "RTData"]
            tracer = pv.StreamTracer(registrationName="StreamTracer1", Input=gradient, SeedType="Line")
            tracer.Vectors = ["POINTS", "Gradient"]
            tracer.MaximumStreamlineLength = 10.0
            tube = pv.Tube(registrationName="Tube1", Input=tracer, Radius=0.1)
            pv.Show(tube, view)
            cell_to_point = pv.CellDatatoPointData(registrationName="CellDatatoPointData1", Input=reader)
            pv.Show(cell_to_point, view).SetRepresentationType("Outline")
            text = pv.Text(registrationName="Text1", Text="OmnibusCloud corpus")
            pv.Show(text, view)
            annotate_time = pv.AnnotateTimeFilter(registrationName="AnnotateTimeFilter1", Input=reader)
            pv.Show(annotate_time, view)
            view.AxesGrid.Visibility = 1
            view.OrientationAxesVisibility = 1

        def gui_chart(view):
            reader = pv.XMLImageDataReader(registrationName="wavelet.vti", FileName=[wavelet_file])
            display = pv.Show(reader, view)
            pv.ColorBy(display, ("POINTS", "RTData"))
            plot = pv.PlotOverLine(registrationName="PlotOverLine1", Input=reader)
            plot.Point1 = [-5.0, 0.0, 0.0]
            plot.Point2 = [5.0, 0.0, 0.0]
            pv.Show(plot, view)
            layout = pv.GetLayout(view)
            layout.SplitHorizontal(0, 0.6)
            chart = pv.CreateView("XYChartView")
            pv.AssignViewToLayout(view=chart, layout=layout, hint=2)
            chart_display = pv.Show(plot, chart, "XYChartRepresentation")
            chart_display.SeriesVisibility = ["RTData"]
            pv.SetActiveView(view)

        for name, build in (("gui_native", gui_native), ("gui_filters", gui_filters), ("gui_chart", gui_chart)):
            try:
                pv.ResetSession()
                view = pv.GetActiveViewOrCreate("RenderView")
                build(view)
                pv.ResetCamera(view)
                pv.Render(view)
                target = os.path.join(out_dir, name + ".pvsm")
                pv.SaveState(target)
                rewrite_paths(target, corpus)
                say("saved", target)
            except Exception as error:  # noqa: BLE001
                say("FAILED", name, error)
                say(traceback.format_exc())
        say("done")
        return 0
    finally:
        log.close()


# --script executes this file inside the GUI's embedded interpreter (__name__ is not "__main__"):
# run unconditionally, then end the GUI process — sys.exit would only end the shell command.
try:
    main()
finally:
    sys.stdout.flush()
    os._exit(0)
