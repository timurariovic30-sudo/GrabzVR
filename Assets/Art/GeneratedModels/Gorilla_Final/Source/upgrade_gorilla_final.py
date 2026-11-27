# Апгрейд проверенной модели Gorilla_Final (Tripo) для GrabzVR (Blender 5.x, без интерфейса):
#   blender -b --factory-startup -P upgrade_gorilla_final.py
# Форма головы и торса — исходная. Изменения:
#  1) убран «3D-пух»: мелкие отдельные лоскутки-волоски (острова меша) удаляются, остаток слегка сглаживается;
#  2) ноги удаляются (как в прежнем GorillaFinalAvatarBuilder), отверстия закрываются;
#  3) старые 5-палые кисти срезаны, вместо них — стилизованные ладони с 3 толстыми пальцами (большой, указательный,
#     средний) и манжетой меха поверх среза предплечья; кости Thumb/Index/Middle перенесены под новые пальцы,
#     Ring/Pinky удалены; веса — плавные по расстоянию до костей (локоть/запястье/фаланги гнутся мягко);
#  4) материалы: Fur_Main (окрашиваемый мех) и Skin_Grey (лицо, уши, живот, ладони) — по исходной текстуре
#     (зелёный = мех), затем равномерная развёртка под зернистую текстуру в Unity; полоска рта Mouth_Dark;
#  5) меш делится на Body и Head (голова — на слое LocalHead в Unity).
# Выход: ../Gorilla_Final_3F.fbx (исходный Gorilla_Final.fbx не меняется).
import bpy, bmesh, math, os
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "Gorilla_Final.fbx")
TEX = os.path.join(HERE, "..", "Textures", "Gorilla_Final_BaseColor.jpg")
OUT = os.path.abspath(os.path.join(HERE, "..", "Gorilla_Final_3F.fbx"))
PREVIEW = os.environ.get("GF_PREVIEW")          # папка для контрольных рендеров (необязательно)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH")
for o in bpy.data.objects:
    if o.type == "MESH" and o != body: bpy.data.objects.remove(o)
bones = arm.data.bones
def bw(name): return arm.matrix_world @ bones[name].head_local
def bt(name): return arm.matrix_world @ bones[name].tail_local
K = (bw("Left_LowerArm") - bw("Left_UpperArm")).length + (bw("Left_Hand") - bw("Left_LowerArm")).length
K /= 0.55                                   # модельные единицы на «игровой метр» (рука 0.55 м)
print(f"SCALE K={K:.4f}")

# ------------------------------------------------------------------ текстура: зелёный = мех
img = bpy.data.images.load(TEX); W, H = img.size; PX = img.pixels[:]
def green(uv):
    x = min(W - 1, max(0, int((uv.x % 1.0) * W))); y = min(H - 1, max(0, int((uv.y % 1.0) * H)))
    i = (y * W + x) * 4; r, g, b = PX[i], PX[i + 1], PX[i + 2]
    return g > 0.15 and g > r * 1.12 and g > b * 1.12

me = body.data
vg = {g.index: g.name for g in body.vertex_groups}
LEG = {n for n in vg.values() if any(k in n for k in ("UpperLeg", "LowerLeg", "Foot", "Toes"))}
HAND = {n for n in vg.values() if n.endswith("_Hand") or any(k in n for k in ("Thumb", "Index", "Middle", "Ring", "Pinky"))}
HEADB = {"Head", "Left_Eye", "Right_Eye", "Neck", "Neck_Twist_A"}

bm = bmesh.new(); bm.from_mesh(me)
dl = bm.verts.layers.deform.verify(); uvl = bm.loops.layers.uv.active
def wsum(v, names): return sum(w for gi, w in v[dl].items() if vg.get(gi) in names)

# класс материала каждой грани по исходной текстуре (запоминаем до любых правок)
fur_layer = bm.faces.layers.int.new("isfur")
for f in bm.faces:
    votes = sum(1 for l in f.loops if green(l[uvl].uv)) + (2 if green(sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)) else 0)
    f[fur_layer] = 1 if votes >= 3 else 0

