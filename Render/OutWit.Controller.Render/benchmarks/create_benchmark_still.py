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
scene.frame_end = 1
scene.frame_set(1)

# Floor
bpy.ops.mesh.primitive_plane_add(size=10, location=(0, 0, 0))
floor = bpy.context.active_object
floor_material = bpy.data.materials.new('FloorMat')
floor_material.use_nodes = True
floor_bsdf = floor_material.node_tree.nodes['Principled BSDF']
floor_bsdf.inputs['Base Color'].default_value = (0.3, 0.3, 0.35, 1.0)
floor_bsdf.inputs['Roughness'].default_value = 0.8
floor.data.materials.append(floor_material)

# Glass sphere
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.8, location=(0, 0, 0.8), segments=32, ring_count=16)
sphere = bpy.context.active_object
sphere_material = bpy.data.materials.new('GlassMat')
sphere_material.use_nodes = True
sphere_bsdf = sphere_material.node_tree.nodes['Principled BSDF']
sphere_bsdf.inputs['Transmission Weight'].default_value = 1.0
sphere_bsdf.inputs['Roughness'].default_value = 0.05
sphere_bsdf.inputs['IOR'].default_value = 1.45
sphere.data.materials.append(sphere_material)

# Metal cube
bpy.ops.mesh.primitive_cube_add(size=1, location=(1.5, -1.0, 0.5))
cube = bpy.context.active_object
cube_material = bpy.data.materials.new('MetalMat')
cube_material.use_nodes = True
cube_bsdf = cube_material.node_tree.nodes['Principled BSDF']
cube_bsdf.inputs['Base Color'].default_value = (0.9, 0.6, 0.2, 1.0)
cube_bsdf.inputs['Metallic'].default_value = 1.0
cube_bsdf.inputs['Roughness'].default_value = 0.2
cube.data.materials.append(cube_material)

# Red torus
bpy.ops.mesh.primitive_torus_add(location=(-1.5, 1.0, 0.4), major_radius=0.6, minor_radius=0.2)
torus = bpy.context.active_object
torus_material = bpy.data.materials.new('DiffuseMat')
torus_material.use_nodes = True
torus_bsdf = torus_material.node_tree.nodes['Principled BSDF']
torus_bsdf.inputs['Base Color'].default_value = (0.8, 0.15, 0.15, 1.0)
torus.data.materials.append(torus_material)

# Area light
bpy.ops.object.light_add(type='AREA', location=(2.0, -2.0, 4.0))
light = bpy.context.active_object
light.data.energy = 200
light.data.size = 3
light.rotation_euler = (math.radians(45), 0.0, math.radians(45))

# Camera
bpy.ops.object.camera_add(location=(4.0, -4.0, 3.0))
camera = bpy.context.active_object
camera.rotation_euler = (math.radians(60), 0.0, math.radians(45))
camera.data.lens = 35
scene.camera = camera

script_dir = os.path.dirname(os.path.abspath(__file__))
benchmark_root = os.path.normpath(os.path.join(script_dir, '..', '..', '..', '..', '@Prerequisites', 'benchmark', 'render'))
os.makedirs(benchmark_root, exist_ok=True)

legacy_output = os.path.join(benchmark_root, 'benchmark_scene.blend')
still_output = os.path.join(benchmark_root, 'benchmark_scene_still.blend')

bpy.ops.wm.save_as_mainfile(filepath=legacy_output)
bpy.ops.wm.save_as_mainfile(filepath=still_output, copy=True)
print(f'Still benchmark scenes saved to: {legacy_output} and {still_output}')
