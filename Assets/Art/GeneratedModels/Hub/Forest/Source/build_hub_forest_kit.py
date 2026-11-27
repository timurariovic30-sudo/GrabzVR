# Модульный кит «Лесной двор-убежище» для MainHub GrabzVR (Blender 5.x, без интерфейса):
#   blender -b --factory-startup -P build_hub_forest_kit.py
# Ровные стилизованные детали: настил 2×2, столб, поваленное бревно, секция забора, дощатая крыша 3×3, лестница,
# костёр (шалаш из поленьев + кольцо камней + угли), ящик, гирлянда лампочек.
# Центр по X и глубине, низ на Z = 0; развёртка в метрах (текстуры досок/кирпича тайлятся, а не растягиваются).
# Материалы по имени (в Unity HubForestBuilder заменяет их): M_Wood, M_WoodDark, M_Bark, M_Stone, M_Ember, M_Bulb, M_Wire.
# Экспорт: ../<Имя>.fbx
import bpy, bmesh, math, os, random
from mathutils import Matrix, Vector

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
random.seed(7)

def reset(): bpy.ops.wm.read_factory_settings(use_empty=True)
def mat(n): return bpy.data.materials.get(n) or bpy.data.materials.new(n)

def box(name, x0, x1, y0, y1, z0, z1, m, bevel=0.012):
    me = bpy.data.meshes.new(name); bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0); v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0); v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); ob.data.materials.append(mat(m)); bpy.context.collection.objects.link(ob)
    if bevel > 0: md = ob.modifiers.new("b", "BEVEL"); md.width = bevel; md.segments = 1
    return ob

def cyl(name, r, length, m, seg=12, axis="X", r2=None, at=(0, 0, 0)):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=length)
    rot = {"X": Matrix.Rotation(math.pi / 2, 4, "Y"), "Y": Matrix.Rotation(math.pi / 2, 4, "X"), "Z": Matrix.Identity(4)}[axis]
    bmesh.ops.transform(bm, matrix=Matrix.Translation(at) @ rot, verts=bm.verts)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    ob = bpy.data.objects.new(name, me); ob.data.materials.append(mat(m)); bpy.context.collection.objects.link(ob); return ob

def sphere(name, r, m, at, scale=(1, 1, 1), seg=(10, 6)):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg[0], v_segments=seg[1], radius=r)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(at) @ Matrix.Diagonal((*scale, 1)), verts=bm.verts)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); ob.data.materials.append(mat(m)); bpy.context.collection.objects.link(ob); return ob

def finish(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.convert(target="MESH"); bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active; ob.name = name
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0); bpy.ops.object.mode_set(mode="OBJECT")
    ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
                             axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE")
    print("exported", name, len(ob.data.polygons))
    reset()

# ---------------------------------------------------------------- детали
def deck():
    p = [box("beamA", -1, 1, -0.75, -0.6, 0, 0.14, "M_WoodDark"), box("beamB", -1, 1, 0.6, 0.75, 0, 0.14, "M_WoodDark")]
    for i in range(8):
        x0 = -1 + i * 0.25 + 0.005
        p.append(box(f"plank{i}", x0, x0 + 0.24, -1, 1, 0.14, 0.2, "M_Wood", 0.008))
    finish("Hub_Deck_2x2", p)

def post(): finish("Hub_Post_3", [box("post", -0.1, 0.1, -0.1, 0.1, 0, 3, "M_WoodDark", 0.015)])

def log():
    p = [cyl("trunk", 0.22, 3.0, "M_Bark", 14, "X", r2=0.19)]
    p.append(cyl("capA", 0.2, 0.02, "M_Wood", 14, "X", at=(1.5, 0, 0)))
    p.append(cyl("capB", 0.175, 0.02, "M_Wood", 14, "X", at=(-1.5, 0, 0)))
    for i, (x, a) in enumerate(((-0.6, 40), (0.4, 150), (0.95, 260))):
        st = cyl(f"stub{i}", 0.05, 0.25, "M_Bark", 8, "Z", r2=0.03)
        st.location = (x, math.cos(math.radians(a)) * 0.2, math.sin(math.radians(a)) * 0.2)
        st.rotation_euler = (math.radians(a + 90), 0, 0); p.append(st)
    for o in p: o.location.z += 0.21
    finish("Hub_Log_3", p)