# 1) «пух»: острова меша меньше 2 % от самого большого
islands, seen = [], set()
for f in bm.faces:
    if f.index in seen: continue
    stack, isl = [f], []
    seen.add(f.index)
    while stack:
        g = stack.pop(); isl.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); stack.append(h)
    islands.append(isl)
islands.sort(key=len, reverse=True)
big = len(islands[0])
kill = [f for isl in islands[1:] if len(isl) < big * 0.02 for f in isl]
print(f"ISLANDS {len(islands)} main {big} faces, removed {len(kill)} faces in small islands")
bmesh.ops.delete(bm, geom=kill, context="FACES")

# 2) ноги и 3) старые кисти
legs = [f for f in bm.faces if sum(wsum(v, LEG) for v in f.verts) / len(f.verts) > 0.5]
bmesh.ops.delete(bm, geom=legs, context="FACES")
hands = [f for f in bm.faces if sum(wsum(v, HAND) for v in f.verts) / len(f.verts) > 0.45]
bmesh.ops.delete(bm, geom=hands, context="FACES")
# ровные срезы плоскостью: предплечья чуть раньше запястья (лохматые края уходят под манжету), низ торса над ногами
WL0, WR0 = bw("Left_Hand"), bw("Right_Hand")
cut_info = {}
for P, W0 in (("Left_", WL0), ("Right_", WR0)):
    ax = (W0 - bw(P + "LowerArm")).normalized(); plane = W0 - ax * 0.05 * K; cut_info[P] = (plane, ax)
    gone = [f for f in bm.faces if (f.calc_center_median() - plane).dot(ax) > 0 and abs(f.calc_center_median().x) > abs(W0.x) * 0.6]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
# торчащие «лоскутки» шерсти перед срезом: всё, что дальше 1.15 медианного радиуса от оси предплечья
for P, W0 in (("Left_", WL0), ("Right_", WR0)):
    plane, ax = cut_info[P]; base = bw(P + "LowerArm")
    near = [f for f in bm.faces if -0.25 * K < (f.calc_center_median() - plane).dot(ax) <= 0 and abs(f.calc_center_median().x) > abs(W0.x) * 0.6]
    if near: base = sum((f.calc_center_median() for f in near), Vector()) / len(near)   # центр руки у среза (кость Tripo смещена от центра)
    def rad(p): q = p - base; return (q - ax * q.dot(ax)).length
    rads = sorted(rad(f.calc_center_median()) for f in near)
    if rads:
        med = rads[len(rads) // 2]
        def side_on(f):        # «карточка» шерсти: нормаль почти перпендикулярна направлению от оси руки
            q = f.calc_center_median() - base; rdir = (q - ax * q.dot(ax))
            return rdir.length > 1e-6 and abs(f.normal.dot(rdir.normalized())) < 0.35
        flaps = [f for f in near if rad(f.calc_center_median()) > med * 1.1 or side_on(f)]
        bmesh.ops.delete(bm, geom=flaps, context="FACES")
        print(f"FLAPS {P}: removed {len(flaps)} of {len(near)} faces near the cut")
z_cut = bw("Hips").z - 0.02 * K
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z < z_cut], context="FACES")
loose = [v for v in bm.verts if not v.link_faces]; bmesh.ops.delete(bm, geom=loose, context="VERTS")
# мелкие острова, оставшиеся после срезов (обрывки шерсти)
seen, isl_all = set(), []
for f in bm.faces:
    if f.index in seen: continue
    st, isl = [f], []; seen.add(f.index)
    while st:
        g = st.pop(); isl.append(g)
        for ed in g.edges:
            for h in ed.link_faces:
                if h.index not in seen: seen.add(h.index); st.append(h)
    isl_all.append(isl)
