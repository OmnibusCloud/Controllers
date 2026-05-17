import math
import os

import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 16
scene.render.resolution_x = 128
scene.render.resolution_y = 128
scene.render.film_transparent = False
scene.frame_start = 1
scene.frame_end = 16
scene.frame_set(1)

# Floor
bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
floor = bpy.context.active_object
floor_material = bpy.data.materials.new('FloorMat')
floor_material.use_nodes = True
floor_bsdf = floor_material.node_tree.nodes['Principled BSDF']
floor_bsdf.inputs['Base Color'].default_value = (0.28, 0.3, 0.34, 1.0)
floor_bsdf.inputs['Roughness'].default_value = 0.78
floor.data.materials.append(floor_material)

# Glass sphere
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.75, location=(0.0, 0.0, 0.8), segments=32, ring_count=16)
sphere = bpy.context.active_object
sphere_material = bpy.data.materials.new('GlassMat')
sphere_material.use_nodes = True
sphere_bsdf = sphere_material.node_tree.nodes['Principled BSDF']
sphere_bsdf.inputs['Transmission Weight'].default_value = 1.0
sphere_bsdf.inputs['Roughness'].default_value = 0.03
sphere_bsdf.inputs['IOR'].default_value = 1.45
sphere.data.materials.append(sphere_material)

# Moving cube
bpy.ops.mesh.primitive_cube_add(size=1.0, location=(1.6, -1.2, 0.55))
cube = bpy.context.active_object
cube_material = bpy.data.materials.new('MetalMat')
cube_material.use_nodes = True
cube_bsdf = cube_material.node_tree.nodes['Principled BSDF']
cube_bsdf.inputs['Base Color'].default_value = (0.92, 0.66, 0.18, 1.0)
cube_bsdf.inputs['Metallic'].default_value = 1.0
cube_bsdf.inputs['Roughness'].default_value = 0.16
cube.data.materials.append(cube_material)

# Animated torus
bpy.ops.mesh.primitive_torus_add(location=(-1.8, 1.1, 0.5), major_radius=0.65, minor_radius=0.18)
torus = bpy.context.active_object
torus_material = bpy.data.materials.new('TorusMat')
torus_material.use_nodes = True
torus_bsdf = torus_material.node_tree.nodes['Principled BSDF']
torus_bsdf.inputs['Base Color'].default_value = (0.82, 0.18, 0.18, 1.0)
torus_bsdf.inputs['Roughness'].default_value = 0.35
torus.data.materials.append(torus_material)

# Area light
bpy.ops.object.light_add(type='AREA', location=(2.5, -2.5, 4.5))
light = bpy.context.active_object
light.data.energy = 240
light.data.size = 3.2
light.rotation_euler = (math.radians(50), 0.0, math.radians(42))

# Camera
bpy.ops.object.camera_add(location=(4.2, -4.5, 3.2))
camera = bpy.context.active_object
camera.rotation_euler = (math.radians(63), 0.0, math.radians(43))
camera.data.lens = 34
scene.camera = camera

# Animate camera drift
camera.keyframe_insert(data_path='location', frame=1)
camera.keyframe_insert(data_path='rotation_euler', frame=1)
camera.location = (3.6, -3.7, 2.9)
camera.rotation_euler = (math.radians(58), 0.0, math.radians(34))
camera.keyframe_insert(data_path='location', frame=16)
camera.keyframe_insert(data_path='rotation_euler', frame=16)

# Animate cube sweep
cube.keyframe_insert(data_path='location', frame=1)
cube.keyframe_insert(data_path='rotation_euler', frame=1)
cube.location = (0.4, 1.6, 0.75)
cube.rotation_euler = (math.radians(20), math.radians(35), math.radians(140))
cube.keyframe_insert(data_path='location', frame=16)
cube.keyframe_insert(data_path='rotation_euler', frame=16)

# Animate torus spin and bob
for frame, z_offset, rotation_z in [(1, 0.45, 0.0), (8, 0.9, math.radians(160)), (16, 0.55, math.radians(320))]:
    torus.location = (-1.8, 1.1, z_offset)
    torus.rotation_euler = (math.radians(90), 0.0, rotation_z)
    torus.keyframe_insert(data_path='location', frame=frame)
    torus.keyframe_insert(data_path='rotation_euler', frame=frame)

# Animate light intensity slightly
light.data.keyframe_insert(data_path='energy', frame=1)
light.data.energy = 320
light.data.keyframe_insert(data_path='energy', frame=16)

# Keep linear timing predictable
for action in bpy.data.actions:
    if not hasattr(action, 'fcurves'):
        continue

    for fcurve in action.fcurves:
        for keyframe in fcurve.keyframe_points:
            keyframe.interpolation = 'LINEAR'

script_dir = os.path.dirname(os.path.abspath(__file__))
benchmark_root = os.path.normpath(os.path.join(script_dir, '..', '..', '..', '..', '@Prerequisites', 'benchmark', 'render'))
os.makedirs(benchmark_root, exist_ok=True)

video_output = os.path.join(benchmark_root, 'benchmark_scene_video.blend')
bpy.ops.wm.save_as_mainfile(filepath=video_output)
print(f'Video benchmark scene saved to: {video_output}')
