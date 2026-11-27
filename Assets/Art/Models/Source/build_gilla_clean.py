# Цельная стилизованная горилла игрока GrabzVR в духе GILLA / Gorilla Tag (Blender 5.x, без интерфейса):
#   blender -b --factory-startup -P build_gilla_clean.py
# 1) Форма из гладких объёмов: торс с широкой грудью (сужается книзу), серый овал живота, голова с заострённым хохолком,
#    серой надбровной дугой, маской лица, вытянутой мордочкой с полоской рта и нижней губой, круглые серые уши,
#    длинные руки в T-позе, кисти с 3 пальцами (большой, указательный, средний) по 2 фаланги.
# 2) Всё сливается в ОДИН водонепроницаемый меш: Voxel Remesh (объединение без щелей) → сглаживание стыков
#    (Laplacian, с сохранением объёма) → Decimate до бюджета Quest.
# 3) Материалы переносятся с исходных объёмов по ближайшей поверхности: Fur_Main (перекрашиваемая шерсть),
#    Skin_Grey (маска лица, брови, мордочка, уши, ладони, пальцы, живот), Mouth_Dark (полоска рта).
# 4) Веса: плавные, по расстоянию до костей (степень 6, отсечка дальних костей — соседние пальцы не тянут друг друга).
# 5) Меш разрезается по шее на Body и Head (общие вершины на разрезе — без щели): Head в Unity на слое LocalHead.
# Скелет и имена костей — как у прежней Gorilla_Final (весь код игры работает без правок).
# Экспорт: ../Gorilla_GillaClean.fbx (для Unity) и <repo>/Exports/Gorilla_GillaClean.glb (просмотр, с глазами и цветами).
import bpy, bmesh, math, os
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "Gorilla_GillaClean.fbx"))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
GLB = os.path.join(REPO, "Exports", "Gorilla_GillaClean.glb")
FUR_TEX = os.path.join(REPO, "Assets", "Art", "Models", "GillaClean", "T_GillaFur.png")
E = 1.45            # высота костей глаз (в Unity глаза модели = начало аватара)
SH_Z, SH_X = 1.20, 0.165
UPPER, LOWER = 0.27, 0.28
VOXEL = 0.0034
TARGET_TRIS = 5200
NECK_CUT = 1.288

bpy.ops.wm.read_factory_settings(use_empty=True)
def mat(name, rgb):
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); return m
M = [mat("Fur_Main", (0.35, 0.33, 0.32)), mat("Skin_Grey", (0.55, 0.55, 0.57)), mat("Mouth_Dark", (0.08, 0.06, 0.07))]
FUR, SKIN, MOUTH = 0, 1, 2

