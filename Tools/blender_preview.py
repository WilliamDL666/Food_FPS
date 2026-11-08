# Render a contact sheet of exported .glb files:  blender -b -P blender_preview.py -- <dir> <out.png> [spacing]
import bpy, sys, os, math, glob
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, out = args[0], args[1]; spacing = float(args[2]) if len(args) > 2 else 0.9
bpy.ops.wm.read_factory_settings(use_empty=True)
files = sorted(glob.glob(os.path.join(src, '*.glb')))
cols = 4
for i, f in enumerate(files):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=f)
    new = [o for o in bpy.data.objects if o not in before]
    roots = [o for o in new if o.parent is None]
    for r in roots:
        r.location = Vector(((i % cols) * spacing, 0, -(i // cols) * spacing * .55))
        r.rotation_mode = 'XYZ'; r.rotation_euler = (0, 0, math.radians(90))   # barrel (-Y) → +X, seen from the side
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items.keys() else 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x, scene.render.resolution_y = 1600, 900
scene.render.film_transparent = False
world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.85, 1.0, 1); world.node_tree.nodes['Background'].inputs[1].default_value = 1.0
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); scene.collection.objects.link(sun); sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
rows = (len(files) + cols - 1) // cols
cx, cz = (cols - 1) * spacing / 2, -(rows - 1) * spacing * .55 / 2
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = cols * spacing * 1.05
cam.location = (cx, -6, cz + .4); cam.rotation_euler = (math.radians(86), 0, 0)
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print('PREVIEW_DONE', out)
