# Чистая стилизованная горилла игрока GrabzVR в духе Gorilla Tag / GILLA (Blender 5.x, без интерфейса):
#   blender -b --factory-startup -P build_gilla_clean.py
# Гладкий безногий торс (сужается книзу) с серым овалом на животе, круглая голова с серыми ушами, серой «маской»
# лица и выступающей мордочкой с полоской рта, длинные руки в T-позе, кисти с 3 пальцами (большой, указательный,
# средний) по 2 фаланги. Материалы: Fur_Main (перекрашиваемая шерсть), Skin_Grey (лицо, уши, ладони, живот),
# Mouth_Dark (полоска рта). Скелет с именами костей как у прежней Gorilla_Final — весь код игры (GorillaFinalRig,
# VRHandPoseController, GorillaFallRagdoll, реакции лица) работает без правок:
#   Hips > Spine > Chest > UpperChest > Neck > Head (+ Left_Eye/Right_Eye),
#   UpperChest > {Left,Right}_Shoulder > _UpperArm > _LowerArm > _Hand > _{Thumb,Index,Middle}{Proximal,Intermediate,Distal}
#   (+ _IndexDistalEnd — точка касания клавиш GRABZ OS).
# Веса — плавные, по расстоянию до костей с нормализацией внутри каждой части (без «заломов» на сгибах; авто-веса
# Blender на склеенных примитивах дают дыры). Меши: Body (слой Player) и Head (слой LocalHead в Unity).
# Масштаб финальный (метры): глаза-кости на высоте 1.45, рука плечо→запястье 0.55.
import bpy, bmesh, math, os
from mathutils import Matrix, Vector

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "Gorilla_GillaClean.fbx"))
E = 1.45            # высота костей глаз
SH_Z = 1.20         # высота плеч
SH_X = 0.165        # плечевой сустав
UPPER, LOWER = 0.27, 0.28

bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    return m
M_FUR = mat("Fur_Main", (0.35, 0.33, 0.32))
M_SKIN = mat("Skin_Grey", (0.55, 0.55, 0.57))
M_MOUTH = mat("Mouth_Dark", (0.08, 0.06, 0.07))

# ------------------------------------------------------------------ кости
bone_specs = {}   # имя -> (head, tail, parent)
def bone(name, h, t, parent=None): bone_specs[name] = (Vector(h), Vector(t), parent)

bone("Hips", (0, 0, 0.72), (0, 0, 0.88))
bone("Spine", (0, 0, 0.88), (0, 0, 1.02), "Hips")
bone("Chest", (0, 0, 1.02), (0, 0, 1.14), "Spine")
bone("UpperChest", (0, 0, 1.14), (0, 0, 1.25), "Chest")
bone("Neck", (0, 0, 1.25), (0, 0, 1.31), "UpperChest")
bone("Head", (0, 0, 1.31), (0, 0, 1.60), "Neck")
for s, P in ((1, "Left_"), (-1, "Right_")):   # левая сторона персонажа = +X в Blender (лицом к -Y)
    bone(P + "Eye", (s * 0.0625, -0.12, E), (s * 0.0625, -0.16, E), "Head")
    bone(P + "Shoulder", (s * 0.05, 0, SH_Z), (s * SH_X, 0, SH_Z), "UpperChest")
    x0 = SH_X; x1 = x0 + UPPER; x2 = x1 + LOWER; x3 = x2 + 0.085
    bone(P + "UpperArm", (s * x0, 0, SH_Z), (s * x1, 0, SH_Z), P + "Shoulder")
    bone(P + "LowerArm", (s * x1, 0, SH_Z), (s * x2, 0, SH_Z), P + "UpperArm")
    bone(P + "Hand", (s * x2, 0, SH_Z), (s * x3, 0, SH_Z), P + "LowerArm")
    # пальцы: указательный (вперёд, к -Y) и средний — из конца ладони, большой — из передней кромки ладони вперёд-наружу
    for fname, y, l1, l2 in (("Index", -0.022, 0.052, 0.046), ("Middle", 0.024, 0.056, 0.048)):
        a = Vector((s * x3, y, SH_Z)); d = Vector((s, 0, -0.08)).normalized()
        b = a + d * l1; c = b + d * l2; tip = c + d * 0.018
        bone(P + fname + "Proximal", a, b, P + "Hand")
        bone(P + fname + "Intermediate", b, c, P + fname + "Proximal")
        bone(P + fname + "Distal", c, tip, P + fname + "Intermediate")
        if fname == "Index": bone(P + "IndexDistalEnd", tip, tip + d * 0.012, P + "IndexDistal")
    a = Vector((s * (x2 + 0.035), -0.045, SH_Z - 0.006)); d = Vector((s * 0.5, -0.86, -0.12)).normalized()
    b = a + d * 0.044; c = b + d * 0.038; tip = c + d * 0.016
    bone(P + "ThumbProximal", a, b, P + "Hand")
    bone(P + "ThumbIntermediate", b, c, P + "ThumbProximal")
    bone(P + "ThumbDistal", c, tip, P + "ThumbIntermediate")