# ------------------------------------------------------------------ скелет
bone_specs = {}
def bone(name, h, t, parent=None): bone_specs[name] = (Vector(h), Vector(t), parent)
bone("Hips", (0, 0, 0.72), (0, 0, 0.88)); bone("Spine", (0, 0, 0.88), (0, 0, 1.02), "Hips")
bone("Chest", (0, 0, 1.02), (0, 0, 1.14), "Spine"); bone("UpperChest", (0, 0, 1.14), (0, 0, 1.25), "Chest")
bone("Neck", (0, 0, 1.25), (0, 0, 1.31), "UpperChest"); bone("Head", (0, 0, 1.31), (0, 0, 1.62), "Neck")
for s, P in ((1, "Left_"), (-1, "Right_")):          # левая сторона персонажа = +X (лицом к -Y)
    bone(P + "Eye", (s * 0.0625, -0.12, E), (s * 0.0625, -0.16, E), "Head")
    bone(P + "Shoulder", (s * 0.05, 0, SH_Z), (s * SH_X, 0, SH_Z), "UpperChest")
    x0 = SH_X; x1 = x0 + UPPER; x2 = x1 + LOWER; x3 = x2 + 0.085
    bone(P + "UpperArm", (s * x0, 0, SH_Z), (s * x1, 0, SH_Z), P + "Shoulder")
    bone(P + "LowerArm", (s * x1, 0, SH_Z), (s * x2, 0, SH_Z), P + "UpperArm")
    bone(P + "Hand", (s * x2, 0, SH_Z), (s * x3, 0, SH_Z), P + "LowerArm")
    for fname, y, l1, l2 in (("Index", -0.028, 0.052, 0.046), ("Middle", 0.03, 0.056, 0.048)):
        a = Vector((s * x3, y, SH_Z)); d = Vector((s, 0, -0.08)).normalized()
        b = a + d * l1; c = b + d * l2; tip = c + d * 0.018
        bone(P + fname + "Proximal", a, b, P + "Hand"); bone(P + fname + "Intermediate", b, c, P + fname + "Proximal")
        bone(P + fname + "Distal", c, tip, P + fname + "Intermediate")
        if fname == "Index": bone(P + "IndexDistalEnd", tip, tip + d * 0.012, P + "IndexDistal")
    a = Vector((s * (x2 + 0.035), -0.047, SH_Z - 0.006)); d = Vector((s * 0.5, -0.86, -0.12)).normalized()
    b = a + d * 0.044; c = b + d * 0.038; tip = c + d * 0.016
    bone(P + "ThumbProximal", a, b, P + "Hand"); bone(P + "ThumbIntermediate", b, c, P + "ThumbProximal")
    bone(P + "ThumbDistal", c, tip, P + "ThumbIntermediate")
DEFORM = [n for n in bone_specs if not n.endswith("Eye") and not n.endswith("DistalEnd")]

arm_data = bpy.data.armatures.new("Armature"); arm_obj = bpy.data.objects.new("Armature", arm_data)
bpy.context.collection.objects.link(arm_obj); bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")
for n, (h, t, p) in bone_specs.items():
    eb = arm_data.edit_bones.new(n); eb.head = h; eb.tail = t; eb.roll = 0
for n, (h, t, p) in bone_specs.items():
    if p: arm_data.edit_bones[n].parent = arm_data.edit_bones[p]
bpy.ops.object.mode_set(mode="OBJECT")
for b in arm_data.bones: b.use_deform = b.name in DEFORM

def seg_dist(p, name):
    h, t, _ = bone_specs[name]; ab = t - h
    k = max(0.0, min(1.0, (p - h).dot(ab) / max(1e-9, ab.length_squared)))
    return (p - (h + ab * k)).length

# ------------------------------------------------------------------ объёмы
parts = []   # (объект, материал)
def new_obj(name, bm, mi):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob); parts.append((ob, mi)); return ob
def ellipsoid(name, c, r, mi, rot=None, seg=(32, 18)):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg[0], v_segments=seg[1], radius=1.0)
    m = Matrix.Diagonal((*r, 1.0))
    if rot is not None: m = rot.to_4x4() @ m
    bmesh.ops.transform(bm, matrix=Matrix.Translation(c) @ m, verts=bm.verts); return new_obj(name, bm, mi)
def tube(name, a, b, r0, r1, mi):
    a, b = Vector(a), Vector(b); d = b - a
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=28, radius1=r0, radius2=r1, depth=d.length)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=bm.verts); return new_obj(name, bm, mi)
def along_bone(name, bname, r, mi, k=0.62):
    h, t, _ = bone_specs[bname]; d = t - h
    rot = Vector((1, 0, 0)).rotation_difference(d.normalized()).to_matrix()
    return ellipsoid(name, (h + t) / 2, (d.length * k, r, r * 0.92), mi, rot, (16, 10))

# торс: широкая грудная клетка, плавно сужается к низу
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=28, radius=1.0)
for v in bm.verts:
    z = v.co.z; f = 0.58 + 0.42 * ((z + 1) / 2) ** 0.8
    chest = 1.0 + 0.08 * math.exp(-((z - 0.45) / 0.35) ** 2)          # грудь
    top = 1.0 - 0.2 * max(0.0, z) ** 3
    v.co = Vector((v.co.x * 0.235 * f * top * chest, v.co.y * 0.168 * f * chest, 1.0 + z * 0.3))