def fence():
    p = [box("postL", -1.0, -0.88, -0.06, 0.06, 0, 1.15, "M_WoodDark"), box("postR", 0.88, 1.0, -0.06, 0.06, 0, 1.15, "M_WoodDark")]
    for z in (0.35, 0.8): p.append(box(f"rail{z}", -1.0, 1.0, -0.035, 0.035, z, z + 0.12, "M_Wood"))
    for i in range(7):
        x = -0.75 + i * 0.25
        p.append(box(f"picket{i}", x - 0.05, x + 0.05, 0.04, 0.07, 0.1, 1.0 + (0.06 if i % 2 else 0), "M_Wood", 0.006))
    finish("Hub_Fence_2", p)

def roof():
    p = []
    for i in range(12):
        x0 = -1.5 + i * 0.25 + 0.004
        p.append(box(f"board{i}", x0, x0 + 0.245, -1.6, 1.6, 0.0, 0.05, "M_Wood", 0.006))
    for y in (-1.2, 0, 1.2): p.append(box(f"rafter{y}", -1.6, 1.6, y - 0.06, y + 0.06, -0.14, 0.0, "M_WoodDark"))
    finish("Hub_Roof_3x3", p)

def ladder():
    p = [box("railL", -0.3, -0.24, -0.04, 0.04, 0, 3.0, "M_WoodDark"), box("railR", 0.24, 0.3, -0.04, 0.04, 0, 3.0, "M_WoodDark")]
    for i in range(9): p.append(cyl(f"rung{i}", 0.03, 0.5, "M_Wood", 8, "X", at=(0, 0, 0.3 + i * 0.32)))
    finish("Hub_Ladder_3", p)

def campfire():
    p = []
    for i in range(12):
        a = i / 12 * math.tau; r = 0.62 + random.uniform(-0.03, 0.03)
        p.append(sphere(f"stone{i}", 0.13, "M_Stone", (math.cos(a) * r, math.sin(a) * r, 0.07), (1.2, 0.95, 0.65), (8, 5)))
    for i in range(6):
        a = i / 6 * math.tau
        lg = cyl(f"stick{i}", 0.06, 0.85, "M_Bark", 8, "Z", r2=0.045)
        lg.location = (math.cos(a) * 0.17, math.sin(a) * 0.17, 0.3)
        lg.rotation_euler = (math.sin(a) * math.radians(28), -math.cos(a) * math.radians(28), 0); p.append(lg)
    p.append(cyl("embers", 0.42, 0.06, "M_Ember", 14, "Z", at=(0, 0, 0.03)))
    finish("Hub_Campfire", p)

def crate():
    p = [box("body", -0.4, 0.4, -0.4, 0.4, 0, 0.8, "M_Wood", 0.01)]
    for z in (0.02, 0.7): p.append(box(f"band{z}", -0.42, 0.42, -0.42, 0.42, z, z + 0.08, "M_WoodDark", 0.008))
    for x in (-0.42, 0.34): p.append(box(f"edge{x}", x, x + 0.08, -0.42, 0.42, 0, 0.8, "M_WoodDark", 0.008))
    finish("Hub_Crate", p)

def string_lights():
    p = []; L = 4.0; n = 10
    pts = [Vector((-L / 2 + L * i / 24, 0, -0.35 * (1 - ((i / 24) * 2 - 1) ** 2))) for i in range(25)]
    for a, b in zip(pts, pts[1:]):
        d = b - a; c = cyl("wire", 0.006, d.length, "M_Wire", 5, "Z", at=(0, 0, 0))
        c.matrix_world = Matrix.Translation((a + b) / 2) @ Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4(); p.append(c)
    for i in range(n):
        t = (i + 0.5) / n; x = -L / 2 + L * t; z = -0.35 * (1 - (t * 2 - 1) ** 2)
        p.append(sphere(f"bulb{i}", 0.045, "M_Bulb", (x, 0, z - 0.07), (1, 1, 1.25), (8, 6)))
    finish("Hub_StringLights_4", p)

if __name__ == "__main__":
    reset()
    deck(); post(); log(); fence(); roof(); ladder(); campfire(); crate(); string_lights()
    print("DONE")
