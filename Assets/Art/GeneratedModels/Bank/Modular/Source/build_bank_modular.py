# Модульный кит банка SUNSET TRUST для GrabzVR (Blender 5.x, запуск без интерфейса):
#   blender -b -P build_bank_modular.py
# Ровные стены с плинтусом и карнизом, стены с проёмами, классические колонны, пилястры, кассовая стойка,
# рама и круглая дверь хранилища, ячейки, кессонный потолок, ступени, антаблемент, столбик ограждения, излучатель лазера.
# Центр по X и глубине, низ на Z = 0. Материалы по имени (M_Wall, M_Trim, M_Marble, M_Gold, M_Steel, M_DarkSteel,
# M_Wood, M_Glass, M_Velvet) — в Unity их заменяет BankQuaterniusBuilder на URP-материалы.
# Экспорт FBX рядом: ../<Имя>.fbx (оси для Unity: -Z вперёд, Y вверх, трансформ запечён).
import bpy, bmesh, math, os

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
T = 0.3  # толщина стены

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    return m

def box(name, x0, x1, y0, y1, z0, z1, m):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 + (v.co.x + 0.5) * (x1 - x0)
        v.co.y = y0 + (v.co.y + 0.5) * (y1 - y0)
        v.co.z = z0 + (v.co.z + 0.5) * (z1 - z0)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); ob.data.materials.append(mat(m))
    bpy.context.collection.objects.link(ob)
    return ob

def cyl(name, r, z0, z1, m, seg=24, x=0.0, y=0.0, axis="Z", r2=None):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=z1 - z0)
    for v in bm.verts:
        v.co.z += (z0 + z1) / 2
    if axis == "Y":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=__import__("mathutils").Matrix.Rotation(math.pi / 2, 3, "X"))
    for v in bm.verts:
        v.co.x += x; v.co.y += y
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = seg > 8
    ob = bpy.data.objects.new(name, me); ob.data.materials.append(mat(m))
    bpy.context.collection.objects.link(ob)
    return ob

def bevel(ob, w=0.015):
    mod = ob.modifiers.new("bevel", "BEVEL"); mod.width = w; mod.segments = 1; mod.limit_method = "ANGLE"
    return ob

def finish(name, parts, bevel_w=0.012, keep_separate=()):
    """Склеить детали в один объект (кроме keep_separate — например стекло), применить фаски, экспортировать."""
    for o in parts:
        if o.name not in keep_separate and bevel_w > 0: bevel(o, bevel_w)
    bpy.ops.object.select_all(action="DESELECT")
    separate = [o for o in parts if o.name in keep_separate]
    solid = [o for o in parts if o.name not in keep_separate]
    for o in solid: o.select_set(True)
    bpy.context.view_layer.objects.active = solid[0]
    bpy.ops.object.convert(target="MESH")
    bpy.ops.object.join()
    main = bpy.context.view_layer.objects.active; main.name = name
    for o in separate: o.parent = main
    bpy.ops.object.select_all(action="DESELECT")
    main.select_set(True)
    for o in main.children: o.select_set(True)
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                             mesh_smooth_type="FACE", add_leaf_bones=False)
    print("exported", path)
    reset()

# ---------------------------------------------------------------- стены
def trims(parts, x0, x1, h, base=True, crown=True, band=None):
    if base: parts.append(box("base", x0, x1, -T / 2 - 0.04, T / 2 + 0.04, 0, 0.2, "M_Trim"))
    if crown:
        parts.append(box("crown1", x0, x1, -T / 2 - 0.05, T / 2 + 0.05, h - 0.28, h - 0.16, "M_Trim"))
        parts.append(box("crown2", x0, x1, -T / 2 - 0.09, T / 2 + 0.09, h - 0.16, h, "M_Trim"))
    if band: parts.append(box("band", x0, x1, -T / 2 - 0.03, T / 2 + 0.03, band, band + 0.14, "M_Trim"))