isl_all.sort(key=len, reverse=True)
bmesh.ops.delete(bm, geom=[f for isl in isl_all[1:] for f in isl], context="FACES")
bm.faces.index_update()
# радиус предплечья по кромке среза
wrist_r = {}
for P, (plane, ax) in cut_info.items():
    ring = [v.co for v in bm.verts if v.is_boundary and abs((v.co - plane).dot(ax)) < 0.04 * K and (v.co - plane).length < 0.25 * K]
    if ring:
        cen = sum(ring, Vector()) / len(ring)
        wrist_r[P] = (sum(((p - cen) - ax * (p - cen).dot(ax)).length for p in ring) / len(ring), cen)
    else: wrist_r[P] = (0.06 * K, plane)
print(f"REMOVED legs {len(legs)} hands {len(hands)}; forearm r L {wrist_r['Left_'][0] / K:.3f} R {wrist_r['Right_'][0] / K:.3f} m")
# закрываем отверстия; низ торса — куполом (центр опускаем)
bnd = [e for e in bm.edges if e.is_boundary]
res = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
for f in res["faces"]: f[fur_layer] = 1
caps = [f for f in res["faces"] if f.calc_center_median().z < z_cut + 0.05 * K]
pk = bmesh.ops.poke(bm, faces=caps)
for v in pk["verts"]: v.co.z -= 0.05 * K
for f in pk["faces"]: f[fur_layer] = 1
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
bm.to_mesh(me); bm.free()

# лёгкое сглаживание остатков «лохматости» (форма сохраняется)
bpy.context.view_layer.objects.active = body; body.select_set(True)
sm = body.modifiers.new("smooth", "LAPLACIANSMOOTH"); sm.lambda_factor = 0.35; sm.iterations = 3; sm.use_volume_preserve = True
sm.vertex_group = ""
bpy.ops.object.modifier_apply(modifier="smooth")

# ------------------------------------------------------------------ новые кисти (строим для левой, правая — зеркало)
WL, WR = bw("Left_Hand"), bw("Right_Hand")
d = (bt("Left_Hand") - WL).normalized()
e = (bw("Left_PinkyProximal") - bw("Left_IndexProximal")); e = (e - d * e.dot(d)).normalized()   # от указательного к мизинцу
n = d.cross(e).normalized()
if n.z > 0: n = -n                                                                             # ладонь вниз
FR = Matrix((d, e, n)).transposed()            # столбцы: вдоль кисти, поперёк, нормаль ладони
def L(p): return Vector(p) * K                 # игровые метры → модельные единицы (в осях кисти)
def toW(local, wrist=WL, mirror=False):
    v = FR @ L(local)
    if mirror: v.x = -v.x
    return wrist + v

# радиус предплечья у запястья (по оставшимся вершинам)
axis0 = bw("Left_LowerArm"); axisd = (WL - axis0).normalized()
rs = [((body.matrix_world @ v.co) - axis0 - axisd * ((body.matrix_world @ v.co) - axis0).dot(axisd)).length
      for v in body.data.vertices
      if 0.75 < ((body.matrix_world @ v.co) - axis0).dot(axisd) / max(1e-6, (WL - axis0).length) < 1.02]
r_wrist = wrist_r["Left_"][0]

finger_specs = [  # имя, основание (в осях кисти), направление, длины фаланг, радиус
    # кисть компактная (−40 % от прежней), соразмерна предплечью
    ("Index", (0.081, -0.028, 0.0), (1, 0, 0.09), (0.045, 0.038, 0.014), 0.021),
    ("Middle", (0.081, 0.029, 0.0), (1, 0, 0.09), (0.047, 0.04, 0.014), 0.023),
    ("Thumb", (0.036, -0.054, 0.011), (0.5, -0.84, 0.2), (0.037, 0.031, 0.013), 0.021),
]
new_bones = {}   # (side, name) -> (head, tail)
for side, P, wrist, mir in (("L", "Left_", WL, False), ("R", "Right_", WR, True)):
    for fname, base, dirv, lens, rad in finger_specs:
        dv = Vector(dirv).normalized()
        a = Vector(base); b = a + dv * lens[0]; c = b + dv * lens[1]; t = c + dv * lens[2]
        pts = [toW(p, wrist, mir) for p in (a, b, c, t, t + dv * 0.012)]
        for i, part in enumerate(("Proximal", "Intermediate", "Distal", "DistalEnd")):
            new_bones[P + fname + part] = (pts[i], pts[i + 1])

