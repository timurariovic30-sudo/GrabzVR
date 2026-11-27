// GRABZ STUDIO — 3D-офис (Three.js r128): этаж небоскрёба с панорамными окнами и городом на закате,
// живые люди (CC0 Quaternius «Animated Human», скелетная анимация), мебель из боксов, мигающие серверы.
// Два режима: от первого лица (WASD + мышь, Pointer Lock) и обзор сверху (OrbitControls, потолок скрыт).
// Логика (агенты, пути, задачи, события) — в app.js; здесь только мир, люди и камера.
// 1 тайл карты = 1 единица = 0.75 м; X → x, Y карты → z. Человек ≈ 2.3 ед., потолок 3.7 ед.
"use strict";

function initOffice3D(canvas) {
  const CEIL = 3.7, WALL_H = CEIL, K_H = 2.55 / 22;   // высота стен; px «над головой» (app.js) → единицы
  const EYE = 2.15, HUMAN_H = 2.3;
  const BX0 = 1, BX1 = 98, BZ0 = 1, BZ1 = 50;          // контур этажа (тайлы)
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  const SKY_TOP = "#2b2f6b", SKY_MID = "#e0708a", SKY_LOW = "#ffb36b";
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(SKY_MID);
  scene.fog = new THREE.Fog("#d88a8a", 160, 900);
  const camera = new THREE.PerspectiveCamera(70, 1, 0.08, 3000);
  const controls = new THREE.OrbitControls(camera, canvas);
  controls.enableDamping = true; controls.dampingFactor = 0.1;
  controls.maxPolarAngle = 1.32; controls.minDistance = 6; controls.maxDistance = 300;
  controls.screenSpacePanning = false;

  scene.add(new THREE.HemisphereLight("#fff1e6", "#5a4a52", 0.55));
  scene.add(new THREE.AmbientLight("#ffffff", 0.12));
  // низкое закатное солнце светит в окна с запада
  const sun = new THREE.DirectionalLight("#ffc48a", 0.75);
  sun.position.set(-60, 28, 40); sun.target.position.set(50, 0, 26);
  sun.castShadow = true; sun.shadow.mapSize.set(2048, 2048);
  Object.assign(sun.shadow.camera, { left: -70, right: 70, top: 60, bottom: -60, near: 10, far: 260 });
  sun.shadow.bias = -0.0008;
  scene.add(sun, sun.target);

  // ------------------------------------------------------------------ утилиты
  const roofParts = [], wallMeshes = [];   // потолок/крыша (скрыты в обзоре), стены (сжимаются в обзоре)
  const unit = new THREE.BoxGeometry(1, 1, 1);
  const mats = {};
  const mat = (color, extra) => {
    const key = color + (extra ? JSON.stringify(extra) : "");
    return mats[key] || (mats[key] = new THREE.MeshLambertMaterial(Object.assign({ color }, extra || {})));
  };
  function box(parent, w, h, d, color, x, y, z, opts = {}) {
    const m = new THREE.Mesh(unit, opts.material || mat(color, opts.mat));
    m.scale.set(w, h, d); m.position.set(x, y + h / 2, z);
    m.castShadow = opts.cast !== false; m.receiveShadow = true;
    parent.add(m); return m;
  }
  function cyl(parent, r, h, color, x, y, z, seg = 10) {
    const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, h, seg), mat(color));
    m.position.set(x, y + h / 2, z); m.castShadow = true; m.receiveShadow = true; parent.add(m); return m;
  }
  function blob(parent, r, color, x, y, z) {
    const m = new THREE.Mesh(new THREE.IcosahedronGeometry(r, 0), new THREE.MeshLambertMaterial({ color, flatShading: true }));
    m.position.set(x, y, z); m.castShadow = true; parent.add(m); return m;
  }
  function canvasTex(c) { const t = new THREE.CanvasTexture(c); t.magFilter = THREE.NearestFilter; t.anisotropy = renderer.capabilities.getMaxAnisotropy(); return t; }
  function texOf(f, pad = 14) {
    const c = document.createElement("canvas"); c.width = f.w * T; c.height = f.h * T + pad;
    const g = c.getContext("2d"); g.translate(-f.x * T, -f.y * T + pad); drawFurniture(g, f); return canvasTex(c);
  }
  function textTex(text, color, bg, w = 256, h = 48, font = "bold 26px Segoe UI") {
    const c = document.createElement("canvas"); c.width = w; c.height = h; const g = c.getContext("2d");
    g.fillStyle = bg; g.fillRect(0, 0, w, h); g.fillStyle = color; g.font = font; g.textAlign = "center"; g.textBaseline = "middle";
    g.fillText(text, w / 2, h / 2 + 1, w - 10); const t = new THREE.CanvasTexture(c); t.anisotropy = 4; return t;
  }
  function plane(parent, w, h, tex, x, y, z, rotY = 0, emissive) {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshLambertMaterial({ map: tex, transparent: true, emissive: emissive || 0, emissiveMap: emissive ? tex : null }));
    m.position.set(x, y, z); m.rotation.y = rotY; parent.add(m); return m;
  }

  // ------------------------------------------------------------------ пол: пиксель-арт текстура карты
  const FW = BX1 - BX0 + 1, FD = BZ1 - BZ0 + 1, FCX = BX0 + FW / 2, FCZ = BZ0 + FD / 2;
  {
    // пол этажа — только внутри здания (улица и парковка из 2D-карты в небоскрёбе не нужны)
    const c = document.createElement("canvas"); c.width = FW * T; c.height = FD * T; const g = c.getContext("2d"); const r = seeded(1234);
    g.translate(-BX0 * T, -BZ0 * T);
    for (let y = BZ0; y <= BZ1; y++) for (let x = BX0; x <= BX1; x++) drawFloorTile(g, x, y, floor[idx(x, y)] === "door" && y === BZ1 ? "marble" : floor[idx(x, y)], r);
    for (const f of furniture) if ((f.t === "rug" || f.t === "vrmat") && f.y < BZ1) drawFurniture(g, f);
    const tex = canvasTex(c); tex.minFilter = THREE.LinearMipmapLinearFilter;
    const m = new THREE.Mesh(new THREE.PlaneGeometry(FW, FD), new THREE.MeshLambertMaterial({ map: tex }));
    m.rotation.x = -Math.PI / 2; m.position.set(FCX, 0, FCZ); m.receiveShadow = true; scene.add(m);
  }

  // ------------------------------------------------------------------ небоскрёб, небо и город внизу
  const DROP = 230;   // этаж висит на ~170 м над улицей
  const winTex = (() => {
    const c = document.createElement("canvas"); c.width = 64; c.height = 128; const g = c.getContext("2d"); const r = seeded(77);
    g.fillStyle = "#1d2233"; g.fillRect(0, 0, 64, 128);
    for (let y = 2; y < 128; y += 8) for (let x = 2; x < 64; x += 8) { const lit = r() < 0.42; g.fillStyle = lit ? (r() < 0.6 ? "#ffd98a" : "#bfe3ff") : "#2c3550"; g.fillRect(x, y, 5, 5); }
    const t = new THREE.CanvasTexture(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.magFilter = THREE.NearestFilter; return t;
  })();
  {
    // небо — градиентная сфера
    const sky = new THREE.Mesh(new THREE.SphereGeometry(1400, 32, 16), new THREE.MeshBasicMaterial({ side: THREE.BackSide, vertexColors: true, fog: false }));
    const pos = sky.geometry.attributes.position, cols = [], cTop = new THREE.Color(SKY_TOP), cMid = new THREE.Color(SKY_MID), cLow = new THREE.Color(SKY_LOW), tmp = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const h = pos.getY(i) / 1400;
      if (h > 0.12) tmp.copy(cMid).lerp(cTop, Math.min(1, (h - 0.12) / 0.6)); else tmp.copy(cLow).lerp(cMid, Math.max(0, (h + 0.05) / 0.17));
      cols.push(tmp.r, tmp.g, tmp.b);
    }
    sky.geometry.setAttribute("color", new THREE.Float32BufferAttribute(cols, 3));
    sky.position.set(FCX, 0, FCZ); scene.add(sky);
    const sunDisc = new THREE.Mesh(new THREE.CircleGeometry(60, 32), new THREE.MeshBasicMaterial({ color: "#fff1b8", fog: false }));
    sunDisc.position.set(FCX - 1100, 70, FCZ + 520); sunDisc.lookAt(FCX, 0, FCZ); scene.add(sunDisc);

    // улицы далеко внизу
    const gc = document.createElement("canvas"); gc.width = gc.height = 256; const gg = gc.getContext("2d");
    gg.fillStyle = "#3a3a44"; gg.fillRect(0, 0, 256, 256); gg.fillStyle = "#24242b"; gg.fillRect(0, 108, 256, 40); gg.fillRect(108, 0, 40, 256);
    gg.fillStyle = "#e9d27a"; for (let i = 0; i < 256; i += 16) { gg.fillRect(i, 127, 8, 2); gg.fillRect(127, i, 2, 8); }
    const gt = new THREE.CanvasTexture(gc); gt.wrapS = gt.wrapT = THREE.RepeatWrapping; gt.repeat.set(40, 40);
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(4000, 4000), new THREE.MeshLambertMaterial({ map: gt }));
    ground.rotation.x = -Math.PI / 2; ground.position.set(FCX, -DROP, FCZ); scene.add(ground);

    // корпус нашей башни под этажом и над ним
    const towerMat = new THREE.MeshLambertMaterial({ map: winTex.clone(), emissive: "#ffffff", emissiveMap: winTex, emissiveIntensity: 0.35 });
    towerMat.map.needsUpdate = true; towerMat.map.repeat.set(12, 30);
    const tower = new THREE.Mesh(new THREE.BoxGeometry(FW + 1, DROP - 0.6, FD + 1), towerMat);
    tower.position.set(FCX, -DROP / 2 - 0.3, FCZ); scene.add(tower);
    const roof = new THREE.Mesh(new THREE.BoxGeometry(FW + 1, 30, FD + 1), towerMat);
    roof.position.set(FCX, CEIL + 0.3 + 15, FCZ); scene.add(roof);
    roofParts.push(roof);

    // соседние небоскрёбы
    const r = seeded(4242), N = 260;
    const bmat = new THREE.MeshLambertMaterial({ map: winTex, emissive: "#ffffff", emissiveMap: winTex, emissiveIntensity: 0.55 });
    const inst = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1), bmat, N);
    const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), sc = new THREE.Vector3(), p = new THREE.Vector3(), col = new THREE.Color();
    for (let i = 0; i < N; i++) {
      const ang = r() * Math.PI * 2, dist = 110 + Math.pow(r(), 0.7) * 700;
      const w = 30 + r() * 50, d = 30 + r() * 50, h = 80 + r() * (dist < 300 ? 330 : 260);
      p.set(FCX + Math.cos(ang) * dist, -DROP + h / 2, FCZ + Math.sin(ang) * dist);
      q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.round(r() * 4) * Math.PI / 2 + (r() - 0.5) * 0.3);
      sc.set(w, h, d); m4.compose(p, q, sc); inst.setMatrixAt(i, m4);
      inst.setColorAt(i, col.setHSL(0.6 + r() * 0.15, 0.15 + r() * 0.2, 0.55 + r() * 0.3));
    }
    scene.add(inst);
  }

  // ------------------------------------------------------------------ стены (склеены в отрезки) и стекло
  // внешний контур — панорамные окна в пол; внутренние стены — на всю высоту (в обзоре сверху сжимаются)
  const isExterior = (x, y) => x === BX0 || x === BX1 || y === BZ0 || y === BZ1;
  {
    const used = new Uint8Array(W * H);
    const glassMat = new THREE.MeshLambertMaterial({ color: "#a8dcf2", transparent: true, opacity: 0.22, depthWrite: false });
    const winMat = new THREE.MeshLambertMaterial({ color: "#bfe6ff", transparent: true, opacity: 0.12, depthWrite: false });
    const kind = (x, y) => isExterior(x, y) ? 2 : glassTile[idx(x, y)] ? 1 : 0;
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const i = idx(x, y);
      const ext = isExterior(x, y) && x >= BX0 && x <= BX1 && y >= BZ0 && y <= BZ1 && floor[i] !== "door";
      if ((solid[i] !== 1 && !ext) || used[i]) continue;
      const g = kind(x, y);
      const ok = (xx, yy) => xx < W && yy < H && !used[idx(xx, yy)] && (solid[idx(xx, yy)] === 1 || (isExterior(xx, yy) && xx <= BX1 && yy <= BZ1 && floor[idx(xx, yy)] !== "door")) && kind(xx, yy) === g;
      let len = 1; while (ok(x + len, y)) len++;
      let hx = len, hy = 1;
      if (len === 1) { while (ok(x, y + hy)) hy++; hx = 1; }
      for (let j = 0; j < hy; j++) for (let k = 0; k < hx; k++) used[idx(x + k, y + j)] = 1;
      const cx = x + hx / 2, cz = y + hy / 2;
      if (g === 2) {
        // окно в пол: стекло, импосты каждые 3 тайла, подоконная полоса
        box(scene, hx, CEIL, hy * 0.12 + (hy > 1 ? 0 : 0), "", cx, 0, cz, { material: winMat, cast: false });
        box(scene, hx + 0.05, 0.18, hy + 0.05, "#2a2d36", cx, 0, cz, { cast: false });
        box(scene, hx + 0.05, 0.2, hy + 0.05, "#2a2d36", cx, CEIL - 0.2, cz, { cast: false });
        const along = hx > 1 ? hx : hy;
        for (let k = 0; k <= along; k += 3) {
          const mx = hx > 1 ? x + k : cx, mz = hx > 1 ? cz : y + k;
          box(scene, hx > 1 ? 0.12 : 0.3, CEIL, hx > 1 ? 0.3 : 0.12, "#3a3e4a", mx, 0, mz, { cast: false });
        }
      } else if (g === 1) {
        wallMeshes.push(box(scene, hx, WALL_H, hy * 0.25, "", cx, 0, cz, { material: glassMat, cast: false }));
        wallMeshes.push(box(scene, hx, 0.1, hy * 0.35, "#6b7c8a", cx, 0, cz, { cast: false }));
        for (let k = 0; k <= (hx > 1 ? hx : hy); k += 4) wallMeshes.push(box(scene, hx > 1 ? 0.08 : 0.3, WALL_H, hx > 1 ? 0.3 : 0.08, "#8a9aa8", hx > 1 ? x + k : cx, 0, hx > 1 ? cz : y + k, { cast: false }));
      } else {
        wallMeshes.push(box(scene, hx, WALL_H, hy, "#e8e3da", cx, 0, cz));
        wallMeshes.push(box(scene, hx + 0.02, 0.25, hy + 0.02, "#4a4458", cx, 0, cz, { cast: false }));   // плинтус
      }
    }
    // потолок с панелями света
    const ceil = new THREE.Mesh(new THREE.PlaneGeometry(FW, FD), new THREE.MeshLambertMaterial({ color: "#f2efe8", side: THREE.DoubleSide }));
    ceil.rotation.x = Math.PI / 2; ceil.position.set(FCX, CEIL, FCZ); scene.add(ceil); roofParts.push(ceil);
    const panelMat = new THREE.MeshBasicMaterial({ color: "#fffbe8" });
    const panels = new THREE.InstancedMesh(new THREE.BoxGeometry(2.4, 0.05, 1.2), panelMat, 200);
    let n = 0; const m4 = new THREE.Matrix4();
    for (let z = BZ0 + 4; z < BZ1 - 1 && n < 200; z += 6) for (let x = BX0 + 4; x < BX1 - 1 && n < 200; x += 6) {
      if (solid[idx(x, z)] === 1) continue;
      m4.makeTranslation(x + 0.5, CEIL - 0.03, z + 0.5); panels.setMatrixAt(n++, m4);
    }
    panels.count = n; scene.add(panels); roofParts.push(panels);
    // несколько настоящих ламп (без теней) — тёплый офисный свет
    for (const [lx, lz] of [[14, 8], [41, 8], [77, 8], [12, 31], [38, 37], [62, 31], [85, 37], [62, 44], [48, 20]]) {
      const l = new THREE.PointLight("#fff1dc", 0.55, 26, 1.6); l.position.set(lx, CEIL - 0.4, lz); scene.add(l);
    }
    // лифт в лобби на месте бывшего входа с улицы
    box(scene, 4.2, CEIL - 0.5, 0.3, "#9aa3ad", 62, 0, BZ1 + 0.35, { mat: { emissive: "#1a1c20" } });
    box(scene, 0.06, CEIL - 0.5, 0.32, "#5d6570", 62, 0, BZ1 + 0.33);
    box(scene, 4.4, 0.5, 1, "#e8e3da", 62, CEIL - 0.5, BZ1 + 0.5, { cast: false });
    plane(scene, 2.2, 0.4, textTex("▲ ЛИФТ ▼", "#ffcc4d", "#1d1b26", 220, 40), 62, CEIL - 0.25, BZ1 - 0.02, Math.PI, "#ffcc4d");
  }

  // ------------------------------------------------------------------ мебель
  const screensByOwner = {}, leds = [], arcadeScreens = [], steam = [];
  const ledOn = mat("#69f0ae", { emissive: "#2bd47a" }), ledRed = mat("#ff5252", { emissive: "#c62828" }), ledOff = mat("#1b3a2b");
  const screenMat = col => new THREE.MeshLambertMaterial({ color: "#111", emissive: col, emissiveIntensity: 0.55 });
  const OWN_SCREEN = { dev: "#2f8fd8", art: "#c45ba8", eng: "#6b54b8", audio: "#4f8f4a", qa: "#3ea860", prod: "#d0884a", dir: "#3f6fb0" };

  function monitor(parent, x, y, z, w, col, owner) {
    box(parent, w, 0.6, 0.06, "#15161b", x, y, z);
    const s = new THREE.Mesh(new THREE.PlaneGeometry(w - 0.08, 0.5), screenMat(col)); s.position.set(x, y + 0.3, z + 0.035); parent.add(s);
    box(parent, 0.08, 0.25, 0.08, "#2a2a2a", x, y - 0.25, z - 0.02);
    if (owner) (screensByOwner[owner] ||= []).push(s);
    return s;
  }
  function chair(parent, x, z, color) {
    box(parent, 0.75, 0.12, 0.75, color, x, 0.55, z); box(parent, 0.75, 0.75, 0.12, color, x, 0.67, z + 0.38);
    cyl(parent, 0.06, 0.55, "#333", x, 0, z, 6);
  }

  for (const f of furniture) {
    if (f.y >= BZ1 || (f.t === "poster" && f.y === BZ0)) continue;   // улица, парковка; плакаты на панорамных окнах
    const x0 = f.x, z0 = f.y, cx = f.x + f.w / 2, cz = f.y + f.h / 2;
    switch (f.t) {
      case "desk": {
        const top = { dev: "#3b3f4c", art: "#d9c3a0", eng: "#5a5368", audio: "#7a5a3a", qa: "#e2e2e2", prod: "#9a6b4f" }[f.style] || "#a07a55";
        box(scene, f.w - 0.1, 0.1, 0.95, top, cx, 0.95, z0 + 0.5);
        box(scene, f.w - 0.2, 0.75, 0.08, shade(top, -0.3), cx, 0.2, z0 + 0.08);
        for (const dx of [-f.w / 2 + 0.15, f.w / 2 - 0.15]) box(scene, 0.08, 0.95, 0.85, shade(top, -0.3), cx + dx, 0, z0 + 0.5);
        const col = OWN_SCREEN[f.style] || "#3f6fb0";
        if (f.style === "dev") for (const dx of [-0.95, 0, 0.95]) monitor(scene, cx + dx, 1.3, z0 + 0.25, 0.85, col, f.owner);
        else monitor(scene, cx - 0.2, 1.3, z0 + 0.25, 1.0, col, f.owner);
        if (f.style === "art") { box(scene, 0.7, 0.03, 0.5, "#333", cx + 0.9, 1.05, z0 + 0.55); box(scene, 0.05, 0.03, 0.3, "#e94e77", cx + 0.9, 1.08, z0 + 0.55); }
        if (f.style === "audio") box(scene, 0.8, 0.12, 0.45, "#222", cx + 0.9, 1.05, z0 + 0.55);
        box(scene, 0.9, 0.04, 0.3, "#d9d9d9", cx - 0.1, 1.05, z0 + 0.72);                           // клавиатура
        const a = byId[f.owner];
        if (a) plane(scene, 1.4, 0.26, textTex(a.title, "#1d1b26", "#e8d9a8", 320, 56, "bold 24px Segoe UI"), cx, 0.72, z0 + 1.0, 0);
        const s = seats[f.owner]; if (s) chair(scene, s.x + 0.5, s.y + 0.5, s.room === "dir" ? "#3e2318" : "#3a3a48");
        break;
      }
      case "bigdesk": {
        box(scene, f.w, 1.05, f.h - 0.3, "#4a2c1d", cx, 0, cz - 0.1); box(scene, f.w + 0.1, 0.08, f.h - 0.1, "#6b4430", cx, 1.05, cz - 0.1);
        monitor(scene, cx, 1.45, z0 + 0.4, 1.3, "#3f6fb0", f.owner);
        box(scene, 0.45, 0.35, 0.45, "#c9a227", cx + 1.7, 1.13, z0 + 0.7, { mat: { emissive: "#5a4710" } });
        const a = byId[f.owner];
        if (a) plane(scene, 2.2, 0.32, textTex(a.title, "#1d1b26", "#c9a227", 360, 56, "bold 24px Segoe UI"), cx, 0.7, z0 + f.h - 0.44, 0);
        const s = seats[f.owner]; if (s) { box(scene, 0.95, 0.2, 0.9, "#3e2318", s.x + 0.5, 0.5, s.y + 0.5); box(scene, 0.95, 1.2, 0.18, "#3e2318", s.x + 0.5, 0.6, s.y + 0.95); }
        break;
      }
      case "board": {
        const tex = texOf(f, 0);
        box(scene, f.w, 1.2, 0.08, "#8a8578", cx, 0.35, z0 + 0.3);
        plane(scene, f.w - 0.2, 1.05, tex, cx, 0.95, z0 + 0.35);
        if (f.kind === "kanban") { box(scene, 0.08, 0.45, 0.08, "#555", cx - f.w / 2 + 0.3, 0, z0 + 0.3); box(scene, 0.08, 0.45, 0.08, "#555", cx + f.w / 2 - 0.3, 0, z0 + 0.3); }
        break;
      }
      case "poster": plane(scene, f.w - 0.2, 1.0, texOf(f, 0), cx, 1.9, z0 + 1.03); break;
      case "plant":
        cyl(scene, 0.32, 0.5, "#a0522d", cx, 0, cz, 8);
        blob(scene, 0.55, "#2e7d32", cx, 1.0, cz); blob(scene, 0.4, "#43a047", cx + 0.15, 1.45, cz - 0.1); break;
      case "easel":
        for (const dx of [-0.4, 0.4]) box(scene, 0.07, 2.0, 0.07, "#8d6e63", cx + dx, 0, cz + 0.2);
        plane(scene, 1.4, 1.0, textTex("", "#fff", "#ff7aa2", 64, 48), cx, 1.4, cz + 0.27);
        box(scene, 1.4, 0.45, 0.03, "#5b3f8c", cx, 0.9, cz + 0.29); blob(scene, 0.16, "#ffd166", cx, 1.45, cz + 0.32); break;
      case "model_bank":
        box(scene, f.w, 0.9, 0.9, "#d9c3a0", cx, 0, z0 + 0.5);
        box(scene, 1.9, 0.12, 0.8, "#f2efe6", cx, 0.9, z0 + 0.5);
        for (let i = 0; i < 5; i++) cyl(scene, 0.06, 0.5, "#f2efe6", cx - 0.8 + i * 0.4, 1.02, z0 + 0.8, 6);
        box(scene, 1.9, 0.5, 0.5, "#e0d8c8", cx, 1.02, z0 + 0.4); box(scene, 2.0, 0.12, 0.85, "#c9a227", cx, 1.52, z0 + 0.5); break;
      case "model_gorilla":
        box(scene, f.w, 0.9, 0.9, "#d9c3a0", cx, 0, z0 + 0.5);
        box(scene, 0.7, 0.6, 0.45, "#3a3a44", cx, 0.9, z0 + 0.5); box(scene, 0.5, 0.45, 0.45, "#3a3a44", cx, 1.5, z0 + 0.5);
        box(scene, 0.36, 0.25, 0.05, "#c9a27e", cx, 1.55, z0 + 0.74); break;
      case "rack": {
        box(scene, f.w - 0.2, 2.6, f.h - 0.3, "#1c1f27", cx, 0, cz);
        for (let j = 0; j < 7; j++) for (let i = 0; i < 3; i++) {
          const l = box(scene, 0.12, 0.06, 0.04, "", cx - 0.45 + i * 0.45, 0.4 + j * 0.3, z0 + f.h - 0.13, { material: ledOff, cast: false });
          l.userData.phase = f.x * 1.3 + j * 7; l.userData.speed = 3 + i + j; l.userData.red = i === 2; leds.push(l);
        }
        break;
      }
      case "booth": {
        const fc = "#2f2f38";
        box(scene, f.w, 2.2, 0.3, fc, cx, 0, z0 + 0.15); box(scene, f.w, 2.2, 0.3, fc, cx, 0, z0 + f.h - 0.15);
        box(scene, 0.3, 2.2, f.h, fc, x0 + f.w - 0.15, 0, cz);
        box(scene, 0.3, 2.2, 3, fc, x0 + 0.15, 0, z0 + 1.5 + 0.5); box(scene, 0.3, 2.2, 2, fc, x0 + 0.15, 0, z0 + 7);
        box(scene, 2.2, 0.45, 0.12, "#ff3b3b", cx + 1.5, 2.25, z0 + 0.2, { mat: { emissive: "#a01010" } });
        plane(scene, 2.0, 0.4, textTex("ON AIR", "#fff", "#e02020", 200, 40), cx + 1.5, 2.48, z0 + 0.27, 0, "#ffffff");
        break;
      }
      case "mixer": box(scene, f.w, 0.95, 0.8, "#222", cx, 0, cz); for (let i = 0; i < 8; i++) box(scene, 0.08, 0.06, 0.25, "#ffb74d", x0 + 0.4 + i * 0.3, 0.95, cz + (i % 3) * 0.08, { cast: false }); break;
      case "mic": cyl(scene, 0.04, 1.5, "#888", cx, 0, cz, 6); cyl(scene, 0.25, 0.04, "#555", cx, 0, cz, 10); blob(scene, 0.13, "#555", cx, 1.6, cz); break;
      case "tv": box(scene, f.w, 1.6, 0.15, "#111", cx, 0.6, z0 + 0.5); { const s = new THREE.Mesh(new THREE.PlaneGeometry(f.w - 0.2, 1.4), screenMat("#1b6fb0")); s.position.set(cx, 1.4, z0 + 0.58); scene.add(s); arcadeScreens.push(s); } break;
      case "reception":
        box(scene, f.w, 1.15, f.h, "#2bd47a", cx, 0, cz); box(scene, f.w + 0.1, 0.08, f.h + 0.1, "#7ff0a8", cx, 1.15, cz);
        monitor(scene, cx - 1.5, 1.45, z0 + 0.6, 1.0, "#3f6fb0");
        plane(scene, 4, 0.6, textTex("GRABZ STUDIO", "#0d3d22", "#7ff0a8", 400, 60), cx, 0.65, z0 + f.h + 0.01); break;
      case "counter": box(scene, f.w, 1.0, f.h + 0.2, "#9e9e9e", cx, 0, cz); box(scene, f.w, 0.08, f.h + 0.3, "#e6e6e6", cx, 1.0, cz); break;
      case "coffee":
        box(scene, 1.3, 1.1, 0.8, "#3a3a3a", cx, 1.08, cz); box(scene, 1.0, 0.25, 0.7, "#5d4037", cx, 2.18, cz);
        cyl(scene, 0.12, 0.25, "#fff", cx, 1.08, cz + 0.35, 8);
        for (let i = 0; i < 4; i++) { const s = blob(scene, 0.09, "#ffffff", cx, 1.5, cz + 0.4); s.material = new THREE.MeshBasicMaterial({ color: "#fff", transparent: true, opacity: 0.6 }); s.castShadow = false; s.userData.k = i / 4; s.userData.base = new THREE.Vector3(cx, 1.4, cz + 0.4); steam.push(s); }
        break;
      case "fridge": box(scene, f.w - 0.1, 2.6, f.h - 0.2, "#dfe6ea", cx, 0, cz); box(scene, 0.06, 0.8, 0.06, "#9aa5ab", cx + 0.6, 1.2, z0 + f.h - 0.08); box(scene, f.w - 0.1, 0.04, 0.02, "#9aa5ab", cx, 1.7, z0 + f.h - 0.09); break;
      case "cooler": box(scene, 0.6, 1.1, 0.6, "#e0e0e0", cx, 0, cz); { const b = cyl(scene, 0.25, 0.55, "#64b5f6", cx, 1.1, cz, 10); b.material = new THREE.MeshLambertMaterial({ color: "#64b5f6", transparent: true, opacity: 0.75 }); } break;
      case "arcade": {
        box(scene, f.w - 0.4, 2.4, f.h - 0.4, "#5e35b1", cx, 0, cz);
        box(scene, f.w - 0.3, 0.12, 0.6, "#311b92", cx, 1.1, z0 + f.h - 0.3);
        const s = new THREE.Mesh(new THREE.PlaneGeometry(f.w - 0.7, 0.8), screenMat("#ff00aa")); s.position.set(cx, 1.75, z0 + f.h - 0.19); s.rotation.x = -0.2; scene.add(s); arcadeScreens.push(s);
        blob(scene, 0.07, "#ff5252", cx - 0.3, 1.25, z0 + f.h - 0.2); blob(scene, 0.07, "#ffd740", cx + 0.1, 1.25, z0 + f.h - 0.2);
        break;
      }
      case "foos":
        box(scene, f.w, 0.85, f.h, "#5d4037", cx, 0, cz); box(scene, f.w - 0.3, 0.05, f.h - 0.3, "#2e7d32", cx, 0.85, cz, { cast: false });
        for (let i = 0; i < 6; i++) { const r = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, f.h + 0.8, 6), mat("#bbb")); r.rotation.x = Math.PI / 2; r.position.set(x0 + 0.5 + i * 0.6, 1.05, cz); scene.add(r); box(scene, 0.12, 0.3, 0.12, i < 3 ? "#e53935" : "#1e88e5", x0 + 0.5 + i * 0.6, 0.88, cz + ((i % 2) - 0.5) * 0.6); }
        break;
      case "table": cyl(scene, 0.9, 0.08, "#c8a27a", cx, 0.85, cz, 14); cyl(scene, 0.08, 0.85, "#6d4c41", cx, 0, cz, 6); cyl(scene, 0.1, 0.18, "#fff", cx + 0.2, 0.93, cz, 8); break;
      case "sofa": {
        const c = f.color;
        box(scene, f.w - 0.1, 0.5, f.h - 0.1, shade(c, -0.25), cx, 0, cz);
        box(scene, f.w - 0.3, 0.2, f.h - 0.4, c, cx, 0.5, cz + 0.1);
        box(scene, f.w - 0.1, 0.8, 0.35, shade(c, -0.1), cx, 0.5, z0 + 0.18);
        box(scene, 0.3, 0.45, f.h - 0.1, shade(c, -0.15), x0 + 0.15, 0.5, cz); box(scene, 0.3, 0.45, f.h - 0.1, shade(c, -0.15), x0 + f.w - 0.15, 0.5, cz);
        break;
      }
      case "van": {
        const g = new THREE.Group(); scene.add(g);
        box(g, f.w - 0.4, 2.4, f.h - 0.6, "#2bd47a", cx, 0.45, cz);
        box(g, f.w - 0.3, 0.15, f.h - 0.5, "#7ff0a8", cx, 2.85, cz);
        box(g, 0.1, 1.0, f.h - 1.2, "#1c3d4f", x0 + f.w - 0.2, 1.6, cz);
        for (const wx of [x0 + 1.4, x0 + f.w - 1.6]) for (const wz of [z0 + 0.3, z0 + f.h - 0.3]) { const w = new THREE.Mesh(new THREE.CylinderGeometry(0.55, 0.55, 0.35, 12), mat("#111")); w.rotation.x = Math.PI / 2; w.position.set(wx, 0.55, wz); w.castShadow = true; g.add(w); }
        plane(g, 4.5, 1.0, textTex("GRABZ", "#0d3d22", "#2bd47a", 300, 64, "900 46px Segoe UI"), cx - 0.4, 1.7, z0 + f.h - 0.29, 0);
        break;
      }
      case "car":
        box(scene, f.w - 0.3, 0.9, f.h - 0.5, f.color, cx, 0.35, cz); box(scene, f.w - 2, 0.7, f.h - 0.8, shade(f.color, 0.25), cx - 0.2, 1.25, cz);
        box(scene, f.w - 2.1, 0.5, f.h - 0.75, "#1c3d4f", cx - 0.2, 1.3, cz, { cast: false });
        for (const wx of [x0 + 1, x0 + f.w - 1]) for (const wz of [z0 + 0.3, z0 + f.h - 0.3]) { const w = new THREE.Mesh(new THREE.CylinderGeometry(0.4, 0.4, 0.3, 10), mat("#111")); w.rotation.x = Math.PI / 2; w.position.set(wx, 0.4, wz); scene.add(w); }
        break;
      case "tree": cyl(scene, 0.25, 1.6, "#6d4c41", cx, 0, cz, 7); blob(scene, 1.5, "#2e7d32", cx, 2.4, cz); blob(scene, 1.0, "#43a047", cx + 0.4, 3.3, cz - 0.2); break;
    }
  }


  // ------------------------------------------------------------------ агенты: блочные 3D-человечки
  const meshes = new Map(), pickables = [];
  function outfitOf(a) {
    switch (a.dept) {
      case "dir": return a.id === "producer" ? ["#5a6b7d", "#3e4b58"] : ["#2b2d42", "#1a1b29"];
      case "dev": return [a.darkHood, shade(a.darkHood, 0.15)];
      case "eng": return a.id.startsWith("godot") ? ["#478cbf", "#2f6a96"] : ["#22242b", "#4a4d57"];
      case "audio": return a.accessory > 0.5 ? ["#8e3b2f", "#5e271f"] : ["#3b5f8e", "#28405f"];
      case "qa": return ["#3d8b4f", "#2a6136"];
      case "prod": return ["#e9e3d6", "#b9b2a4"];
      default: return [a.bright, shade(a.bright, -0.25)];
    }
  }
  function buildAgent(a) {
    const g = new THREE.Group(); const [shirt, trim] = outfitOf(a);
    const body = new THREE.Group(); g.add(body);
    const limb = (w, h, d, col, px, py) => { const pivot = new THREE.Group(); pivot.position.set(px, py, 0); box(pivot, w, h, d, col, 0, -h, 0); body.add(pivot); return pivot; };
    const legL = limb(0.26, 0.75, 0.28, a.pants, -0.17, 0.78), legR = limb(0.26, 0.75, 0.28, a.pants, 0.17, 0.78);
    box(legL, 0.28, 0.1, 0.36, "#222", 0, -0.75, 0.04); box(legR, 0.28, 0.1, 0.36, "#222", 0, -0.75, 0.04);
    box(body, 0.74, 0.8, 0.42, shirt, 0, 0.75, 0); box(body, 0.76, 0.08, 0.44, trim, 0, 0.75, 0);
    const armL = limb(0.2, 0.72, 0.22, shirt, -0.48, 1.52), armR = limb(0.2, 0.72, 0.22, shirt, 0.48, 1.52);
    box(armL, 0.18, 0.16, 0.2, a.skin, 0, -0.86, 0); box(armR, 0.18, 0.16, 0.2, a.skin, 0, -0.86, 0);
    const head = new THREE.Group(); head.position.set(0, 1.58, 0); body.add(head);
    box(head, 0.62, 0.6, 0.58, a.skin, 0, 0, 0);
    // волосы
    const hs = a.hairStyle, hc = a.hair;
    if (hs !== 5) box(head, 0.66, hs === 2 ? 0.28 : 0.18, 0.62, hc, 0, 0.5, -0.01);
    box(head, 0.64, 0.45, 0.12, hc, 0, 0.15, -0.28);
    if (hs === 1) { box(head, 0.1, 0.55, 0.5, hc, -0.34, 0.05, -0.04); box(head, 0.1, 0.55, 0.5, hc, 0.34, 0.05, -0.04); }
    if (hs === 2) box(head, 0.3, 0.25, 0.3, hc, 0, 0.72, -0.15);
    if (hs === 3) box(head, 0.12, 0.25, 0.5, hc, 0, 0.62, 0);
    // лицо
    const eyes = new THREE.Group(); head.add(eyes);
    box(eyes, 0.09, 0.1, 0.02, "#1b1b1b", -0.14, 0.27, 0.3, { cast: false }); box(eyes, 0.09, 0.1, 0.02, "#1b1b1b", 0.14, 0.27, 0.3, { cast: false });
    box(head, 0.18, 0.04, 0.02, shade(a.skin, -0.35), 0, 0.1, 0.3, { cast: false });
    // отдел: одежда и аксессуары
    if (a.dept === "dir") { box(body, 0.16, 0.6, 0.02, "#f2efe6", 0, 0.95, 0.22, { cast: false }); box(body, 0.07, 0.5, 0.03, a.id === "creative-director" ? "#c9a227" : "#b3261e", 0, 0.98, 0.235, { cast: false }); }
    if (a.id === "technical-director") { box(body, 0.2, 0.8, 0.44, "#3d4a5c", -0.27, 0.75, 0); box(body, 0.2, 0.8, 0.44, "#3d4a5c", 0.27, 0.75, 0); }
    if (a.id === "producer") { box(armR, 0.4, 0.55, 0.05, "#222", 0.08, -0.95, 0.15); box(armR, 0.32, 0.45, 0.02, "#4fc3f7", 0.08, -0.9, 0.18, { cast: false, mat: { emissive: "#1d6fa0" } }); }
    if (a.dept === "art" && a.accessory < 0.5) box(body, 0.8, 0.16, 0.48, a.accessory < 0.25 ? "#ffe14d" : "#4fc3f7", 0, 1.42, 0);
    if (a.dept === "art" && a.accessory >= 0.5 && a.accessory < 0.8) { const b = cyl(head, 0.36, 0.1, "#c62828", 0.06, 0.58, 0, 12); b.rotation.z = 0.25; }
    if (a.dept === "dev" && a.accessory > 0.3) { box(head, 0.5, 0.04, 0.02, "#111", 0, 0.27, 0.31, { cast: false }); box(head, 0.16, 0.14, 0.02, "#111", -0.14, 0.22, 0.305, { cast: false, mat: { transparent: true, opacity: 0.5 } }); box(head, 0.16, 0.14, 0.02, "#111", 0.14, 0.22, 0.305, { cast: false, mat: { transparent: true, opacity: 0.5 } }); }
    if (a.dept === "dev" || (a.dept === "eng" && a.accessory > 0.5) || a.dept === "audio") {
      const big = a.dept === "audio";
      box(head, 0.74, 0.08, 0.12, "#222", 0, 0.62, 0);
      box(head, 0.1, big ? 0.36 : 0.26, big ? 0.36 : 0.26, "#151515", -0.36, 0.12, 0); box(head, 0.1, big ? 0.36 : 0.26, big ? 0.36 : 0.26, "#151515", 0.36, 0.12, 0);
      if (big) { box(head, 0.02, 0.2, 0.2, "#ffb74d", -0.42, 0.2, 0, { cast: false }); box(head, 0.02, 0.2, 0.2, "#ffb74d", 0.42, 0.2, 0, { cast: false }); }
    }
    if (a.dept === "qa") { box(body, 0.06, 0.8, 0.44, "#ffd54f", -0.2, 0.75, 0.01); box(body, 0.06, 0.8, 0.44, "#ffd54f", 0.2, 0.75, 0.01); }
    if (a.dept === "prod") { box(body, 0.14, 0.18, 0.02, "#2bd47a", 0, 1.05, 0.23, { cast: false }); }
    // VR-шлем (у QA всегда, у остальных — когда тестируют в лаборатории)
    const vr = new THREE.Group(); head.add(vr);
    box(vr, 0.66, 0.26, 0.2, "#151515", 0, 0.16, 0.3); box(vr, 0.5, 0.06, 0.02, "#4fc3f7", 0, 0.26, 0.405, { cast: false, mat: { emissive: "#2a8fc0" } });
    box(vr, 0.68, 0.08, 0.66, "#151515", 0, 0.25, 0);
    vr.visible = a.dept === "qa";
    // кружка кофе
    const cup = cyl(armR, 0.09, 0.16, "#ffffff", 0, -1.0, 0.12, 8); cup.visible = false;
    // кольцо под ногами: занят — цвет отдела, наведение — белое
    const ring = new THREE.Mesh(new THREE.RingGeometry(0.45, 0.6, 24), new THREE.MeshBasicMaterial({ color: DEPARTMENTS[a.dept].color, transparent: true, opacity: 0.9, side: THREE.DoubleSide }));
    ring.rotation.x = -Math.PI / 2; ring.position.y = 0.03; g.add(ring);
    g.traverse(o => { if (o.isMesh) { o.userData.agent = a; pickables.push(o); } });
    g.scale.setScalar(1.05);
    scene.add(g);
    const parts = { g, body, legL, legR, armL, armR, head, eyes, vr, cup, ring, yaw: 0 };
    meshes.set(a, parts);
    return parts;
  }
  for (const a of agents) buildAgent(a);

  const YAW = { down: 0, up: Math.PI, left: -Math.PI / 2, right: Math.PI / 2 };
  function animateAgent(a, p, t, dt) {
    p.g.position.set(a.x / T, 0, a.y / T);
    const target = YAW[a.facing] ?? 0;
    let d = target - p.yaw; while (d > Math.PI) d -= Math.PI * 2; while (d < -Math.PI) d += Math.PI * 2;
    p.yaw += d * Math.min(1, dt * 10); p.g.rotation.y = p.yaw;
    const moving = a.path.length > 0;
    const sitting = ((a.doing === "work" || a.doing === "desk") && a.atSeat) || a.doing === "sofa";
    const typing = a.doing === "work";
    const vr = a.doing === "vr";
    const sw = moving ? Math.sin(a.walkT) * 0.7 : 0;
    p.body.position.y = sitting ? -0.22 : moving ? Math.abs(Math.sin(a.walkT)) * 0.06 : 0;
    p.legL.rotation.x = sitting ? -1.45 : sw; p.legR.rotation.x = sitting ? -1.45 : -sw;
    if (vr) { p.armL.rotation.x = -2.6 + Math.sin(t * 9) * 0.5; p.armR.rotation.x = -2.6 + Math.sin(t * 9 + 2) * 0.5; p.armL.rotation.z = -0.3; p.armR.rotation.z = 0.3; }
    else if (typing) { p.armL.rotation.x = -1.25 + Math.sin(t * 24) * 0.08; p.armR.rotation.x = -1.25 + Math.sin(t * 24 + 1.5) * 0.08; p.armL.rotation.z = p.armR.rotation.z = 0; }
    else if (a.doing === "coffee") { p.armL.rotation.x = 0; p.armR.rotation.x = -1.3 + Math.sin(t * 1.5) * 0.15; p.armL.rotation.z = p.armR.rotation.z = 0; }
    else if (a.doing === "talk" || a.doing === "inspect" || a.doing === "chat") { p.armL.rotation.x = Math.sin(t * 4) * 0.25; p.armR.rotation.x = -0.6 + Math.sin(t * 5) * 0.35; p.armL.rotation.z = p.armR.rotation.z = 0; }
    else { p.armL.rotation.x = -sw * 0.8; p.armR.rotation.x = sw * 0.8; p.armL.rotation.z = p.armR.rotation.z = 0; }
    p.head.rotation.x = a.nod > t ? Math.sin((a.nod - t) * 14) * 0.3 : typing ? 0.12 : 0;
    p.head.rotation.y = a.doing === "stand" || a.doing === "chat" ? Math.sin(t * 0.7 + a.x) * 0.4 : 0;
    p.eyes.scale.y = t > a.blinkAt && t < a.blinkAt + 0.12 ? 0.15 : 1;
    if (t > a.blinkAt + 0.12) a.blinkAt = t + rand(2, 6);
    p.vr.visible = a.dept === "qa" || vr;
    p.cup.visible = a.doing === "coffee";
    const hovered = a === hover || a === cam.follow;
    p.ring.visible = a.busy || hovered;
    p.ring.material.color.set(hovered && !a.busy ? "#ffffff" : DEPARTMENTS[a.dept].color);
    p.ring.material.opacity = a.busy ? 0.55 + 0.35 * Math.sin(t * 5) : 0.9;
  }


  // ------------------------------------------------------------------ живые люди (CC0 Quaternius «Animated Human»)
  // Пока модель грузится (или если не загрузилась) — показываются блочные человечки выше.
  const humans = new Map(), humanPick = [];
  const POSE = {
    sitDrop: 0.5,                                           // насколько опускается тело на стул
    upLeg: [1, 0, 0, -1.45], leg: [1, 0, 0, 1.55],          // [ось x,y,z, угол] — бедро вперёд, колено вниз
    // у правой руки оси костей зеркальны — знак противоположный (подобрано по снимкам)
    typeArmL: [1, 0, 0, -1.0], typeArmR: [1, 0, 0, 1.0],    // руки вперёд к клавиатуре
    typeForeL: [1, 0, 0, -0.9], typeForeR: [1, 0, 0, 0.9],
    vrArmL: [1, 0, 0, -2.6], vrArmR: [1, 0, 0, 2.6],
  };
  const _q = new THREE.Quaternion(), _ax = new THREE.Vector3();
  function rot(bone, cfg, mul = 1) { if (!bone) return; _ax.set(cfg[0], cfg[1], cfg[2]).normalize(); _q.setFromAxisAngle(_ax, cfg[3] * mul); bone.quaternion.multiply(_q); }

  function paletteTex(a) {
    const [shirt] = outfitOf(a);
    let pants = a.pants;
    if (a.dept === "dir") pants = a.id === "producer" ? "#3e4b58" : "#1f2030";
    if (a.dept === "prod") pants = "#2c3e66";
    const c = document.createElement("canvas"); c.width = c.height = 32; const g = c.getContext("2d");
    const band = (y0, y1, col) => { g.fillStyle = col; g.fillRect(0, y0, 32, y1 - y0 + 1); };
    band(0, 5, a.skin); band(6, 10, "#2a2522"); band(11, 16, a.hair); band(17, 22, shirt); band(23, 31, pants);
    const t = new THREE.CanvasTexture(c); t.flipY = false; t.magFilter = THREE.NearestFilter; t.minFilter = THREE.NearestFilter; return t;
  }

  function buildAccessories(a, root, hs) {
    // всё в пространстве root; каждый кадр группа ставится на кость головы/руки
    const head = new THREE.Group(), hand = new THREE.Group(); root.add(head, hand);
    const s = hs;                                            // высота головы (ед.)
    const B = (parent, w, h, d, col, x, y, z, opts) => box(parent, w * s, h * s, d * s, col, x * s, y * s, z * s, opts);
    if (a.dept === "dev" || a.dept === "audio" || (a.dept === "eng" && a.accessory > 0.5)) {
      const big = a.dept === "audio";
      B(head, 1.05, 0.1, 0.16, "#202020", 0, 0.95, 0);
      B(head, 0.14, big ? 0.42 : 0.32, big ? 0.42 : 0.32, "#151515", -0.52, 0.32, 0); B(head, 0.14, big ? 0.42 : 0.32, big ? 0.42 : 0.32, "#151515", 0.52, 0.32, 0);
      if (big) { B(head, 0.03, 0.24, 0.24, "#ffb74d", -0.6, 0.4, 0, { cast: false }); B(head, 0.03, 0.24, 0.24, "#ffb74d", 0.6, 0.4, 0, { cast: false }); }
    }
    if (a.dept === "dev" && a.accessory > 0.3) { B(head, 0.62, 0.05, 0.04, "#111", 0, 0.55, 0.48, { cast: false }); B(head, 0.22, 0.17, 0.03, "#111", -0.17, 0.48, 0.48, { cast: false, mat: { transparent: true, opacity: 0.45 } }); B(head, 0.22, 0.17, 0.03, "#111", 0.17, 0.48, 0.48, { cast: false, mat: { transparent: true, opacity: 0.45 } }); }
    if (a.dept === "art" && a.accessory >= 0.5 && a.accessory < 0.8) { const b = cyl(head, 0.5 * s, 0.14 * s, "#c62828", 0.06 * s, 0.9 * s, 0, 14); b.rotation.z = 0.25; }
    const vr = new THREE.Group(); head.add(vr);
    B(vr, 0.82, 0.32, 0.3, "#151515", 0, 0.35, 0.45); B(vr, 0.62, 0.07, 0.02, "#4fc3f7", 0, 0.48, 0.61, { cast: false, mat: { emissive: "#2a8fc0" } });
    B(vr, 0.86, 0.09, 0.9, "#151515", 0, 0.48, 0);
    vr.visible = a.dept === "qa";
    let tablet = null;
    if (a.id === "producer") { tablet = new THREE.Group(); hand.add(tablet); B(tablet, 0.55, 0.75, 0.05, "#222", 0, -0.35, 0.12); B(tablet, 0.45, 0.62, 0.02, "#4fc3f7", 0, -0.3, 0.15, { cast: false, mat: { emissive: "#1d6fa0" } }); }
    const cup = cyl(hand, 0.13 * s, 0.24 * s, "#ffffff", 0, -0.1 * s, 0.1 * s, 8); cup.visible = false;
    // обувь: каждый кадр ставится между костями стопы и носка
    const shoeCol = a.dept === "dir" ? "#1a1a1a" : a.dept === "art" ? pick(["#f5f5f5", "#ff6f3c", "#222"]) : a.dept === "qa" ? "#e0e0e0" : pick(["#2a2a2a", "#4e342e", "#f2f2f2", "#263238"]);
    const shoes = [0, 1].map(() => { const m = new THREE.Mesh(unit, mat(shoeCol)); m.scale.set(0.42 * s, 0.26 * s, 1.0 * s); m.castShadow = true; root.add(m); return m; });
    return { head, hand, vr, cup, tablet, shoes };
  }

  function setupHumans(gltf) {
    const base = gltf.scene;
    base.updateMatrixWorld(true);
    const clips = {}; for (const c of gltf.animations) clips[c.name] = c;
    const hb = {}; base.traverse(o => { if (o.isBone) hb[o.name] = o; });
    // рост — по костям (bbox скин-меша считается по позе привязки без масштаба арматуры и врёт)
    const pHead = new THREE.Vector3(), pTop = new THREE.Vector3(), pFoot = new THREE.Vector3();
    hb.Head.getWorldPosition(pHead); (hb.HeadTop_End || hb.Head).getWorldPosition(pTop);
    let footY = Infinity; for (const n of ["LeftToe_End", "RightToe_End", "LeftFoot", "RightFoot", "LeftToeBase"]) if (hb[n]) { hb[n].getWorldPosition(pFoot); footY = Math.min(footY, pFoot.y); }
    if (!isFinite(footY)) footY = 0;
    const hRaw = (pTop.y - footY) * 1.03;
    const k = HUMAN_H / hRaw;
    const bb = { min: { y: footY - hRaw * 0.015 } };
    const hs = Math.max(0.12, (pTop.y - pHead.y) * k);
    for (const a of agents) {
      const model = THREE.SkeletonUtils.clone(base);
      model.scale.multiplyScalar(k);
      model.position.y = -bb.min.y * k;
      const tex = paletteTex(a);
      model.traverse(o => { if (o.isMesh) { o.material = new THREE.MeshLambertMaterial({ map: tex, skinning: !!o.isSkinnedMesh }); o.castShadow = true; o.receiveShadow = true; o.frustumCulled = false; } });
      const root = new THREE.Group(); root.add(model); scene.add(root);
      const pick = new THREE.Mesh(unit, new THREE.MeshBasicMaterial({ visible: false }));
      pick.scale.set(0.8, HUMAN_H, 0.8); pick.position.y = HUMAN_H / 2; pick.userData.agent = a; root.add(pick); humanPick.push(pick);
      const mixer = new THREE.AnimationMixer(model);
      const act = n => { if (!clips[n]) return null; const x = mixer.clipAction(clips[n]); x.play(); x.setEffectiveWeight(0); return x; };
      const actions = { idle: act("Idle"), walk: act("Walk"), run: act("Run"), work: act("Working") };
      if (actions.idle) { actions.idle.setEffectiveWeight(1); actions.idle.time = Math.random() * 8; }
      const bones = {}; model.traverse(o => { if (o.isBone) bones[o.name] = o; });
      const old = meshes.get(a); old.g.visible = false; root.add(old.ring);
      humans.set(a, { root, model, mixer, actions, bones, acc: buildAccessories(a, root, hs), yaw: old.yaw, baseY: model.position.y, w: { idle: 1, walk: 0, run: 0, work: 0 } });
    }
  }

  const loader = new THREE.GLTFLoader();
  const ready = new Promise(res => loader.load("models/human.glb",
    gltf => { try { setupHumans(gltf); } catch (e) { console.error("Люди не собрались, остаются блочные", e); } res(); },
    undefined, e => { console.warn("models/human.glb не загрузился — блочные агенты", e); res(); }));

  const _v = new THREE.Vector3(), _v2 = new THREE.Vector3(), _qInv = new THREE.Quaternion();
  function animateHuman(a, h, t, dt) {
    h.root.position.set(a.x / T, 0, a.y / T);
    let target = YAW[a.facing] ?? 0;
    if (a.faceYawUntil > t) target = a.faceYaw;
    let d = target - h.yaw; while (d > Math.PI) d -= Math.PI * 2; while (d < -Math.PI) d += Math.PI * 2;
    h.yaw += d * Math.min(1, dt * 8); h.root.rotation.y = h.yaw;

    const moving = a.path.length > 0;
    const sitting = ((a.doing === "work" || a.doing === "desk") && a.atSeat) || a.doing === "sofa";
    const typing = a.doing === "work" && a.atSeat;
    const vr = a.doing === "vr";
    const want = { idle: 0, walk: 0, run: 0, work: 0 };
    if (moving) want[a.speed > 70 ? "run" : "walk"] = 1;
    else if (a.doing === "foos" || a.doing === "arcade") want.work = 1;
    else want.idle = 1;
    for (const n in want) {
      h.w[n] += (want[n] - h.w[n]) * Math.min(1, dt * 7);
      if (h.actions[n]) h.actions[n].setEffectiveWeight(h.w[n]);
    }
    if (h.actions.walk) h.actions.walk.timeScale = Math.max(0.6, a.speed / 42);
    h.mixer.update(dt);

    const b = h.bones;
    h.model.position.y = h.baseY - (sitting ? POSE.sitDrop : 0);
    if (sitting) { rot(b.LeftUpLeg, POSE.upLeg); rot(b.RightUpLeg, POSE.upLeg); rot(b.LeftLeg, POSE.leg); rot(b.RightLeg, POSE.leg); }
    if (typing) {
      const j = Math.sin(t * 22) * 0.06;
      rot(b.LeftArm, POSE.typeArmL); rot(b.RightArm, POSE.typeArmR);
      rot(b.LeftForeArm, POSE.typeForeL, 1 + j); rot(b.RightForeArm, POSE.typeForeR, 1 - j);
    } else if (vr) {
      rot(b.LeftArm, POSE.vrArmL, 1 + Math.sin(t * 9) * 0.2); rot(b.RightArm, POSE.vrArmR, 1 + Math.sin(t * 9 + 2) * 0.2);
    } else if (a.doing === "coffee") {
      rot(b.RightArm, POSE.typeArmR, 0.7); rot(b.RightForeArm, POSE.typeForeR, 1.4);
    } else if (a.doing === "talk" || a.doing === "inspect" || a.doing === "chat") {
      rot(b.RightArm, POSE.typeArmR, 0.4 + Math.sin(t * 4) * 0.25); rot(b.RightForeArm, POSE.typeForeR, 0.8);
    }
    if (a.nod > t && b.Head) rot(b.Head, [1, 0, 0, Math.sin((a.nod - t) * 14) * 0.35]);

    // аксессуары следуют за головой и рукой
    h.root.updateMatrixWorld(true);
    if (b.Head) { b.Head.getWorldPosition(_v); h.acc.head.position.copy(h.root.worldToLocal(_v)); }
    if (b.RightHand) { b.RightHand.getWorldPosition(_v2); h.acc.hand.position.copy(h.root.worldToLocal(_v2)); }
    for (let i = 0; i < 2; i++) {
      const foot = i ? b.RightFoot : b.LeftFoot, toe = i ? (b.RightToe_End || b.RightToeBase) : (b.LeftToe_End || b.LeftToeBase);
      if (!foot || !toe) continue;
      foot.getWorldPosition(_v); toe.getWorldPosition(_v2);
      const shoe = h.acc.shoes[i];
      shoe.position.copy(h.root.worldToLocal(_v.clone().lerp(_v2, 0.45)));
      shoe.position.y -= 0.03;
      shoe.updateMatrixWorld(); _v2.y = _v.y + (_v2.y - _v.y) * 0.3; shoe.lookAt(_v2);   // lookAt сам учитывает поворот родителя
    }
    h.acc.vr.visible = a.dept === "qa" || vr;
    h.acc.cup.visible = a.doing === "coffee";
    if (h.acc.tablet) h.acc.tablet.visible = !typing;

    const ring = meshes.get(a).ring, hovered = a === hover || a === cam.follow;
    ring.visible = a.busy || hovered;
    ring.material.color.set(hovered && !a.busy ? "#ffffff" : DEPARTMENTS[a.dept].color);
    ring.material.opacity = a.busy ? 0.55 + 0.35 * Math.sin(t * 5) : 0.9;
  }

  // ------------------------------------------------------------------ пакеты данных (под потолком)
  const packetMeshes = new Map();
  function updatePackets(t) {
    for (const [pk, m] of packetMeshes) if (!packets.includes(pk)) { scene.remove(m); packetMeshes.delete(pk); }
    for (const pk of packets) {
      let m = packetMeshes.get(pk);
      if (!m) {
        m = new THREE.Group();
        for (let i = 0; i < 6; i++) { const s = new THREE.Mesh(new THREE.SphereGeometry(0.16 - i * 0.02, 10, 8), new THREE.MeshBasicMaterial({ color: i ? pk.color : "#ffffff", transparent: true, opacity: 1 - i * 0.14 })); m.add(s); }
        m.add(new THREE.PointLight(pk.color, 1.0, 5));
        scene.add(m); packetMeshes.set(pk, m);
      }
      const k = Math.min(1, (t - pk.t0) / pk.dur);
      m.children.forEach((s, i) => {
        const kk = Math.max(0, k - i * 0.025);
        s.position.set((pk.a.x + (pk.b.x - pk.a.x) * kk) / T, 2.5 + Math.sin(kk * Math.PI) * 0.9, (pk.a.y + (pk.b.y - pk.a.y) * kk) / T);
      });
    }
  }

  // ------------------------------------------------------------------ режимы камеры
  let mode = "fps";
  const player = { x: 62.5, z: 48.3, yaw: 0, pitch: -0.05, bob: 0 };   // у лифта в лобби, лицом к ресепшену
  const keys = new Set();
  let locked = false;

  function setWallsLow(low) {
    for (const m of wallMeshes) {
      m.userData.h ??= m.scale.y; m.userData.y0 ??= m.position.y - m.scale.y / 2;
      const h = low ? Math.min(m.userData.h, 1.4) : m.userData.h;
      m.scale.y = h; m.position.y = m.userData.y0 + h / 2;
    }
    for (const r of roofParts) r.visible = !low;
  }
  function setMode(m) {
    mode = m;
    const fps = m === "fps";
    controls.enabled = !fps;
    camera.fov = fps ? 70 : 38; camera.updateProjectionMatrix();
    setWallsLow(!fps);
    document.body.classList.toggle("fps", fps);
    const btn = document.getElementById("viewBtn"); if (btn) btn.textContent = fps ? "🗺 Обзор сверху (Tab)" : "👤 От первого лица (Tab)";
    const hint = document.getElementById("hint");
    if (hint) hint.textContent = fps ? "Клик — управлять · WASD — ходить · Shift — бег · мышь — смотреть · ЛКМ по агенту — досье · E — поговорить · Tab — обзор · Esc — отпустить мышь"
      : "Колесо — масштаб · ЛКМ — вращать · ПКМ — сдвиг · клик по агенту — досье · Tab — от первого лица";
    if (fps) { cam.follow = null; } else { if (document.pointerLockElement === canvas) document.exitPointerLock(); resetCamera(); }
  }

  function resetCamera() {
    if (mode === "fps") return;
    const tanV = Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)), tanH = tanV * camera.aspect;
    const dist = Math.max(52 / tanH, 30 / tanV) * 0.72;
    controls.target.set(50, 0, 27);
    camera.position.set(50, dist * 0.74, 27 + dist * 0.67);
    cam.follow = null; controls.update();
  }
  function resize() {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    renderer.setSize(w, h, false); camera.aspect = w / Math.max(1, h); camera.updateProjectionMatrix();
  }

  // проходимость для игрока: стены, мебель, края этажа
  function blockedTile(x, z) {
    const tx = Math.floor(x), tz = Math.floor(z);
    if (tx <= BX0 || tx >= BX1 || tz <= BZ0 || tz >= BZ1) return true;
    return solid[idx(tx, tz)] !== 0;
  }
  const PR = 0.28;
  const blocked = (x, z) => blockedTile(x - PR, z - PR) || blockedTile(x + PR, z - PR) || blockedTile(x - PR, z + PR) || blockedTile(x + PR, z + PR);

  // прямая видимость по сетке (стекло прозрачно)
  function lineOfSight(x0, z0, x1, z1) {
    const n = Math.ceil(Math.hypot(x1 - x0, z1 - z0) * 2);
    for (let i = 1; i < n; i++) {
      const x = Math.floor(x0 + (x1 - x0) * i / n), z = Math.floor(z0 + (z1 - z0) * i / n);
      if (x < 0 || z < 0 || x >= W || z >= H) continue;
      const k = idx(x, z);
      if (solid[k] === 1 && !glassTile[k] && !(isExterior(x, z))) return false;
    }
    return true;
  }

  const ray = new THREE.Raycaster(), ndc = new THREE.Vector2();
  const pickList = () => (humans.size ? humanPick : pickables);
  function pickNdc(x, y) {
    ndc.set(x, y); ray.setFromCamera(ndc, camera);
    const hit = ray.intersectObjects(pickList(), false)[0];
    return hit ? hit.object.userData.agent : null;
  }
  function pickAt(ev) {
    const r = canvas.getBoundingClientRect();
    return pickNdc(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  }

  const PLAYER_LINES = ["Привет, шеф! Всё по плану.", "О, владелец студии! Как вам GRABZ VR?", "Хотите, покажу, что я сделал?", "Кофе будете? Машина на кухне.",
    "У нас тут аврал, но мы справимся!", "Гориллы уже почти готовы к ограблению.", "Заходите в VR-лабу, там весело."];
  function talkToNearest(t) {
    let best = null, bd = 4.2;
    const fx = -Math.sin(player.yaw), fz = -Math.cos(player.yaw);
    for (const a of agents) {
      const dx = a.x / T - player.x, dz = a.y / T - player.z, d = Math.hypot(dx, dz);
      if (d < bd && (dx * fx + dz * fz) / Math.max(0.01, d) > 0.35) { bd = d; best = a; }
    }
    if (!best) return;
    best.faceYaw = Math.atan2(player.x - best.x / T, player.z - best.y / T);
    best.faceYawUntil = t + 5;
    if (!best.busy && best.doing !== "work") { best.path = []; best.after = null; best.stay("talk", 5, () => { best.nextThink = now() + 1; }); }
    const line = best.busy ? `Привет! Сейчас делаю «${best.task}». Чуть позже поболтаем!` : pick(PLAYER_LINES);
    best.say(line, 4); best.nod = t + 1;
    addChat(best, `вам: ${line}`, "");
  }

  canvas.addEventListener("click", () => { if (mode === "fps" && !locked) canvas.requestPointerLock(); });
  document.addEventListener("pointerlockchange", () => { locked = document.pointerLockElement === canvas; document.body.classList.toggle("locked", locked); });
  document.addEventListener("mousemove", e => {
    if (!locked) return;
    player.yaw -= e.movementX * 0.0022;
    player.pitch = Math.max(-1.35, Math.min(1.35, player.pitch - e.movementY * 0.0022));
  });
  document.addEventListener("mousedown", e => {
    if (!locked || e.button !== 0) return;
    const a = pickNdc(0, 0);
    if (a) { document.exitPointerLock(); openDossier(a); }
  });
  window.addEventListener("keydown", e => {
    if (e.key === "Tab") { e.preventDefault(); setMode(mode === "fps" ? "overview" : "fps"); return; }
    if (document.getElementById("dossier") && !document.getElementById("dossier").classList.contains("hidden")) return;
    keys.add(e.code);
    if (e.code === "KeyE" && mode === "fps") talkToNearest(now());
  });
  window.addEventListener("keyup", e => keys.delete(e.code));
  window.addEventListener("blur", () => keys.clear());

  let down = null;
  canvas.addEventListener("pointerdown", e => { if (mode !== "fps") down = { x: e.clientX, y: e.clientY }; });
  canvas.addEventListener("pointermove", e => {
    if (mode === "fps") return;
    hover = pickAt(e); canvas.style.cursor = hover ? "pointer" : "grab";
    if (down && Math.hypot(e.clientX - down.x, e.clientY - down.y) > 5) cam.follow = null;
  });
  canvas.addEventListener("pointerup", e => {
    if (mode !== "fps" && down && Math.hypot(e.clientX - down.x, e.clientY - down.y) < 5) { const a = pickAt(e); if (a) openDossier(a); }
    down = null;
  });
  window.addEventListener("resize", resize);
  const vb = document.getElementById("viewBtn"); if (vb) vb.onclick = () => setMode(mode === "fps" ? "overview" : "fps");
  resize(); setMode("fps");

  function updatePlayer(dt, t) {
    let mx = 0, mz = 0;
    if (keys.has("KeyW") || keys.has("ArrowUp")) mz += 1;
    if (keys.has("KeyS") || keys.has("ArrowDown")) mz -= 1;
    if (keys.has("KeyD") || keys.has("ArrowRight")) mx += 1;
    if (keys.has("KeyA") || keys.has("ArrowLeft")) mx -= 1;
    const run = keys.has("ShiftLeft") || keys.has("ShiftRight");
    const len = Math.hypot(mx, mz);
    if (len > 0) {
      const sp = (run ? 7 : 3.6) * dt / len;
      const fx = -Math.sin(player.yaw), fz = -Math.cos(player.yaw), rx = Math.cos(player.yaw), rz = -Math.sin(player.yaw);
      const dx = (fx * mz + rx * mx) * sp, dz = (fz * mz + rz * mx) * sp;
      if (!blocked(player.x + dx, player.z)) player.x += dx;
      if (!blocked(player.x, player.z + dz)) player.z += dz;
      player.bob += dt * (run ? 14 : 9);
    }
    camera.position.set(player.x, EYE + (len > 0 ? Math.sin(player.bob) * 0.035 : 0), player.z);
    camera.rotation.set(player.pitch, player.yaw, 0, "YXZ");
    hover = locked ? pickNdc(0, 0) : null;
  }

  const v3 = new THREE.Vector3();
  return {
    ready,
    get fps() { return mode === "fps"; },
    setMode, resetCamera, POSE, humans, player,
    /** Экранные CSS-координаты точки карты (px карты) на высоте h px; null — за камерой. */
    project(wx, wy, h) {
      v3.set(wx / T, h * K_H, wy / T).project(camera);
      if (v3.z > 1 || v3.z < -1) return null;
      return [(v3.x + 1) / 2 * canvas.clientWidth, (1 - v3.y) / 2 * canvas.clientHeight];
    },
    /** В режиме от первого лица — только рядом и при прямой видимости. */
    canSee(wx, wy, maxDist = 25) {
      if (mode !== "fps") return true;
      const x = wx / T, z = wy / T;
      if (Math.hypot(x - player.x, z - player.z) > maxDist) return false;
      return lineOfSight(player.x, player.z, x, z);
    },
    zoomK() { return mode === "fps" ? 2.5 : 160 / camera.position.distanceTo(controls.target); },
    follow(a, dt) {
      if (mode === "fps") return;
      const tx = a.x / T, tz = a.y / T, k = Math.min(1, dt * 4);
      const dx = (tx - controls.target.x) * k, dz = (tz - controls.target.z) * k;
      controls.target.x += dx; controls.target.z += dz; camera.position.x += dx; camera.position.z += dz;
      if (camera.position.distanceTo(controls.target) > 30) camera.position.lerp(controls.target, Math.min(0.05, dt * 2));
    },
    render(t, dt) {
      const pr = renderer.getPixelRatio();
      if (canvas.width !== Math.floor(canvas.clientWidth * pr) || canvas.height !== Math.floor(canvas.clientHeight * pr)) resize();
      for (const l of leds) l.material = Math.sin(t * l.userData.speed + l.userData.phase) > 0.2 ? (l.userData.red ? ledRed : ledOn) : ledOff;
      for (const s of arcadeScreens) s.material.emissive.setHSL((t * 0.15) % 1, 0.8, 0.45);
      for (const s of steam) { const k = (t * 0.6 + s.userData.k) % 1; s.position.set(s.userData.base.x + Math.sin(t * 3 + s.userData.k * 9) * 0.1, s.userData.base.y + 0.2 + k * 1.4, s.userData.base.z); s.material.opacity = 0.6 * (1 - k); s.scale.setScalar(1 + k); }
      for (const a of agents) {
        const list = screensByOwner[a.id];
        if (list) for (const s of list) s.material.emissiveIntensity = a.busy ? 1.0 + 0.25 * Math.sin(t * 6) : a.doing === "desk" ? 0.6 : 0.25;
        const h = humans.get(a);
        if (h) animateHuman(a, h, t, dt); else animateAgent(a, meshes.get(a), t, dt);
      }
      updatePackets(t);
      if (mode === "fps") updatePlayer(dt, t); else controls.update();
      renderer.render(scene, camera);
    },
  };
}