def wall(name, w, h, opening=None, band=None):
    """opening = (ширина, низ, верх) — проём по центру."""
    p = []
    if opening is None:
        p.append(box("wall", -w / 2, w / 2, -T / 2, T / 2, 0, h, "M_Wall"))
        trims(p, -w / 2, w / 2, h, band=band)
    else:
        ow, ob, ot = opening
        p.append(box("L", -w / 2, -ow / 2, -T / 2, T / 2, 0, h, "M_Wall"))
        p.append(box("R", ow / 2, w / 2, -T / 2, T / 2, 0, h, "M_Wall"))
        p.append(box("top", -ow / 2, ow / 2, -T / 2, T / 2, ot, h, "M_Wall"))
        if ob > 0: p.append(box("sill", -ow / 2, ow / 2, -T / 2, T / 2, 0, ob, "M_Wall"))
        # наличник
        p.append(box("fL", -ow / 2 - 0.12, -ow / 2, -T / 2 - 0.05, T / 2 + 0.05, ob, ot + 0.12, "M_Trim"))
        p.append(box("fR", ow / 2, ow / 2 + 0.12, -T / 2 - 0.05, T / 2 + 0.05, ob, ot + 0.12, "M_Trim"))
        p.append(box("fT", -ow / 2 - 0.12, ow / 2 + 0.12, -T / 2 - 0.05, T / 2 + 0.05, ot, ot + 0.12, "M_Trim"))
        if ob > 0: p.append(box("fB", -ow / 2 - 0.18, ow / 2 + 0.18, -T / 2 - 0.08, T / 2 + 0.08, ob - 0.08, ob, "M_Trim"))
        p.append(box("baseL", -w / 2, -ow / 2 - 0.12, -T / 2 - 0.04, T / 2 + 0.04, 0, 0.2, "M_Trim"))
        p.append(box("baseR", ow / 2 + 0.12, w / 2, -T / 2 - 0.04, T / 2 + 0.04, 0, 0.2, "M_Trim"))
        if ob > 0: p.append(box("baseC", -ow / 2, ow / 2, -T / 2 - 0.04, T / 2 + 0.04, 0, 0.2, "M_Trim"))
        trims(p, -w / 2, w / 2, h, base=False, band=band)
        if band and band < ot:  # полоса не должна пересекать проём
            p[-1] = box("bandL", -w / 2, -ow / 2 - 0.12, -T / 2 - 0.03, T / 2 + 0.03, band, band + 0.14, "M_Trim")
            p.append(box("bandR", ow / 2 + 0.12, w / 2, -T / 2 - 0.03, T / 2 + 0.03, band, band + 0.14, "M_Trim"))
    finish(name, p)

# ---------------------------------------------------------------- колонны
def column(name, h, r=0.38):
    p = [box("plinth", -r - 0.14, r + 0.14, -r - 0.14, r + 0.14, 0, 0.3, "M_Trim"),
         cyl("torus", r + 0.08, 0.3, 0.45, "M_Trim", 24),
         cyl("shaft", r, 0.45, h - 0.55, "M_Marble", 24, r2=r * 0.9),
         cyl("neck", r * 0.9 + 0.06, h - 0.55, h - 0.45, "M_Gold", 24),
         cyl("echinus", r * 0.9 + 0.06, h - 0.45, h - 0.3, "M_Trim", 24, r2=r + 0.16),
         box("abacus", -r - 0.2, r + 0.2, -r - 0.2, r + 0.2, h - 0.3, h, "M_Trim")]
    # каннелюры — тонкие рёбра
    for i in range(12):
        a = i / 12 * math.tau
        p.append(cyl(f"fl{i}", 0.035, 0.5, h - 0.6, "M_Trim", 6, x=math.cos(a) * r * 0.97, y=math.sin(a) * r * 0.97))
    finish(name, p, bevel_w=0.0)

def pilaster(name, h):
    p = [box("plinth", -0.42, 0.42, 0, 0.34, 0, 0.3, "M_Trim"),
         box("shaft", -0.3, 0.3, 0, 0.22, 0.3, h - 0.4, "M_Marble"),
         box("gold", -0.32, 0.32, 0, 0.24, h - 0.55, h - 0.45, "M_Gold"),
         box("cap", -0.44, 0.44, 0, 0.34, h - 0.4, h, "M_Trim")]
    for i in range(3): p.append(box(f"fl{i}", -0.18 + i * 0.18 - 0.03, -0.18 + i * 0.18 + 0.03, 0.2, 0.25, 0.5, h - 0.65, "M_Trim"))
    finish(name, p)