new_obj("Torso", bm, FUR)
ellipsoid("Belly", (0, -0.1, 0.99), (0.135, 0.075, 0.175), SKIN)
# голова
ellipsoid("HeadBall", (0, 0.005, 1.432), (0.166, 0.16, 0.16), FUR)
ellipsoid("Tuft", (0, 0.02, 1.585), (0.034, 0.045, 0.034), FUR)                          # хохолок
ellipsoid("TuftTip", (0, 0.035, 1.618), (0.013, 0.022, 0.03), FUR, Matrix.Rotation(math.radians(-25), 3, "X"))
ellipsoid("FacePlate", (0, -0.088, 1.452), (0.13, 0.086, 0.1), SKIN)
for s in (1, -1):                                                                         # надбровная дуга (арка)
    ellipsoid("Brow", (s * 0.058, -0.142, 1.508), (0.072, 0.034, 0.024), SKIN, Matrix.Rotation(math.radians(s * 12), 3, "Y"))
ellipsoid("BrowMid", (0, -0.148, 1.502), (0.03, 0.03, 0.02), SKIN)
ellipsoid("Muzzle", (0, -0.128, 1.345), (0.1, 0.082, 0.068), SKIN)                      # вытянутая мордочка
ellipsoid("MouthLine", (0, -0.199, 1.316), (0.046, 0.007, 0.005), MOUTH)
ellipsoid("LowerLip", (0, -0.19, 1.297), (0.056, 0.022, 0.016), SKIN)
for s in (1, -1): ellipsoid("Ear", (s * 0.163, 0.015, 1.45), (0.032, 0.056, 0.061), SKIN)
# руки
for s, P in ((1, "Left_"), (-1, "Right_")):
    x0 = SH_X; x1 = x0 + UPPER; x2 = x1 + LOWER
    ellipsoid(P + "ShoulderBall", (s * (x0 + 0.005), 0, SH_Z + 0.005), (0.09, 0.08, 0.082), FUR)
    tube(P + "UpperArm", (s * x0, 0, SH_Z), (s * x1, 0, SH_Z), 0.066, 0.058, FUR)
    ellipsoid(P + "Elbow", (s * x1, 0, SH_Z), (0.063, 0.061, 0.061), FUR)
    tube(P + "Forearm", (s * x1, 0, SH_Z), (s * (x2 - 0.012), 0, SH_Z), 0.072, 0.055, FUR)
    ellipsoid(P + "Wrist", (s * (x2 - 0.01), 0, SH_Z), (0.05, 0.052, 0.047), FUR)
    ellipsoid(P + "Palm", (s * (x2 + 0.045), 0, SH_Z - 0.004), (0.06, 0.06, 0.03), SKIN)
    for fname, r in (("Index", 0.0172), ("Middle", 0.019), ("Thumb", 0.0185)):
        along_bone(P + fname + "P", P + fname + "Proximal", r, SKIN, 0.66)
        along_bone(P + fname + "I", P + fname + "Intermediate", r * 0.95, SKIN, 0.7)
        ellipsoid(P + fname + "Knuckle", bone_specs[P + fname + "Intermediate"][0], (r * 1.02,) * 3, SKIN, seg=(12, 8))

# ------------------------------------------------------------------ слияние в один меш
depsgraph = bpy.context.evaluated_depsgraph_get()
trees = [(BVHTree.FromObject(o, depsgraph), mi, o.name) for o, mi in parts]
bpy.ops.object.select_all(action="DESELECT")
copies = []
for o, mi in parts:
    c = o.copy(); c.data = o.data.copy(); bpy.context.collection.objects.link(c); copies.append(c)
