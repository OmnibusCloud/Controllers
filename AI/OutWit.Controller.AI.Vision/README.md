# OutWit.Controller.AI.Vision

Annotation extraction and dataset assembly for synthetic computer-vision
training data rendered by the Render controller. Object-index masks from
multilayer EXR frames become COCO annotations (bounding boxes, segmentation,
classes); datasets are assembled into sharded archives with per-class
coverage statistics.

Pure .NET: anything that needs Blender lives in the Render controller —
Vision never bundles or invokes it.

**Status: v0.1.0-dev — project skeleton. No activities are implemented yet.**