# ---------------------------------------------------------------- кассовая стойка (3 м): тумба, мрамор, стекло со щелью
def teller():
    p = [box("cab", -1.5, 1.5, -0.35, 0.35, 0, 1.05, "M_Wood"),
         box("kick", -1.5, 1.5, -0.3, 0.3, 0, 0.12, "M_DarkSteel"),
         box("top", -1.55, 1.55, -0.45, 0.45, 1.05, 1.12, "M_Marble"),
         box("goldline", -1.5, 1.5, -0.36, -0.35, 0.85, 0.9, "M_Gold"),
         box("postL", -1.52, -1.44, -0.05, 0.05, 1.12, 2.45, "M_Gold"),
         box("postR", 1.44, 1.52, -0.05, 0.05, 1.12, 2.45, "M_Gold"),
         box("rail", -1.52, 1.52, -0.06, 0.06, 2.4, 2.48, "M_Gold"),
         box("tray", -0.3, 0.3, -0.45, 0.2, 1.05, 1.1, "M_Steel"),
         box("panelL", -1.3, -0.5, -0.36, -0.355, 0.3, 0.75, "M_Trim"),
         box("panelR", 0.5, 1.3, -0.36, -0.355, 0.3, 0.75, "M_Trim")]
    glass = box("Glass", -1.44, 1.44, -0.012, 0.012, 1.3, 2.4, "M_Glass")
    p.append(glass)
    finish("Bank_TellerCounter", p, keep_separate=("Glass",))

# ---------------------------------------------------------------- хранилище
def vault_frame():
    w, h = 6.0, 4.0
    wallob = box("wall", -w / 2, w / 2, -0.4, 0.4, 0, h, "M_DarkSteel")
    hole = cyl("hole", 1.35, -1, 1, "M_Steel", 40, axis="Y")
    hole.location.z = 1.65
    mod = wallob.modifiers.new("bool", "BOOLEAN"); mod.object = hole; mod.operation = "DIFFERENCE"; mod.solver = "EXACT"
    bpy.context.view_layer.objects.active = wallob; bpy.ops.object.modifier_apply(modifier="bool")
    bpy.data.objects.remove(hole)
    ring = cyl("ring", 1.55, -0.5, 0.5, "M_Steel", 40, axis="Y"); ring.location.z = 1.65
    inner = cyl("inner", 1.35, -1, 1, "M_Steel", 40, axis="Y"); inner.location.z = 1.65
    mod = ring.modifiers.new("bool", "BOOLEAN"); mod.object = inner; mod.operation = "DIFFERENCE"; mod.solver = "EXACT"
    bpy.context.view_layer.objects.active = ring; bpy.ops.object.modifier_apply(modifier="bool")
    bpy.data.objects.remove(inner)
    p = [wallob, ring]
    for i in range(16):
        a = i / 16 * math.tau
        b = cyl(f"bolt{i}", 0.07, -0.56, 0.56, "M_Gold", 8, axis="Y"); b.location = (math.cos(a) * 1.45, 0, 1.65 + math.sin(a) * 1.45)
        p.append(b)
    p.append(box("stripe", -w / 2, w / 2, -0.42, 0.42, 3.6, 3.75, "M_Gold"))
    finish("Bank_VaultFrame", p, bevel_w=0.0)

def vault_door():
    """Круглая дверь: петля слева (X = -1.3), дверь в плоскости XZ, толщина по Y."""
    p = []
    d = cyl("disc", 1.3, -0.25, 0.25, "M_Steel", 40, axis="Y"); d.location = (1.3, 0, 0); p.append(d)
    f = cyl("face", 1.1, -0.27, 0.27, "M_DarkSteel", 40, axis="Y"); f.location = (1.3, 0, 0); p.append(f)
    hub = cyl("hub", 0.22, -0.45, 0.45, "M_Gold", 16, axis="Y"); hub.location = (1.3, 0, 0); p.append(hub)
    for i in range(6):
        a = i / 6 * math.tau
        s = box(f"spoke{i}", -0.03, 0.03, 0.3, 0.42, 0, 0.62, "M_Gold"); s.rotation_euler = (0, a, 0); s.location = (1.3, 0, 0); p.append(s)
        k = cyl(f"knob{i}", 0.06, 0.28, 0.48, "M_Gold", 8, axis="Y"); k.location = (1.3 + math.cos(a) * 0.62, 0, math.sin(a) * 0.62); p.append(k)
    for i in range(12):
        a = i / 12 * math.tau
        b = cyl(f"rivet{i}", 0.05, 0.26, 0.31, "M_Gold", 8, axis="Y"); b.location = (1.3 + math.cos(a) * 1.18, 0, math.sin(a) * 1.18); p.append(b)
    for z in (-0.7, 0.7): p.append(box(f"hinge{z}", -0.15, 0.15, -0.3, 0.3, z - 0.2, z + 0.2, "M_DarkSteel"))
    finish("Bank_VaultDoor", p, bevel_w=0.0)