# геометрия кисти: ладонь, манжета, фаланги, костяшки → один меш через Voxel Remesh
def ellipsoid_world(bm, center, radii_local, rot3, seg=(18, 12)):
    r = bmesh.ops.create_uvsphere(bm, u_segments=seg[0], v_segments=seg[1], radius=1.0)
    m = Matrix.Translation(center) @ rot3.to_4x4() @ Matrix.Diagonal((*radii_local, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=r["verts"])
def tube_world(bm, a, b, r0, r1):
    dvec = b - a
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=r0, radius2=r1, depth=dvec.length)
    rot = Vector((0, 0, 1)).rotation_difference(dvec.normalized()).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=r["verts"])

hand_objs = []
for side, P, wrist, mir in (("L", "Left_", WL, False), ("R", "Right_", WR, True)):
    fr = FR.copy()
    if mir: fr = Matrix.Diagonal((-1, 1, 1)) @ fr
    parts = []   # (bm-объект, is_fur)
    def part(builder, fur):
        bmp = bmesh.new(); builder(bmp)
        mp = bpy.data.meshes.new("p"); bmp.to_mesh(mp); bmp.free()
        ob = bpy.data.objects.new("p", mp); bpy.context.collection.objects.link(ob); parts.append((ob, fur))
    fwd = fr @ Vector((1, 0, 0))
    rw, cen = wrist_r[P]
    # манжета меха: от кромки среза (её центр и радиус) плавно к запястью; край среза скруглён шаром того же радиуса
    # манжета «выходит» из предплечья: начинается тоньше руки глубоко внутри, к срезу чуть шире её, дальше сужается к ладони
    part(lambda bmp, rw=rw, cen=cen: tube_world(bmp, cen - fwd * 0.12 * K, cen, rw * 0.88, rw * 1.03), True)
    part(lambda bmp, rw=rw, cen=cen: tube_world(bmp, cen, wrist + fwd * 0.03 * K, rw * 1.03, 0.058 * K), True)
    part(lambda bmp: ellipsoid_world(bmp, toW((0.045, 0, -0.002), wrist, mir), (0.06 * K, 0.062 * K, 0.033 * K), fr), False)  # ладонь
    for fname, base, dirv, lens, rad in finger_specs:
        for i, part_name in enumerate(("Proximal", "Intermediate")):
            h, t = new_bones[P + fname + part_name]
            dv = (t - h); rot = Vector((1, 0, 0)).rotation_difference(dv.normalized()).to_matrix()
            rr = rad * (1.0 if i == 0 else 0.93) * K
            part(lambda bmp, h=h, t=t, rot=rot, rr=rr, dv=dv: ellipsoid_world(bmp, (h + t) / 2, (dv.length * 0.7, rr, rr * 0.92), rot, (14, 9)), False)
        kn = new_bones[P + fname + "Intermediate"][0]
        part(lambda bmp, kn=kn, rad=rad: ellipsoid_world(bmp, kn, (rad * 1.02 * K,) * 3, Matrix.Identity(3), (10, 7)), False)
    trees = [(BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get()), fur) for o, fur in parts]
    bpy.ops.object.select_all(action="DESELECT")
    for o, _ in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0][0]; bpy.ops.object.join()
    hobj = bpy.context.view_layer.objects.active; hobj.name = P + "NewHand"
    rm = hobj.modifiers.new("r", "REMESH"); rm.mode = "VOXEL"; rm.voxel_size = 0.0026 * K; bpy.ops.object.modifier_apply(modifier="r")
    s2 = hobj.modifiers.new("s", "LAPLACIANSMOOTH"); s2.lambda_factor = 0.4; s2.iterations = 3; s2.use_volume_preserve = True; bpy.ops.object.modifier_apply(modifier="s")
    t0 = len(hobj.data.polygons)
    dm = hobj.modifiers.new("d", "DECIMATE"); dm.ratio = 800 / max(1, t0); bpy.ops.object.modifier_apply(modifier="d")
    bmh = bmesh.new(); bmh.from_mesh(hobj.data)
    fl = bmh.faces.layers.int.new("isfur")
    for f in bmh.faces:
        c = f.calc_center_median(); best, isf = 1e9, 0
        for tree, fur in trees:
            hit = tree.find_nearest(c)
            if hit[0] is not None and hit[3] < best: best, isf = hit[3], (1 if fur else 0)
        f[fl] = isf
    bmh.to_mesh(hobj.data); bmh.free()
    hand_objs.append(hobj)