for c in copies: c.select_set(True)
bpy.context.view_layer.objects.active = copies[0]
bpy.ops.object.join()
mono = bpy.context.view_layer.objects.active; mono.name = "Mono"
rm = mono.modifiers.new("remesh", "REMESH"); rm.mode = "VOXEL"; rm.voxel_size = VOXEL; rm.adaptivity = 0.0
bpy.ops.object.modifier_apply(modifier="remesh")
sm = mono.modifiers.new("smooth", "LAPLACIANSMOOTH"); sm.lambda_factor = 0.45; sm.iterations = 4; sm.use_volume_preserve = True
bpy.ops.object.modifier_apply(modifier="smooth")
tris_before = sum(len(p.vertices) - 2 for p in mono.data.polygons)
dm = mono.modifiers.new("dec", "DECIMATE"); dm.decimate_type = "COLLAPSE"; dm.ratio = TARGET_TRIS / max(1, tris_before)
dm.use_symmetry = True; dm.symmetry_axis = "X"
bpy.ops.object.modifier_apply(modifier="dec")
for o, _ in parts: bpy.data.objects.remove(o)   # исходники больше не нужны (деревья уже построены)
print(f"REMESH tris {tris_before} -> {sum(len(p.vertices) - 2 for p in mono.data.polygons)}")

# ------------------------------------------------------------------ материалы по ближайшей исходной поверхности
for m in M: mono.data.materials.append(m)
for poly in mono.data.polygons:
    c = mono.matrix_world @ poly.center
    best, bm_i = 1e9, FUR
    for tree, mi, nm in trees:
        hit = tree.find_nearest(c)
        if hit[0] is not None and hit[3] < best - (0.0015 if mi == FUR else 0.0):   # на границе — к серому/рту
            best, bm_i = hit[3], mi
    poly.material_index = bm_i
    poly.use_smooth = True
# сглаживание границ материалов (мажоритарный фильтр по соседним граням): ровный овал живота и маска без «лесенки»
bmf = bmesh.new(); bmf.from_mesh(mono.data); bmf.faces.ensure_lookup_table()
for _ in range(3):
    new = []
    for f in bmf.faces:
        votes = {}
        for e in f.edges:
            for g in e.link_faces:
                if g is not f: votes[g.material_index] = votes.get(g.material_index, 0) + 1
        top = max(votes.items(), key=lambda kv: kv[1]) if votes else (f.material_index, 0)
        new.append(top[0] if top[1] >= 2 and f.material_index != MOUTH and top[0] != MOUTH else f.material_index)
    for f, m in zip(bmf.faces, new): f.material_index = m
bmf.to_mesh(mono.data); bmf.free()

# ------------------------------------------------------------------ веса
groups = {n: mono.vertex_groups.new(name=n) for n in DEFORM}
for v in mono.data.vertices:
    p = mono.matrix_world @ v.co
    ds = {n: seg_dist(p, n) for n in DEFORM}
    dmin = min(ds.values())
    cands = {n: d for n, d in ds.items() if d <= dmin * 1.4 + 0.004}
    ws = {n: 1.0 / (d ** 6 + 1e-12) for n, d in cands.items()}
    tot = sum(ws.values())
    for n, w in ws.items():
        w /= tot
        if w > 0.02: groups[n].add([v.index], w, "REPLACE")
    if p.z > NECK_CUT + 0.02 or (p - Vector((0, -0.05, 1.42))).length < 0.13:   # голова — целиком на кости Head
        for n in DEFORM:
            if n != "Head": groups[n].remove([v.index])
        groups["Head"].add([v.index], 1.0, "REPLACE")

# UV для зернистой шерсти
bpy.context.view_layer.objects.active = mono; mono.select_set(True)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
bpy.ops.object.mode_set(mode="OBJECT")

# ------------------------------------------------------------------ разрез по шее: Body + Head (общие вершины на разрезе)
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(mono.data)
for v in bm.verts: v.select = False
for e in bm.edges: e.select = False
for f in bm.faces: f.select = False
for f in bm.faces:
    if (mono.matrix_world @ f.calc_center_median()).z > NECK_CUT: f.select_set(True)
