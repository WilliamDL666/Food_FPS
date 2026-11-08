# Shared "Fall Guys" look for the Blender prop scripts: chunky inflated shapes, saturated candy colours, soft glossy plastic.
# Call apply(root) right before exporting.
import bpy, colorsys

def _saturate(rgba, s_mul=1.22, v_mul=1.04):
    r, g, b, a = rgba
    # values are linear; work in an approximate sRGB space so the boost looks even
    sr, sg, sb = (max(0, c) ** (1 / 2.2) for c in (r, g, b))
    h, s, v = colorsys.rgb_to_hsv(sr, sg, sb)
    s = min(1.0, s * s_mul) if s > .08 else s          # keep whites white
    v = min(1.0, v * v_mul)
    sr, sg, sb = colorsys.hsv_to_rgb(h, s, v)
    return (sr ** 2.2, sg ** 2.2, sb ** 2.2, a)

def apply(root, bevel_mul=1.8, gloss=0.3, s_mul=1.22):
    done = set()
    for o in bpy.data.objects:
        if o.type != 'MESH': continue
        for md in o.modifiers:
            if md.type == 'BEVEL':
                md.width *= bevel_mul; md.segments = max(md.segments, 5); md.limit_method = 'NONE'
                try: md.harden_normals = False
                except Exception: pass
        # every hard box gets a subdivision pass so nothing has a sharp corner
        if len(o.data.vertices) == 8 and not any(md.type == 'SUBSURF' for md in o.modifiers):
            if not any(md.type == 'BEVEL' for md in o.modifiers):
                md = o.modifiers.new('fg_bevel', 'BEVEL'); md.width = min(o.dimensions) * .25; md.segments = 4
            md = o.modifiers.new('fg_sub', 'SUBSURF'); md.levels = md.render_levels = 1
        for p in o.data.polygons: p.use_smooth = True
        for slot in o.material_slots:
            m = slot.material
            if m is None or m.name in done or not m.use_nodes: continue
            done.add(m.name)
            b = m.node_tree.nodes.get('Principled BSDF')
            if b is None: continue
            b.inputs["Base Color"].default_value = _saturate(tuple(b.inputs["Base Color"].default_value), s_mul)
            if b.inputs['Roughness'].default_value > .2:
                b.inputs['Roughness'].default_value = min(b.inputs['Roughness'].default_value, gloss)
            for name, val in (('Coat Weight', .35), ('Coat Roughness', .25), ('Specular IOR Level', .55)):
                if name in b.inputs: b.inputs[name].default_value = val