# ------------------------------------------------------------------ кости пальцев: переносим, Ring/Pinky удаляем
bpy.ops.object.select_all(action="DESELECT"); bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones; inv = arm.matrix_world.inverted()
for name, (h, t) in new_bones.items():
    if name in eb: eb[name].head = inv @ h; eb[name].tail = inv @ t
for name in [b.name for b in eb if "Ring" in b.name or "Pinky" in b.name]: eb.remove(eb[name])
bpy.ops.object.mode_set(mode="OBJECT")
for g in list(body.vertex_groups):
    if "Ring" in g.name or "Pinky" in g.name: body.vertex_groups.remove(g)

# ------------------------------------------------------------------ веса новых кистей
def seg_dist(p, name):
    h = arm.matrix_world @ arm.data.bones[name].head_local; t = arm.matrix_world @ arm.data.bones[name].tail_local; ab = t - h
    k = max(0.0, min(1.0, (p - h).dot(ab) / max(1e-12, ab.length_squared))); return (p - (h + ab * k)).length
for hobj, P in zip(hand_objs, ("Left_", "Right_")):
    cand = [P + "LowerArm", P + "Hand"] + [P + f + s for f in ("Thumb", "Index", "Middle") for s in ("Proximal", "Intermediate", "Distal")]
    groups = {c: hobj.vertex_groups.new(name=c) for c in cand}
    for v in hobj.data.vertices:
        p = hobj.matrix_world @ v.co
        ds = {c: seg_dist(p, c) for c in cand}; dmin = min(ds.values())
        ws = {c: 1.0 / (dd ** 6 + 1e-24) for c, dd in ds.items() if dd <= dmin * 1.4 + 0.004 * K}
        tot = sum(ws.values())
        for c, w in ws.items():
            if w / tot > 0.02: groups[c].add([v.index], w / tot, "REPLACE")

# ------------------------------------------------------------------ сборка: тело + кисти, материалы
bpy.ops.object.select_all(action="DESELECT")
for o in [body] + hand_objs: o.select_set(True)
bpy.context.view_layer.objects.active = body; bpy.ops.object.join()
body.data.materials.clear()
for nm, rgb in (("Fur_Main", (0.35, 0.33, 0.32)), ("Skin_Grey", (0.55, 0.55, 0.57)), ("Mouth_Dark", (0.08, 0.06, 0.07))):
    m = bpy.data.materials.new(nm); m.diffuse_color = (*rgb, 1); body.data.materials.append(m)
bm = bmesh.new(); bm.from_mesh(body.data); fl = bm.faces.layers.int.get("isfur")
for f in bm.faces: f.material_index = 0 if (fl and f[fl]) else 1; f.smooth = True
# сглаживание границ мех/серое (мажоритарный фильтр) — чёткое, без «лесенки» и одиночных пятен
bm.faces.ensure_lookup_table()
for _ in range(6):
    new = []
    for f in bm.faces:
        votes = {}
        for ed in f.edges:
            for g in ed.link_faces:
                if g is not f: votes[g.material_index] = votes.get(g.material_index, 0) + 1
        top = max(votes.items(), key=lambda kv: kv[1]) if votes else (f.material_index, 0)
        new.append(top[0] if top[1] >= 2 else f.material_index)
    for f, mi in zip(bm.faces, new): f.material_index = mi