bm.select_mode = {"FACE"}; bm.select_flush_mode(); bmesh.update_edit_mesh(mono.data)
bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
head = next(o for o in bpy.context.selected_objects if o != mono); head.name = "Head"; head.data.name = "Head"
mono.name = "Body"; mono.data.name = "Body"; body = mono
for o in (body, head):
    o.data.validate(clean_customdata=False)            # вырожденные грани после Decimate
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)   # Unity/Quest: до 4 костей на вершину
    bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
    o.parent = arm_obj; md = o.modifiers.new("Armature", "ARMATURE"); md.object = arm_obj
def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
print(f"TRIS body={tris(body)} head={tris(head)} total={tris(body) + tris(head)}")
for o in (body, head): print(f"UNWEIGHTED {o.name}={sum(1 for v in o.data.vertices if not v.groups)}")
bm = bmesh.new(); bm.from_mesh(body.data); bm.from_mesh(head.data)
print(f"MATERIALS fur={sum(1 for p in body.data.polygons if p.material_index == FUR) + sum(1 for p in head.data.polygons if p.material_index == FUR)} "
      f"skin={sum(1 for o in (body, head) for p in o.data.polygons if p.material_index == SKIN)} mouth={sum(1 for o in (body, head) for p in o.data.polygons if p.material_index == MOUTH)}")
bm.free()

# ------------------------------------------------------------------ экспорт FBX для Unity
bpy.ops.object.select_all(action="DESELECT")
for o in (arm_obj, body, head): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False, axis_forward="-Z", axis_up="Y",
                         mesh_smooth_type="FACE", add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False)
print("EXPORTED", OUT)

# ------------------------------------------------------------------ GLB для просмотра: цвета, зерно меха и глаза под бровями
def setup(m, rgb, tex=None, rough=1.0):
    m.use_nodes = True; b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = 0
    if tex:
        t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = tex; t.interpolation = "Closest"
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
def tinted(name, rgb):
    if not os.path.exists(FUR_TEX): return None
    src = bpy.data.images.load(FUR_TEX); px = list(src.pixels)
    for i in range(0, len(px), 4): px[i] *= rgb[0]; px[i + 1] *= rgb[1]; px[i + 2] *= rgb[2]
    img = bpy.data.images.new(name, *src.size); img.pixels = px; img.pack(); return img
setup(M[FUR], (0.45, 0.3, 0.2), tinted("FurTex", (0.45, 0.3, 0.2)))
setup(M[SKIN], (0.6, 0.6, 0.62), tinted("SkinTex", (0.64, 0.64, 0.66)))
setup(M[MOUTH], (0.06, 0.04, 0.05))
white = bpy.data.materials.new("EyeWhite"); setup(white, (1, 1, 1), rough=0.35)
black = bpy.data.materials.new("Pupil"); setup(black, (0.02, 0.02, 0.02), rough=0.3)
head.data.materials.append(white); head.data.materials.append(black)
wi, pi = len(head.data.materials) - 2, len(head.data.materials) - 1
bm = bmesh.new(); bm.from_mesh(head.data)
dl = bm.verts.layers.deform.verify(); hg = head.vertex_groups["Head"].index
for x in (0.0625, -0.0625):                     # как в игре: центр глаза на 1.464, под надбровной дугой
    for (c, r, mi) in (((x, -0.165, 1.464), (0.045, 0.026, 0.036), wi), ((x, -0.188, 1.462), (0.017, 0.008, 0.019), pi)):
        res = bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=1.0)
        bmesh.ops.transform(bm, matrix=Matrix.Translation(c) @ Matrix.Diagonal((*r, 1.0)), verts=res["verts"])
        for v in res["verts"]:
            v[dl][hg] = 1.0
            for f in v.link_faces: f.material_index = mi; f.smooth = True
bm.to_mesh(head.data); bm.free()
os.makedirs(os.path.dirname(GLB), exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
for o in (arm_obj, body, head): o.select_set(True)
bpy.ops.export_scene.gltf(filepath=GLB, export_format="GLB", use_selection=True, export_yup=True)
print("GLB", GLB)
