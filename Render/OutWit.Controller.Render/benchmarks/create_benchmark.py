import bpy
import math
import sys
import os

bpy.ops.wm.read_factory_settings(use_empty=True)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 16
scene.render.resolution_x = 128
scene.render.resolution_y = 128

# Floor
bpy.ops.mesh.primitive_plane_add(size=10, location=(0, 0, 0))
floor = bpy.context.active_object
fm = bpy.data.materials.new('FloorMat')
fm.use_nodes = True
fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.3, 0.35, 1)
fm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.8
floor.data.materials.append(fm)

# Glass sphere
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.8, location=(0, 0, 0.8), segments=32, ring_count=16)
sphere = bpy.context.active_object
sm = bpy.data.materials.new('GlassMat')
sm.use_nodes = True
sm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.05
sm.node_tree.nodes['Principled BSDF'].inputs['IOR'].default_value = 1.45
sphere.data.materials.append(sm)

# Metal cube
bpy.ops.mesh.primitive_cube_add(size=1, location=(1.5, -1, 0.5))
cube = bpy.context.active_object
cm = bpy.data.materials.new('MetalMat')
cm.use_nodes = True
cm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.9, 0.6, 0.2, 1)
cm.node_tree.nodes['Principled BSDF'].inputs['Metallic'].default_value = 1.0
cm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.2
cube.data.materials.append(cm)

# Red torus
bpy.ops.mesh.primitive_torus_add(location=(-1.5, 1, 0.4), major_radius=0.6, minor_radius=0.2)
torus = bpy.context.active_object
tm = bpy.data.materials.new('DiffuseMat')
tm.use_nodes = True
tm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.8, 0.15, 0.15, 1)
torus.data.materials.append(tm)

# Area light
bpy.ops.object.light_add(type='AREA', location=(2, -2, 4))
light = bpy.context.active_object
light.data.energy = 200
light.data.size = 3
light.rotation_euler = (math.radians(45), 0, math.radians(45))

# Camera
bpy.ops.object.camera_add(location=(4, -4, 3))
cam = bpy.context.active_object
cam.rotation_euler = (math.radians(60), 0, math.radians(45))
cam.data.lens = 35
scene.camera = cam

scene.frame_start = 1
scene.frame_end = 1

# Save next to this script
script_dir = os.path.dirname(os.path.abspath(__file__))
output = os.path.join(script_dir, "benchmark_scene.blend")
bpy.ops.wm.save_as_mainfile(filepath=output)
print(f"Benchmark scene saved to: {output}")