arm_data = bpy.data.armatures.new("Armature")
arm_obj = bpy.data.objects.new("Armature", arm_data)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")
for name, (h, t, p) in bone_specs.items():
    eb = arm_data.edit_bones.new(name); eb.head = h; eb.tail = t; eb.roll = 0
for name, (h, t, p) in bone_specs.items():
    if p: arm_data.edit_bones[name].parent = arm_data.edit_bones[p]; arm_data.edit_bones[name].use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")

def seg_dist(p, name):
    h, t, _ = bone_specs[name]; ab = t - h
    k = max(0.0, min(1.0, (p - h).dot(ab) / max(1e-9, ab.length_squared)))
    return (p - (h + ab * k)).length

# ------------------------------------------------------------------ детали меша
parts = []   # (объект, кандидаты-кости или None для жёсткой привязки, жёсткая кость)
def new_obj(name, bm, material):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    ob = bpy.data.objects.new(name, me); ob.data.materials.append(material)
    bpy.context.collection.objects.link(ob); return ob

def ellipsoid(name, center, radii, material, seg=(16, 10), rot=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg[0], v_segments=seg[1], radius=1.0, calc_uvs=True)
    m = Matrix.Diagonal((*radii, 1.0))
    if rot is not None: m = rot.to_4x4() @ m
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ m, verts=bm.verts)
    return new_obj(name, bm, material)

def along(name, a, b, r0, r1, material, seg=10):
    """Скруглённая «колбаса» от a до b (радиусы r0 → r1): цилиндр без крышек + концы закрывают суставы-сферы."""
    a, b = Vector(a), Vector(b); d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=seg, radius1=r0, radius2=r1, depth=d.length, calc_uvs=True)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=bm.verts)
    return new_obj(name, bm, material)

def finger_seg(name, bname, material, r):
    h, t, _ = bone_specs[bname]; d = t - h
    rot = Vector((1, 0, 0)).rotation_difference(d.normalized()).to_matrix()
    return ellipsoid(name, (h + t) / 2, (d.length * 0.62, r, r * 0.92), material, (8, 5), rot)

# торс: сфера, вытянутая вверх и сужающаяся книзу
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=9, radius=1.0, calc_uvs=True)
for v in bm.verts:
    z = v.co.z; f = 0.7 + 0.3 * (z + 1) / 2          # снизу уже, но без острого «носика»
    top = 1.0 - 0.18 * max(0.0, z) ** 3               # плечи — чуть плоский верх
    v.co = Vector((v.co.x * 0.215 * f * top, v.co.y * 0.165 * f, 1.0 + z * 0.29))
torso = new_obj("Torso", bm, M_FUR)
sub = torso.modifiers.new("sub", "SUBSURF"); sub.levels = 1; sub.render_levels = 1
parts.append((torso, ["Hips", "Spine", "Chest", "UpperChest"], None))
parts.append((ellipsoid("Belly", (0, -0.092, 1.0), (0.13, 0.068, 0.17), M_SKIN, (12, 8)), ["Hips", "Spine", "Chest"], None))

# голова (отдельный меш Head): шар, серая маска лица, мордочка, рот, уши
head_parts = []
head_parts.append(ellipsoid("HeadBall", (0, 0, 1.43), (0.165, 0.158, 0.16), M_FUR, (14, 10)))
head_parts.append(ellipsoid("FacePlate", (0, -0.085, 1.452), (0.13, 0.085, 0.1), M_SKIN, (12, 8)))
head_parts.append(ellipsoid("Muzzle", (0, -0.118, 1.338), (0.098, 0.07, 0.066), M_SKIN, (12, 8)))
head_parts.append(ellipsoid("MouthLine", (0, -0.183, 1.314), (0.042, 0.008, 0.0055), M_MOUTH, (12, 6)))
for s in (1, -1):
    ear = ellipsoid("Ear", (s * 0.158, 0.01, 1.45), (0.028, 0.052, 0.058), M_SKIN, (14, 8))
    head_parts.append(ear)