bm.to_mesh(body.data); bm.free()

# полоска рта (исходная нарисованная пропала вместе с текстурой): на поверхности лица на 0.136 м ниже глаз
eye_mid = (bw("Left_Eye") + bw("Right_Eye")) / 2
mouth_z = eye_mid.z - 0.136 * K
dg = bpy.context.evaluated_depsgraph_get(); tree = BVHTree.FromObject(body, dg)
hit = tree.ray_cast(Vector((eye_mid.x, eye_mid.y - 0.5, mouth_z)), Vector((0, 1, 0)))
if hit[0] is not None:
    bm = bmesh.new(); bm.from_mesh(body.data); dl = bm.verts.layers.deform.verify()
    head_gi = body.vertex_groups["Head"].index
    r = bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=1.0)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(hit[0] + Vector((0, 0.002 * K, 0))) @ Matrix.Diagonal((0.038 * K, 0.006 * K, 0.0045 * K, 1)), verts=r["verts"])
    for v in r["verts"]:
        v[dl].clear(); v[dl][head_gi] = 1.0
        for f in v.link_faces: f.material_index = 2; f.smooth = True
    bm.to_mesh(body.data); bm.free()
    print(f"MOUTH at {hit[0]}")

# равномерная развёртка под зерно меха
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)

# ------------------------------------------------------------------ голова отдельным объектом (по весам головы, как в прежнем сборщике)
headset = {body.vertex_groups[n].index for n in HEADB if n in body.vertex_groups}
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(body.data); dl = bm.verts.layers.deform.verify()
for v in bm.verts: v.select = False
for ed in bm.edges: ed.select = False
for f in bm.faces:
    hw = sum(sum(w for gi, w in v[dl].items() if gi in headset) for v in f.verts) / len(f.verts)
    f.select = hw > 0.4
bm.select_mode = {"FACE"}; bm.select_flush_mode(); bmesh.update_edit_mesh(body.data)
bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
head = next(o for o in bpy.context.selected_objects if o != body)
head.name = head.data.name = "Head"; body.name = body.data.name = "Body"
for o in (body, head):
    o.data.validate(clean_customdata=False)
    if not any(m.type == "ARMATURE" for m in o.modifiers): md = o.modifiers.new("Armature", "ARMATURE"); md.object = arm
    o.parent = arm
def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
print(f"TRIS body={tris(body)} head={tris(head)}")
for o in (body, head): print(f"UNWEIGHTED {o.name}={sum(1 for v in o.data.vertices if not v.groups)}")

bpy.ops.object.select_all(action="DESELECT")
for o in (arm, body, head): o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False, axis_forward="-Z", axis_up="Y",
                         mesh_smooth_type="FACE", add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False)
print("EXPORTED", OUT)

if PREVIEW:
    s = bpy.context.scene
    for m, rgb in zip(body.data.materials, ((0.45, 0.3, 0.2), (0.62, 0.62, 0.64), (0.05, 0.03, 0.04))):
        m.use_nodes = True; nb = next(x for x in m.node_tree.nodes if x.type == "BSDF_PRINCIPLED")
        nb.inputs["Base Color"].default_value = (*rgb, 1); nb.inputs["Roughness"].default_value = 0.95
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); s.collection.objects.link(cam); s.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(20)); s.collection.objects.link(sun)
    w = bpy.data.worlds.new("w"); s.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    try: s.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError: s.render.engine = "BLENDER_EEVEE"
    s.render.resolution_x = s.render.resolution_y = 800
    def shot(name, dvec, target, dist):
        cam.location = target + Vector(dvec).normalized() * dist
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        s.render.filepath = os.path.join(PREVIEW, f"gf_{name}.png"); bpy.ops.render.render(write_still=True)
    shot("front", (0, -1, 0.1), Vector((0, 0, 0.5)), 1.4)
    shot("hand", (0.3, -1, 0.7), WL + d * 0.05 * K, 0.22)