def deposit_boxes():
    p = [box("body", -2, 2, -0.3, 0.3, 0, 2.8, "M_Steel")]
    for r in range(7):
        for c in range(10):
            x0 = -1.9 + c * 0.38; z0 = 0.15 + r * 0.38
            p.append(box(f"d{r}_{c}", x0, x0 + 0.34, 0.3, 0.33, z0, z0 + 0.34, "M_Gold" if (r * 3 + c) % 11 == 0 else "M_DarkSteel"))
            p.append(box(f"k{r}_{c}", x0 + 0.24, x0 + 0.29, 0.33, 0.36, z0 + 0.14, z0 + 0.2, "M_Gold"))
    finish("Bank_DepositBoxes", p, bevel_w=0.0)

# ---------------------------------------------------------------- потолок, ступени, антаблемент, мелочи
def coffer():
    p = [box("slab", -2, 2, -2, 2, 0, 0.25, "M_Wall")]
    for i in range(3):
        a = -2 + i * 2
        p.append(box(f"bx{i}", -2, 2, a - 0.1 if i else -2, a + 0.1 if i else -1.9, -0.3, 0, "M_Trim"))
        p.append(box(f"by{i}", a - 0.1 if i else -2, a + 0.1 if i else -1.9, -2, 2, -0.3, 0, "M_Trim"))
    p.append(box("bx3", -2, 2, 1.9, 2, -0.3, 0, "M_Trim")); p.append(box("by3", 1.9, 2, -2, 2, -0.3, 0, "M_Trim"))
    for (x, y) in ((-1, -1), (1, -1), (-1, 1), (1, 1)): p.append(box(f"g{x}{y}", x - 0.35, x + 0.35, y - 0.35, y + 0.35, -0.05, 0, "M_Gold"))
    finish("Bank_CeilingCoffer", p)

def steps():
    p = []
    for i in range(4): p.append(box(f"s{i}", -6, 6, -0.4 * (4 - i), 0.0, 0, 0.18 * (i + 1), "M_Marble"))
    finish("Bank_Steps", p)

def entablature():
    p = [box("arch", -8, 8, -0.5, 0.5, 0, 0.6, "M_Trim"),
         box("frieze", -8, 8, -0.45, 0.45, 0.6, 1.4, "M_Wall"),
         box("gold", -8, 8, -0.47, 0.47, 0.95, 1.05, "M_Gold"),
         box("cornice", -8.3, 8.3, -0.75, 0.75, 1.4, 1.7, "M_Trim")]
    for i in range(24): p.append(box(f"dent{i}", -7.8 + i * 0.68, -7.8 + i * 0.68 + 0.3, -0.62, 0.62, 1.25, 1.4, "M_Trim"))
    finish("Bank_Entablature", p)

def stanchion():
    p = [cyl("base", 0.2, 0, 0.05, "M_Gold", 16), cyl("post", 0.04, 0.05, 0.95, "M_Gold", 10), cyl("top", 0.07, 0.95, 1.02, "M_Gold", 12)]
    finish("Bank_Stanchion", p, bevel_w=0.0)

def laser_post():
    p = [box("base", -0.15, 0.15, -0.15, 0.15, 0, 0.06, "M_DarkSteel"), box("post", -0.06, 0.06, -0.06, 0.06, 0.06, 1.8, "M_DarkSteel")]
    for z in (0.35, 0.75, 1.15, 1.55): p.append(box(f"e{z}", -0.08, 0.08, -0.08, 0.08, z, z + 0.08, "M_Steel"))
    finish("Bank_LaserPost", p, bevel_w=0.0)

def gold_stack():
    p = []
    for layer in range(3):
        for i in range(4 - layer):
            x = -0.45 + i * 0.3 + layer * 0.15
            p.append(box(f"b{layer}{i}", x, x + 0.26, -0.12, 0.12, layer * 0.09, layer * 0.09 + 0.09, "M_Gold"))
    finish("Bank_GoldStack", p, bevel_w=0.008)

if __name__ == "__main__":
    reset()
    wall("Bank_Wall_4x3_5", 4, 3.5)
    wall("Bank_Wall_4x7", 4, 7, band=3.4)
    wall("Bank_WallDoor_4x3_5", 4, 3.5, opening=(2.0, 0, 2.8))
    wall("Bank_WallDoor_4x7", 4, 7, opening=(2.8, 0, 3.6), band=4.2)
    wall("Bank_WallWindow_4x7", 4, 7, opening=(2.6, 1.0, 5.4))
    column("Bank_Column_7", 7)
    column("Bank_Column_3_5", 3.5, r=0.3)
    pilaster("Bank_Pilaster_7", 7)
    teller(); vault_frame(); vault_door(); deposit_boxes(); coffer(); steps(); entablature(); stanchion(); laser_post(); gold_stack()
    print("DONE")