# руки (по сторонам)
for s, P in ((1, "Left_"), (-1, "Right_")):
    x0 = SH_X; x1 = x0 + UPPER; x2 = x1 + LOWER; x3 = x2 + 0.085
    parts.append((ellipsoid(P + "ShoulderBall", (s * (x0 + 0.01), 0, SH_Z), (0.078, 0.072, 0.074), M_FUR, (10, 7)), ["UpperChest", P + "UpperArm"], None))
    parts.append((along(P + "UpperArmMesh", (s * x0, 0, SH_Z), (s * x1, 0, SH_Z), 0.066, 0.058, M_FUR), [P + "UpperArm", P + "LowerArm"], None))
    parts.append((ellipsoid(P + "Elbow", (s * x1, 0, SH_Z), (0.064, 0.062, 0.062), M_FUR, (8, 6)), [P + "UpperArm", P + "LowerArm"], None))
    parts.append((along(P + "ForearmMesh", (s * x1, 0, SH_Z), (s * (x2 - 0.01), 0, SH_Z), 0.07, 0.056, M_FUR), [P + "LowerArm", P + "Hand"], None))
    parts.append((ellipsoid(P + "Wrist", (s * (x2 - 0.005), 0, SH_Z), (0.05, 0.05, 0.046), M_FUR, (8, 6)), [P + "LowerArm", P + "Hand"], None))
    parts.append((ellipsoid(P + "Palm", (s * (x2 + 0.045), 0, SH_Z - 0.004), (0.058, 0.058, 0.028), M_SKIN, (10, 6)), None, P + "Hand"))
    for fname, r in (("Index", 0.0175), ("Middle", 0.0195), ("Thumb", 0.019)):
        parts.append((finger_seg(P + fname + "P", P + fname + "Proximal", M_SKIN, r), None, P + fname + "Proximal"))
        parts.append((finger_seg(P + fname + "I", P + fname + "Intermediate", M_SKIN, r * 0.95), None, P + fname + "Intermediate"))
        tip = bone_specs[P + fname + "Distal"][0]
        parts.append((ellipsoid(P + fname + "Knuckle", bone_specs[P + fname + "Intermediate"][0], (r * 1.02,) * 3, M_SKIN, (6, 4)), [P + fname + "Proximal", P + fname + "Intermediate"], None))

# ------------------------------------------------------------------ веса
def weigh(ob, cands=None, rigid=None):
    bpy.context.view_layer.objects.active = ob
    for m in ob.modifiers: bpy.ops.object.modifier_apply(modifier=m.name)
    names = [rigid] if rigid else cands
    groups = {n: ob.vertex_groups.new(name=n) for n in names}
    for v in ob.data.vertices:
        p = ob.matrix_world @ v.co
        if rigid:
            groups[rigid].add([v.index], 1.0, "REPLACE"); continue
        ws = {n: 1.0 / (seg_dist(p, n) ** 4 + 1e-7) for n in names}
        tot = sum(ws.values())
        for n, w in ws.items():
            w /= tot
            if w > 0.01: groups[n].add([v.index], w, "REPLACE")

for ob, cands, rigid in parts: weigh(ob, cands, rigid)
for ob in head_parts: weigh(ob, rigid="Head")

def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    # UV для зернистой текстуры шерсти: развёртка по проекции (шум изотропный, швы не видны)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")
    return o

body = join([p[0] for p in parts], "Body")
head = join(head_parts, "Head")
for o in (body, head):
    o.parent = arm_obj
    mod = o.modifiers.new("Armature", "ARMATURE"); mod.object = arm_obj

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
print(f"TRIS body={tris(body)} head={tris(head)} total={tris(body) + tris(head)}")
print("BONES", len(arm_data.bones))

# проверка весов: каждая вершина привязана
for o in (body, head):
    bad = sum(1 for v in o.data.vertices if not v.groups)
    print(f"UNWEIGHTED {o.name}={bad}")

bpy.ops.object.select_all(action="DESELECT")
for o in (arm_obj, body, head): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False, axis_forward="-Z", axis_up="Y",
                         mesh_smooth_type="FACE", add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False)
# .blend в Assets не сохраняем: Unity импортирует его через Blender, а у кого Blender нет — ошибка импорта
print("EXPORTED", OUT)
