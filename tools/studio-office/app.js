// GRABZ STUDIO — живой пиксель-арт офис студии Claude Code Game Studios.
// Мир рисуется в «пиксельный» холст 1600×1024 (тайл 16 px) и масштабируется без сглаживания;
// текст (имена, реплики, частицы-символы) — поверх, в экранных координатах, чтобы был чётким.
"use strict";

const T = 16, W = 100, H = 64, WPX = W * T, HPX = H * T;
const now = () => performance.now() / 1000;
const rand = (a, b) => a + Math.random() * (b - a);
const pick = arr => arr[Math.floor(Math.random() * arr.length)];
function hash(s) { let h = 2166136261; for (const c of s) { h ^= c.charCodeAt(0); h = Math.imul(h, 16777619); } return h >>> 0; }
function seeded(seed) { let s = seed || 1; return () => ((s = Math.imul(s ^ (s >>> 15), 2246822507) ^ Math.imul(s ^ (s >>> 13), 3266489909)) >>> 0) / 4294967296; }

// ====================================================================== карта
const solid = new Uint8Array(W * H);        // 0 — можно ходить, 1 — стена, 2 — мебель
const floor = new Array(W * H).fill("grass");
const glassTile = new Uint8Array(W * H);
const idx = (x, y) => y * W + x;
const walkable = (x, y) => x >= 0 && y >= 0 && x < W && y < H && solid[idx(x, y)] === 0;

const ROOMS = [
  { id: "dir",   x0: 1,  y0: 1,  x1: 26, y1: 16, floor: "wood",    doors: [["b", 12, 3]] },
  { id: "art",   x0: 26, y0: 1,  x1: 56, y1: 16, floor: "teal",    doors: [["b", 40, 3]], glass: "b" },
  { id: "dev",   x0: 56, y0: 1,  x1: 98, y1: 16, floor: "dark",    doors: [["b", 76, 3]], glass: "b" },
  { id: "eng",   x0: 1,  y0: 24, x1: 24, y1: 38, floor: "purple",  doors: [["t", 11, 3], ["b", 11, 3]] },
  { id: "audio", x0: 1,  y0: 38, x1: 24, y1: 50, floor: "wood2",   doors: [["r", 43, 3]] },
  { id: "qa",    x0: 24, y0: 24, x1: 52, y1: 50, floor: "concrete",doors: [["t", 36, 4]], glass: "t" },
  { id: "prod",  x0: 52, y0: 24, x1: 72, y1: 38, floor: "red",     doors: [["t", 61, 3], ["b", 61, 3]] },
  { id: "lobby", x0: 52, y0: 38, x1: 72, y1: 50, floor: "marble",  doors: [["b", 60, 4], ["l", 43, 3], ["r", 43, 3]] },
  { id: "lounge",x0: 72, y0: 24, x1: 98, y1: 50, floor: "kitchen", doors: [["t", 84, 3]] },
];
const ROOM_NAMES = { dir: DEPARTMENTS.dir.name, art: DEPARTMENTS.art.name, dev: DEPARTMENTS.dev.name, eng: DEPARTMENTS.eng.name,
  audio: DEPARTMENTS.audio.name, qa: DEPARTMENTS.qa.name, prod: DEPARTMENTS.prod.name, lobby: "Ресепшен", lounge: "Кухня и лаунж" };
const ROOM_COLORS = { lobby: "#e0d6c0", lounge: "#ffd28a" };

function buildMap() {
  // улица: трава, тротуар, парковка, дорога
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    floor[idx(x, y)] = y >= 60 ? "road" : y >= 52 ? (x >= 30 && x <= 92 ? "asphalt" : "grass") : y === 51 ? "sidewalk" : "grass";
  }
  // коридор
  for (let y = 17; y <= 23; y++) for (let x = 2; x <= 97; x++) floor[idx(x, y)] = "hall";
  for (const r of ROOMS) {
    for (let y = r.y0; y <= r.y1; y++) for (let x = r.x0; x <= r.x1; x++) {
      const edge = x === r.x0 || x === r.x1 || y === r.y0 || y === r.y1;
      if (edge) { solid[idx(x, y)] = 1; floor[idx(x, y)] = r.floor; }
      else floor[idx(x, y)] = r.floor;
    }
    if (r.glass === "b") for (let x = r.x0 + 1; x < r.x1; x++) glassTile[idx(x, r.y1)] = 1;
    if (r.glass === "t") for (let x = r.x0 + 1; x < r.x1; x++) glassTile[idx(x, r.y0)] = 1;
  }
  // внешний контур здания у коридора
  for (let y = 16; y <= 24; y++) { solid[idx(1, y)] = 1; solid[idx(98, y)] = 1; }
  for (const r of ROOMS) for (const [side, at, len] of r.doors) for (let i = 0; i < len; i++) {
    const [x, y] = side === "b" ? [at + i, r.y1] : side === "t" ? [at + i, r.y0] : side === "l" ? [r.x0, at + i] : [r.x1, at + i];
    solid[idx(x, y)] = 0; glassTile[idx(x, y)] = 0; floor[idx(x, y)] = "door";
  }
}

// ====================================================================== мебель и точки интереса
const furniture = [];   // {t, x, y, w, h, ...}
const animated = [];    // то, что мигает каждый кадр
const POI = { coffee: [], cooler: [], fridge: [], sofa: [], vr: [], arcade: [], foos: [], wander: [], callSpot: null };
const seats = {};       // id агента → {x, y, room}

function block(x, y, w, h) { for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) solid[idx(x + i, y + j)] = 2; }
function add(t, x, y, w = 1, h = 1, extra = {}, isSolid = true) {
  const f = Object.assign({ t, x, y, w, h }, extra);
  furniture.push(f);
  if (isSolid) block(x, y, w, h);
  return f;
}

function deskFor(id, sx, sy, room, style) {
  seats[id] = { x: sx, y: sy, room };
  add("desk", sx - 1, sy - 1, 3, 1, { owner: id, style });
}

function buildFurniture(ids) {
  const byDept = {};
  for (const id of ids) (byDept[ROLES[id] ? ROLES[id][1] : "prod"] ||= []).push(id);

  // --- руководство: тёмное дерево, кожаные кресла, доска с планом
  add("board", 4, 2, 18, 2, { kind: "plan" });
  const dirSeats = [[6, 9], [13, 9], [20, 9]];
  (byDept.dir || []).forEach((id, i) => {
    const [sx, sy] = dirSeats[i % 3];
    seats[id] = { x: sx, y: sy, room: "dir" };
    add("bigdesk", sx - 2, sy - 2, 5, 2, { owner: id });
  });
  POI.callSpot = { x: 13, y: 11 };
  add("sofa", 4, 13, 5, 1, { color: "#6b3b2a" }, false);
  add("plant", 2, 2); add("plant", 24, 2); add("plant", 24, 14);
  add("poster", 22, 1, 2, 1, { art: "gorilla" }, false);

  // --- арт и левел-дизайн
  const artCols = [29, 34, 39, 44, 49, 54], artRows = [6, 12];
  let k = 0;
  const artIds = byDept.art || [];
  for (const row of artRows) for (const col of artCols) {
    if (k < artIds.length) deskFor(artIds[k], col, row, "art", "art");
    else add(k % 3 === 0 ? "easel" : k % 3 === 1 ? "model_bank" : "model_gorilla", col - 1, row - 1, k % 3 === 0 ? 2 : 3, 1);
    k++;
  }
  add("poster", 28, 1, 2, 1, { art: "bank" }, false); add("poster", 46, 1, 2, 1, { art: "sunset" }, false);
  add("plant", 27, 14); add("plant", 55, 2);

  // --- разработка и сеть: серверные стойки, мультимониторы, схема сети
  for (let x = 58; x <= 70; x += 2) { const f = add("rack", x, 2, 2, 2); animated.push(f); }
  add("board", 73, 2, 10, 2, { kind: "net" });
  add("poster", 86, 1, 2, 1, { art: "gorilla" }, false);
  for (let x = 90; x <= 96; x += 2) { const f = add("rack", x, 2, 2, 2); animated.push(f); }
  const devIds = byDept.dev || [];
  const devCols = [60, 65, 70, 75, 80, 85, 90, 95], devRows = [7, 13];
  k = 0;
  for (const row of devRows) for (const col of devCols) { if (k < devIds.length) deskFor(devIds[k], col, row, "dev", "dev"); k++; }

  // --- гильдия движков
  const engIds = byDept.eng || [];
  const engCols = [4, 8, 12, 16, 20], engRows = [28, 34];
  k = 0;
  for (const row of engRows) for (const col of engCols) { if (k < engIds.length) deskFor(engIds[k], col, row, "eng", "eng"); k++; }
  add("plant", 22, 25); add("plant", 2, 36);

  // --- звук и сценарий: звукоизолированная будка
  const audIds = byDept.audio || [];
  const audSeats = [[4, 42], [8, 42], [4, 47], [8, 47], [12, 47], [12, 42]];
  audIds.forEach((id, i) => deskFor(id, audSeats[i][0], audSeats[i][1], "audio", "audio"));
  add("booth", 15, 40, 8, 9, {}, false);
  block(15, 40, 8, 1); block(15, 48, 8, 1); block(22, 40, 1, 9); block(15, 41, 1, 3); block(15, 46, 1, 2);
  add("mixer", 18, 41, 3, 1);
  add("mic", 19, 45, 1, 1);
  POI.booth = [{ x: 19, y: 44 }, { x: 18, y: 42 }];

  // --- VR QA-лаборатория: столы у стекла + коврики
  const qaIds = byDept.qa || [];
  const qaSeats = [[28, 27], [32, 27], [47, 27]];
  qaIds.forEach((id, i) => deskFor(id, qaSeats[i][0], qaSeats[i][1], "qa", "qa"));
  const mats = [[27, 31, "#ff5c8a"], [38, 31, "#4fc3f7"], [27, 40, "#ffcc4d"], [38, 40, "#66bb6a"]];
  for (const [x, y, c] of mats) { add("vrmat", x, y, 9, 8, { color: c }, false); POI.vr.push({ x: x + 4, y: y + 4 }); }
  add("tv", 48, 34, 3, 2); add("plant", 50, 48); add("plant", 25, 48);
  add("poster", 43, 24, 2, 1, { art: "vr" }, false);

  // --- продакшен и live-ops
  const prodIds = byDept.prod || [];
  const prodSeats = [[56, 28], [61, 28], [66, 28], [56, 34], [61, 34], [66, 34]];
  prodIds.forEach((id, i) => deskFor(id, prodSeats[i][0], prodSeats[i][1], "prod", "prod"));
  add("board", 66, 32, 5, 2, { kind: "kanban" });
  add("plant", 70, 25);

  // --- ресепшен
  add("reception", 57, 41, 8, 2);
  add("rug", 59, 46, 6, 3, {}, false);
  add("plant", 53, 39); add("plant", 70, 39); add("plant", 53, 48); add("plant", 70, 48);
  add("poster", 65, 38, 3, 1, { art: "logo" }, false);

  // --- кухня и лаунж
  add("counter", 73, 25, 10, 1);
  const cm = add("coffee", 74, 25, 2, 1, {}, false); animated.push(cm);
  add("fridge", 84, 25, 2, 2);
  add("cooler", 88, 25, 1, 1);
  const arc = add("arcade", 95, 25, 2, 2); animated.push(arc);
  add("foos", 87, 33, 4, 2);
  add("table", 76, 31, 3, 2); add("table", 76, 36, 3, 2);
  add("sofa", 75, 44, 7, 2, { color: "#3f6fb5" }, false);
  add("sofa", 87, 44, 7, 2, { color: "#b5473f" }, false);
  add("rug", 80, 40, 8, 3, { color: "#d8b46a" }, false);
  add("plant", 73, 48); add("plant", 96, 48); add("plant", 96, 30);
  add("poster", 91, 24, 2, 1, { art: "banana" }, false);
  POI.coffee = [{ x: 74, y: 26 }, { x: 75, y: 26 }, { x: 76, y: 27 }];
  POI.cooler = [{ x: 88, y: 26 }, { x: 87, y: 27 }, { x: 89, y: 27 }];
  POI.fridge = [{ x: 84, y: 27 }];
  POI.arcade = [{ x: 95, y: 27 }, { x: 96, y: 27 }];
  POI.foos = [{ x: 86, y: 33 }, { x: 91, y: 34 }];
  for (let x = 76; x <= 80; x++) POI.sofa.push({ x, y: 44 });
  for (let x = 88; x <= 92; x++) POI.sofa.push({ x, y: 44 });
  for (const [x, y] of [[78, 30], [79, 33], [77, 35], [79, 38]]) POI.sofa.push({ x, y, chair: true });

  // --- коридор: растения, плакаты GRABZ VR
  for (const x of [6, 20, 34, 48, 62, 90]) add("plant", x, 17);
  for (const x of [18, 46, 70]) add("poster", x, 16, 2, 1, { art: x === 44 ? "bank" : "gorilla" }, false);
  for (let x = 4; x <= 95; x += 7) POI.wander.push({ x, y: 20 });

  // --- улица: фургон, машины, деревья
  add("van", 62, 53, 8, 4, {}, true);
  add("car", 40, 53, 5, 3, { color: "#e86fb0" }, true);
  add("car", 78, 53, 5, 3, { color: "#7fd1d8" }, true);
  for (const x of [3, 9, 15, 21, 96]) add("tree", x, 53, 2, 2);
  for (let x = 32; x <= 90; x += 6) add("pline", x, 52, 1, 5, {}, false);
}

// ====================================================================== A*
function nearestWalkable(x, y) {
  if (walkable(x, y)) return { x, y };
  for (let r = 1; r < 8; r++) for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++)
    if (Math.max(Math.abs(dx), Math.abs(dy)) === r && walkable(x + dx, y + dy)) return { x: x + dx, y: y + dy };
  return { x, y };
}
function findPath(sx, sy, tx, ty) {
  const t = nearestWalkable(tx, ty); tx = t.x; ty = t.y;
  if (sx === tx && sy === ty) return [];
  const g = new Float32Array(W * H).fill(Infinity), came = new Int32Array(W * H).fill(-1), closed = new Uint8Array(W * H);
  const heap = []; const push = (i, f) => { heap.push([f, i]); let c = heap.length - 1; while (c > 0) { const p = (c - 1) >> 1; if (heap[p][0] <= heap[c][0]) break; [heap[p], heap[c]] = [heap[c], heap[p]]; c = p; } };
  const pop = () => { const top = heap[0], last = heap.pop(); if (heap.length) { heap[0] = last; let c = 0; for (;;) { const l = c * 2 + 1, r = l + 1; let m = c; if (l < heap.length && heap[l][0] < heap[m][0]) m = l; if (r < heap.length && heap[r][0] < heap[m][0]) m = r; if (m === c) break; [heap[m], heap[c]] = [heap[c], heap[m]]; c = m; } } return top; };
  const s = idx(sx, sy), goal = idx(tx, ty);
  g[s] = 0; push(s, 0);
  while (heap.length) {
    const [, cur] = pop();
    if (cur === goal) break;
    if (closed[cur]) continue; closed[cur] = 1;
    const cx = cur % W, cy = (cur / W) | 0;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const nx = cx + dx, ny = cy + dy;
      if (!walkable(nx, ny)) continue;
      const n = idx(nx, ny), ng = g[cur] + 1;
      if (ng < g[n]) { g[n] = ng; came[n] = cur; push(n, ng + Math.abs(nx - tx) + Math.abs(ny - ty)); }
    }
  }
  if (came[goal] < 0) return [];
  const path = []; for (let c = goal; c !== s; c = came[c]) path.push({ x: c % W, y: (c / W) | 0 });
  return path.reverse();
}

// ====================================================================== пиксельная отрисовка
const P = (c, x, y, w, h, col) => { c.fillStyle = col; c.fillRect(x, y, w, h); };
function shade(hex, k) {
  const n = parseInt(hex.slice(1), 16); let r = n >> 16, g = (n >> 8) & 255, b = n & 255;
  const f = v => Math.max(0, Math.min(255, Math.round(k > 0 ? v + (255 - v) * k : v * (1 + k))));
  return "#" + ((1 << 24) | (f(r) << 16) | (f(g) << 8) | f(b)).toString(16).slice(1);
}

const FLOOR = {
  wood: ["#7a4f33", "#6a432b"], wood2: ["#b48555", "#a27548"], teal: ["#2f7d84", "#2a6f76"], dark: ["#2d3242", "#272b39"],
  purple: ["#584787", "#4f3f7a"], concrete: ["#4b505b", "#444953"], red: ["#86394a", "#7a3242"], marble: ["#e7e2d6", "#d9d3c4"],
  kitchen: ["#ece6d8", "#c8bfaa"], hall: ["#a69d8f", "#9a9183"], grass: ["#4f9a4a", "#478d43"], asphalt: ["#3b3d44", "#363840"],
  road: ["#2c2d33", "#2c2d33"], sidewalk: ["#b3ada2", "#a39d92"], door: ["#a69d8f", "#958c7e"],
};

function drawFloorTile(c, x, y, type, r) {
  const [a, b] = FLOOR[type] || FLOOR.hall; const px = x * T, py = y * T;
  P(c, px, py, T, T, a);
  switch (type) {
    case "wood": case "wood2":
      for (let j = 0; j < 4; j++) { P(c, px, py + j * 4 + 3, T, 1, b); P(c, px + ((x * 7 + j * 5) % 16), py + j * 4, 1, 3, b); }
      if (r() < 0.08) P(c, px + 5, py + 6, 2, 1, b); break;
    case "teal": case "purple": case "red":
      for (let j = 0; j < 16; j += 4) for (let i = (j / 4) % 2 * 2; i < 16; i += 4) P(c, px + i, py + j, 1, 1, b); break;
    case "dark": case "concrete": case "hall": case "door":
      P(c, px, py, T, 1, b); P(c, px, py, 1, T, b); if (r() < 0.15) P(c, px + 4 + r() * 8, py + 4 + r() * 8, 2, 1, shade(a, 0.06)); break;
    case "marble":
      P(c, px, py, T, 1, b); P(c, px, py, 1, T, b); for (let i = 0; i < 6; i++) P(c, px + 2 + i * 2, py + 3 + Math.round(Math.sin((x + i) * 1.7) * 2) + i, 1, 1, "#cfc8b6"); break;
    case "kitchen": if ((x + y) % 2) P(c, px, py, T, T, b); break;
    case "grass": for (let i = 0; i < 5; i++) P(c, px + r() * 15, py + r() * 15, 1, 2, b); if (r() < 0.05) P(c, px + 6, py + 7, 2, 2, "#f4d35e"); break;
    case "asphalt": if (r() < 0.3) P(c, px + r() * 15, py + r() * 15, 1, 1, "#45474e"); break;
    case "road": if (y === 62 && x % 4 < 2) P(c, px, py + 7, T, 2, "#e9e2c9"); break;
    case "sidewalk": P(c, px, py, T, 1, b); P(c, px + (y % 2) * 8, py, 1, T, b); break;
  }
}

function drawWall(c, x, y) {
  const px = x * T, py = y * T;
  if (glassTile[idx(x, y)]) {
    P(c, px, py, T, T, "rgba(160,215,240,0.35)");
    P(c, px, py + 6, T, 2, "#cfe9f5"); P(c, px, py + 2, T, 1, "rgba(255,255,255,0.6)");
    if (x % 4 === 0) P(c, px, py, 2, T, "#6b7c8a");
    return;
  }
  P(c, px, py, T, T, "#4a4458"); P(c, px, py, T, 5, "#6e6688"); P(c, px, py + T - 2, T, 2, "#2e2a39");
  if ((x + y) % 3 === 0) P(c, px + 3, py + 8, 4, 1, "#544e64");
}

function drawGorilla(c, x, y, s, fur = "#3a3a44") {
  P(c, x + 2 * s, y, 6 * s, 2 * s, fur); P(c, x + s, y + 2 * s, 8 * s, 5 * s, fur);
  P(c, x + 2 * s, y + 3 * s, 6 * s, 3 * s, "#c9a27e"); P(c, x + 3 * s, y + 3 * s, s, s, "#111"); P(c, x + 6 * s, y + 3 * s, s, s, "#111");
  P(c, x + 4 * s, y + 5 * s, 2 * s, s, "#5a3d2b");
}

function drawFurniture(c, f) {
  const x = f.x * T, y = f.y * T, w = f.w * T, h = f.h * T;
  switch (f.t) {
    case "desk": {
      const top = { dev: "#3b3f4c", art: "#d9c3a0", eng: "#5a5368", audio: "#7a5a3a", qa: "#e2e2e2", prod: "#9a6b4f" }[f.style] || "#a07a55";
      P(c, x, y + 2, w, h - 2, top); P(c, x, y + h - 4, w, 4, shade(top, -0.35)); P(c, x, y + 2, w, 1, shade(top, 0.25));
      if (f.style === "dev") { for (let i = 0; i < 3; i++) { P(c, x + 3 + i * 14, y - 2, 12, 9, "#111"); P(c, x + 4 + i * 14, y - 1, 10, 6, "#1d4f7a"); } }
      else if (f.style === "art") { P(c, x + 10, y - 2, 18, 10, "#111"); P(c, x + 11, y - 1, 16, 7, "#7a3e6b"); P(c, x + 30, y + 4, 12, 7, "#333"); P(c, x + 32, y + 5, 8, 5, "#555"); P(c, x + 4, y + 6, 3, 3, "#e94e77"); }
      else if (f.style === "audio") { P(c, x + 6, y - 1, 14, 8, "#111"); P(c, x + 7, y, 12, 5, "#3e5c3a"); P(c, x + 26, y + 3, 14, 6, "#2a2a2a"); for (let i = 0; i < 5; i++) P(c, x + 28 + i * 2, y + 4 + (i % 3), 1, 3, "#ffb74d"); }
      else if (f.style === "qa") { P(c, x + 8, y - 1, 14, 8, "#111"); P(c, x + 9, y, 12, 5, "#2e6b3a"); P(c, x + 28, y + 4, 10, 5, "#222"); P(c, x + 29, y + 5, 3, 3, "#66bb6a"); }
      else { P(c, x + 12, y - 1, 16, 9, "#111"); P(c, x + 13, y, 14, 6, f.style === "eng" ? "#3a2f5a" : "#4a3a2a"); }
      P(c, x + w / 2 - 6, y + h - 3, 12, 3, "#e8d9a8"); // табличка с именем
      break;
    }
    case "bigdesk":
      P(c, x, y + 2, w, h - 2, "#4a2c1d"); P(c, x, y + h - 5, w, 5, "#331d12"); P(c, x + 2, y + 4, w - 4, 1, "#6b4430");
      P(c, x + 28, y + 1, 18, 10, "#111"); P(c, x + 29, y + 2, 16, 7, "#2f4f7a"); P(c, x + 8, y + 8, 10, 6, "#f2efe6"); P(c, x + 52, y + 6, 6, 6, "#c9a227");
      P(c, x + w / 2 - 8, y + h - 4, 16, 3, "#c9a227");
      break;
    case "board": {
      P(c, x, y, w, h, "#ddd8cc"); P(c, x + 2, y + 2, w - 4, h - 5, "#f7f6f0"); P(c, x, y + h - 3, w, 3, "#8a8578");
      const r = seeded(f.x * 31 + f.y);
      if (f.kind === "plan") { for (let i = 0; i < 14; i++) P(c, x + 6 + i * 20, y + 5 + (i % 2) * 9, 12, 8, pick(["#ffd54f", "#ff8a80", "#80d8ff", "#b9f6ca"])); P(c, x + 6, y + 24, w - 12, 1, "#3a6"); }
      else if (f.kind === "net") { for (let i = 0; i < 6; i++) { const nx = x + 10 + i * 24, ny = y + 8 + (i % 2) * 12; P(c, nx, ny, 8, 6, "#4fc3f7"); if (i) P(c, nx - 16, ny + 3, 16, 1, "#333"); } }
      else for (let col = 0; col < 3; col++) for (let i = 0; i < 3; i++) if (r() < 0.8) P(c, x + 4 + col * 25, y + 5 + i * 8, 18, 6, ["#ffd54f", "#80d8ff", "#b9f6ca"][col]);
      break;
    }
    case "plant":
      P(c, x + 4, y + 9, 8, 6, "#a0522d"); P(c, x + 4, y + 9, 8, 2, "#c0703d");
      P(c, x + 2, y + 1, 12, 9, "#2e7d32"); P(c, x + 4, y - 2, 8, 6, "#43a047"); P(c, x + 6, y + 3, 3, 3, "#66bb6a"); break;
    case "poster": {
      P(c, x + 2, y + 2, w - 4, T - 2, "#111");
      const bg = { gorilla: "#ff6f3c", bank: "#ffd166", sunset: "#ff7aa2", vr: "#4fc3f7", logo: "#2bd47a", banana: "#ffe14d" }[f.art];
      P(c, x + 3, y + 3, w - 6, T - 4, bg);
      if (f.art === "gorilla" || f.art === "vr" || f.art === "logo") { drawGorilla(c, x + w / 2 - 5, y + 4, 1); if (f.art === "vr") P(c, x + w / 2 - 4, y + 7, 8, 2, "#111"); }
      else if (f.art === "bank") { P(c, x + 6, y + 9, w - 12, 2, "#fff"); for (let i = 0; i < 4; i++) P(c, x + 8 + i * 5, y + 5, 2, 4, "#fff"); P(c, x + 6, y + 4, w - 12, 1, "#fff"); }
      else if (f.art === "sunset") { P(c, x + 3, y + 9, w - 6, 4, "#5b3f8c"); P(c, x + w / 2 - 3, y + 5, 6, 4, "#ffd166"); }
      else { P(c, x + 8, y + 5, 12, 3, "#e0b000"); P(c, x + 10, y + 8, 10, 2, "#e0b000"); }
      break;
    }
    case "easel":
      P(c, x + 6, y - 6, 2, 22, "#8d6e63"); P(c, x + 22, y - 6, 2, 22, "#8d6e63");
      P(c, x + 2, y - 10, 26, 18, "#fff"); P(c, x + 4, y - 8, 22, 7, "#ff7aa2"); P(c, x + 4, y - 1, 22, 7, "#5b3f8c"); P(c, x + 12, y - 6, 6, 4, "#ffd166"); break;
    case "model_bank":
      P(c, x, y + 2, w, h, "#d9c3a0"); P(c, x + 8, y - 8, 30, 14, "#f2efe6"); for (let i = 0; i < 5; i++) P(c, x + 10 + i * 6, y - 4, 2, 8, "#bdb6a6");
      P(c, x + 6, y - 10, 34, 3, "#e0d8c8"); P(c, x + 20, y + 1, 6, 5, "#c9a227"); break;
    case "model_gorilla":
      P(c, x, y + 2, w, h, "#d9c3a0"); drawGorilla(c, x + 14, y - 10, 2); break;
    case "rack":
      P(c, x, y, w, h + 6, "#1c1f27"); P(c, x + 1, y + 1, w - 2, h + 4, "#262a35");
      for (let j = 0; j < 7; j++) P(c, x + 3, y + 3 + j * 5, w - 6, 3, "#30364a"); break;
    case "booth":
      P(c, x, y, w, h, "#2b2b33");
      for (let j = 0; j < h; j += 4) for (let i = (j / 4) % 2 * 4; i < w; i += 8) P(c, x + i, y + j, 4, 4, "#36363f");
      P(c, x + 16, y + 16, w - 32, h - 32, "#4a3b2c");
      P(c, x, y + T * 4, T, T * 2, FLOOR.wood2[0]);  // дверь будки
      P(c, x + w - 30, y + 3, 26, 9, "#ff3b3b"); P(c, x + w - 28, y + 5, 22, 5, "#ffd0d0"); break;
    case "mixer":
      P(c, x, y + 2, w, h, "#222"); for (let i = 0; i < 10; i++) { P(c, x + 3 + i * 4, y + 4, 1, 9, "#555"); P(c, x + 2 + i * 4, y + 5 + (i * 3) % 7, 3, 2, "#ffb74d"); } break;
    case "mic":
      P(c, x + 7, y + 4, 2, 12, "#888"); P(c, x + 5, y - 2, 6, 8, "#333"); P(c, x + 6, y - 1, 4, 5, "#666"); P(c, x + 3, y + 14, 10, 2, "#555"); break;
    case "vrmat":
      P(c, x, y, w, h, shade(f.color, -0.45)); P(c, x + 3, y + 3, w - 6, h - 6, shade(f.color, -0.25));
      P(c, x + 3, y + 3, w - 6, 2, f.color); P(c, x + 3, y + h - 5, w - 6, 2, f.color); P(c, x + 3, y + 3, 2, h - 6, f.color); P(c, x + w - 5, y + 3, 2, h - 6, f.color);
      P(c, x + w / 2 - 6, y + h / 2 - 6, 12, 12, shade(f.color, -0.1)); break;
    case "tv": P(c, x, y, w, h, "#111"); P(c, x + 2, y + 2, w - 4, h - 6, "#1b3b5a"); break;
    case "reception":
      P(c, x, y, w, h, "#2bd47a"); P(c, x, y + h - 6, w, 6, "#1f9a58"); P(c, x + 2, y + 2, w - 4, 3, "#7ff0a8");
      P(c, x + 20, y - 3, 14, 9, "#111"); P(c, x + 21, y - 2, 12, 6, "#2f4f7a"); P(c, x + 70, y + 4, 8, 8, "#ffd166"); break;
    case "rug": P(c, x, y, w, h, f.color || "#8c2f39"); P(c, x + 3, y + 3, w - 6, h - 6, shade(f.color || "#8c2f39", 0.15)); P(c, x + 6, y + 6, w - 12, h - 12, f.color || "#8c2f39"); break;
    case "counter": P(c, x, y, w, h + 4, "#9e9e9e"); P(c, x, y, w, 4, "#cfcfcf"); P(c, x + 40, y + 4, 20, 6, "#bfe3ff"); break;
    case "coffee": P(c, x + 2, y - 6, 26, 18, "#3a3a3a"); P(c, x + 4, y - 4, 22, 6, "#5d4037"); P(c, x + 10, y + 4, 8, 6, "#fff"); break;
    case "fridge": P(c, x, y - 8, w, h + 8, "#dfe6ea"); P(c, x, y + 8, w, 1, "#9aa5ab"); P(c, x + w - 6, y - 4, 2, 8, "#9aa5ab"); P(c, x + 4, y - 4, 6, 4, "#ff8a80"); break;
    case "cooler": P(c, x + 3, y - 2, 10, 16, "#e0e0e0"); P(c, x + 4, y - 12, 8, 11, "rgba(100,180,255,0.8)"); P(c, x + 6, y + 6, 4, 2, "#2196f3"); break;
    case "arcade": P(c, x + 2, y - 10, w - 4, h + 10, "#5e35b1"); P(c, x + 6, y - 6, w - 12, 14, "#111"); P(c, x + 6, y + 12, w - 12, 6, "#311b92"); P(c, x + 10, y + 13, 3, 3, "#ff5252"); P(c, x + 18, y + 13, 3, 3, "#ffd740"); break;
    case "foos":
      P(c, x, y, w, h, "#5d4037"); P(c, x + 3, y + 3, w - 6, h - 6, "#2e7d32"); P(c, x + w / 2, y + 3, 1, h - 6, "#fff");
      for (let i = 0; i < 6; i++) { P(c, x + 6 + i * 9, y - 2, 1, h + 4, "#bbb"); P(c, x + 5 + i * 9, y + 8 + (i % 2) * 8, 3, 4, i < 3 ? "#e53935" : "#1e88e5"); } break;
    case "table": P(c, x + 2, y + 2, w - 4, h - 4, "#c8a27a"); P(c, x + 2, y + h - 6, w - 4, 4, "#9c7a55"); P(c, x + 14, y + 6, 6, 5, "#fff"); P(c, x + 15, y + 7, 4, 3, "#6d4c41"); break;
    case "sofa": { const col = f.color; P(c, x, y, w, h, shade(col, -0.3)); P(c, x + 2, y + 2, w - 4, h - 8, col); for (let i = 1; i < f.w; i++) P(c, x + i * T, y + 4, 1, h - 12, shade(col, -0.2)); P(c, x, y, 6, h, shade(col, -0.15)); P(c, x + w - 6, y, 6, h, shade(col, -0.15)); break; }
    case "van":
      P(c, x + 4, y + h - 6, w - 8, 6, "rgba(0,0,0,0.3)");
      P(c, x, y, w, h - 4, "#2bd47a"); P(c, x, y, w, 6, "#7ff0a8"); P(c, x + 2, y + h - 10, w - 4, 4, "#1f9a58");
      P(c, x + w - 26, y + 8, 20, 18, "#1c3d4f"); P(c, x + w - 24, y + 9, 8, 6, "#5fb3d9");
      P(c, x + 10, y + 16, 64, 14, "#1f9a58");
      for (const wx of [14, w - 34]) { P(c, x + wx, y + h - 8, 16, 8, "#111"); P(c, x + wx + 5, y + h - 6, 6, 4, "#777"); }
      c.fillStyle = "#0d3d22"; c.font = "bold 11px monospace"; c.fillText("GRABZ", x + 18, y + 27); break;
    case "car":
      P(c, x + 2, y + h - 4, w - 4, 4, "rgba(0,0,0,0.3)"); P(c, x, y + 4, w, h - 8, f.color); P(c, x + 10, y, w - 26, h - 4, shade(f.color, 0.2));
      P(c, x + 14, y + 4, w - 34, h - 14, "#1c3d4f"); P(c, x + 4, y + h - 6, 10, 6, "#111"); P(c, x + w - 16, y + h - 6, 10, 6, "#111"); break;
    case "tree":
      P(c, x + 12, y + 16, 8, 14, "#6d4c41"); P(c, x - 2, y - 10, 36, 28, "#2e7d32"); P(c, x + 4, y - 16, 24, 14, "#43a047"); P(c, x + 6, y - 4, 8, 6, "#66bb6a"); break;
    case "pline": P(c, x + 7, y, 2, h, "#e9e2c9"); break;
  }
}

// ====================================================================== статичный фон
const bg = document.createElement("canvas"); bg.width = WPX; bg.height = HPX;
function renderStatic() {
  const c = bg.getContext("2d"); const r = seeded(1234);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) drawFloorTile(c, x, y, floor[idx(x, y)], r);
  // ковровые зоны и подложки — под мебелью
  const order = ["rug", "vrmat", "booth", "pline"];
  for (const f of furniture.filter(f => order.includes(f.t))) drawFurniture(c, f);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (solid[idx(x, y)] === 1) drawWall(c, x, y);
  for (const f of furniture.filter(f => !order.includes(f.t)).sort((a, b) => a.y - b.y)) drawFurniture(c, f);
  // кресла у мест
  for (const s of Object.values(seats)) { P(c, s.x * T + 3, s.y * T + 6, 10, 9, s.room === "dir" ? "#3e2318" : "#3a3a48"); P(c, s.x * T + 4, s.y * T + 12, 8, 3, "#222"); }
}

// ====================================================================== агенты
const SKIN = ["#f3d2b3", "#e8b48f", "#c98c65", "#a0673f", "#7a4a2a", "#f6dcc8"];
const HAIR = ["#2b1d14", "#4a2f1d", "#8a5a2b", "#d8b26a", "#1b1b1b", "#b0442d", "#7a7a7a", "#e8e0d0", "#ff6fae", "#4fc3f7"];
const HOODIE_BRIGHT = ["#ff6fae", "#ffb300", "#26c6da", "#ab47bc", "#ff7043", "#9ccc65"];
const HOODIE_DARK = ["#2b2f3a", "#1f2633", "#33293d", "#263238", "#2e3b2e"];

class Agent {
  constructor(id, meta, i) {
    this.id = id; this.meta = meta || {};
    const role = ROLES[id] || [id, "prod", "Задачи студии"];
    this.title = role[0]; this.dept = role[1]; this.defaultTask = role[2];
    this.seat = seats[id] || { x: 62, y: 20, room: "lobby" };
    this.x = this.seat.x * T + T / 2; this.y = this.seat.y * T + T / 2;
    this.path = []; this.doing = null; this.until = 0; this.after = null;
    this.busy = false; this.task = null; this.bubble = null; this.thought = null;
    this.done = 0; this.facing = "up"; this.walkT = rand(0, 10); this.nod = 0; this.wave = false;
    this.speed = 40; this.nextThink = now() + rand(0.5, 6);
    const r = seeded(hash(id));
    this.skin = SKIN[Math.floor(r() * SKIN.length)];
    this.hair = HAIR[Math.floor(r() * (this.dept === "art" ? HAIR.length : 8))];
    this.hairStyle = Math.floor(r() * 6);
    this.bright = HOODIE_BRIGHT[Math.floor(r() * HOODIE_BRIGHT.length)];
    this.darkHood = HOODIE_DARK[Math.floor(r() * HOODIE_DARK.length)];
    this.accessory = r();
    this.pants = ["#2c3e66", "#333", "#4e3b2a", "#3b4a3b"][Math.floor(r() * 4)];
    this.blinkAt = now() + rand(1, 5);
  }
  get tx() { return Math.floor(this.x / T); }
  get ty() { return Math.floor(this.y / T); }
  get atSeat() { return this.tx === this.seat.x && this.ty === this.seat.y && !this.path.length; }

  goTo(tx, ty, then, run = false) {
    this.path = findPath(this.tx, this.ty, tx, ty);
    this.speed = run ? 85 : rand(34, 46);
    this.doing = "walk"; this.after = then || null;
  }
  say(text, dur = 3.2, kind = "say") { this.bubble = { text, until: now() + dur, kind }; }

  update(dt, t) {
    if (this.path.length) {
      const n = this.path[0], gx = n.x * T + T / 2, gy = n.y * T + T / 2;
      const dx = gx - this.x, dy = gy - this.y, d = Math.hypot(dx, dy), step = this.speed * dt;
      if (Math.abs(dx) > Math.abs(dy)) this.facing = dx > 0 ? "right" : "left"; else if (d > 0.1) this.facing = dy > 0 ? "down" : "up";
      if (d <= step) { this.x = gx; this.y = gy; this.path.shift(); } else { this.x += dx / d * step; this.y += dy / d * step; }
      this.walkT += dt * this.speed / 9;
      if (!this.path.length) { const cb = this.after; this.after = null; this.doing = null; if (cb) cb(); }
      return;
    }
    if (this.doing && this.doing !== "work" && t > this.until) { const cb = this.after; this.after = null; this.doing = null; if (cb) cb(); }
    if (this.busy && this.doing !== "work" && !this.doing) { this.startWork(); return; }
    if (!this.busy && !this.doing && t > this.nextThink) this.think(t);
  }

  startWork() {
    if (!this.busy) return;
    if (!this.atSeat) { this.goTo(this.seat.x, this.seat.y, () => this.startWork(), true); return; }
    this.doing = "work"; this.facing = "up";
  }
  stay(kind, secs, then, facing) { this.doing = kind; this.until = now() + secs; this.after = then || null; if (facing) this.facing = facing; }

  think(t) {
    this.nextThink = t + rand(2, 6);
    const qa = this.dept === "qa";
    const roll = Math.random();
    const back = () => { this.nextThink = now() + rand(1, 4); };
    if (roll < 0.36 || (qa && roll < 0.2)) {
      this.goTo(this.seat.x, this.seat.y, () => this.stay("desk", rand(8, 20), back, "up"));
    } else if (roll < 0.5) {
      const p = pick(POI.coffee);
      this.goTo(p.x, p.y, () => { this.stay("coffee", rand(4, 8), back, "up"); if (Math.random() < 0.5) this.think2(); });
    } else if (roll < 0.6) {
      const p = pick(POI.cooler);
      this.goTo(p.x, p.y, () => { this.stay("chat", rand(6, 10), back); this.chatNearby(); });
    } else if (roll < 0.7) {
      const p = pick(POI.sofa);
      this.goTo(p.x, p.y, () => { this.stay("sofa", rand(10, 18), back, "down"); if (Math.random() < 0.6) this.think2(); });
    } else if (roll < 0.8 || (qa && roll < 0.9)) {
      const p = pick(POI.vr);
      this.goTo(p.x + Math.round(rand(-2, 2)), p.y + Math.round(rand(-2, 2)), () => { this.stay("vr", rand(8, 14), back, "down"); this.say(pick(VR_LINES), 3, "say"); });
    } else if (roll < 0.86) {
      const p = pick(POI.arcade); this.goTo(p.x, p.y, () => this.stay("arcade", rand(6, 12), back, "up"));
    } else if (roll < 0.92) {
      const p = pick(POI.foos); this.goTo(p.x, p.y, () => this.stay("foos", rand(6, 12), back, p.x < 88 ? "right" : "left"));
    } else if (this.dept === "audio" && POI.booth) {
      const p = pick(POI.booth); this.goTo(p.x, p.y, () => { this.stay("booth", rand(6, 10), back, "up"); this.say("♪ Раз-раз, проверка...", 2.5); });
    } else {
      const p = pick(POI.wander); this.goTo(p.x + Math.round(rand(-2, 2)), p.y + Math.round(rand(-2, 2)), () => { this.stay("stand", rand(2, 5), back, "down"); this.think2(); });
    }
  }
  think2() { this.thought = { text: pick(IDLE_THOUGHTS), until: now() + 3.5 }; }
  chatNearby() {
    const other = agents.find(a => a !== this && !a.busy && (a.doing === "chat" || a.doing === "coffee") && Math.hypot(a.x - this.x, a.y - this.y) < 80);
    const [q, ans] = pick(CHAT_PAIRS);
    this.say(q, 3);
    if (other) setTimeout(() => other.say(ans, 3), 1600);
    else this.thought = { text: pick(IDLE_THOUGHTS), until: now() + 3.5 };
  }

  // ------------------------------------------------ задачи
  beginTask(text, source) {
    if (this.busy) return;
    this.busy = true; this.task = text || this.defaultTask;
    this.doing = null; this.path = []; this.after = null;
    this.say(pick(START_LINES), 2.5);
    this.startWork();
    addChat(this, `взял задачу: «${this.task}»`, source === "live" ? "sys" : "");
    const leadId = leadFor(this);
    const lead = leadId && byId[leadId];
    if (lead && lead !== this && !lead.visiting) setTimeout(() => lead.visitDesk(this), rand(2500, 5000));
  }
  finishTask(source) {
    if (!this.busy) return;
    this.busy = false; this.done++;
    const prevTask = this.task; this.task = null; this.doing = null; this.path = []; this.after = null;
    const targets = (HANDOFFS[this.id] || []).map(i => byId[i]).filter(Boolean);
    const to = targets.length ? pick(targets) : null;
    if (!to) { this.say("Готово! ✔", 2.5); addChat(this, `закончил «${prevTask}»`, source === "live" ? "sys" : ""); this.nextThink = now() + 2; return; }
    const pair = pick(HANDOFF_LINES[this.dept] || HANDOFF_LINES.dev);
    const line = pair[0].replace("{to}", to.title.toLowerCase());
    sendPacket(this, to);
    addChat(this, `${line} → <b>${to.title}</b>`, source === "live" ? "sys" : "");
    this.visiting = true;
    this.goTo(to.tx, to.ty + (to.atSeat ? 1 : 0), () => {
      this.visiting = false;
      this.facing = to.y < this.y ? "up" : "down";
      this.say(line, 3.4);
      setTimeout(() => { to.say(pair[1], 3.2); to.nod = now() + 0.9; addChat(to, pair[1], ""); }, 1700);
      this.stay("talk", 4.5, () => { this.nextThink = now() + 1; });
    });
    return to;
  }
  visitDesk(worker) {
    if (this.busy && this.doing === "work") { this.say(`${worker.title}, жду результат!`, 2.5); return; }
    this.visiting = true;
    const sx = worker.seat.x + 1, sy = worker.seat.y + 1;
    this.goTo(sx, sy, () => {
      this.visiting = false; this.facing = "up"; this.nod = now() + 1.2;
      this.say(pick(LEAD_LINES), 2.8);
      this.stay("inspect", 3.2, () => { this.nextThink = now() + 1; });
    });
  }
  callToDirector() {
    const d = byId["creative-director"];
    this.doing = null; this.path = []; this.after = null;
    this.say("Иду к директору!", 2.2);
    this.goTo(POI.callSpot.x + (this.id === "creative-director" ? 0 : Math.round(rand(-2, 2))), POI.callSpot.y, () => {
      this.facing = "up";
      if (d && d !== this) { d.facing = "down"; d.say(`Заходи, ${this.title.toLowerCase()}! Как успехи?`, 3.2); }
      setTimeout(() => this.say(this.busy ? `Делаю: ${this.task}` : "Всё по плану, шеф!", 3), 1500);
      this.stay("talk", 5, () => { this.nextThink = now() + 1; });
    }, true);
    addChat(this, "вызван к директору", "sys");
  }
}

function leadFor(a) {
  let lead = DEPARTMENTS[a.dept] && DEPARTMENTS[a.dept].lead;
  if (!lead || lead === a.id) lead = a.dept === "dev" || a.dept === "eng" ? "technical-director" : a.dept === "dir" ? null : a.dept === "qa" || a.dept === "prod" ? "producer" : "creative-director";
  return lead === a.id ? null : lead;
}

// ---------------------------------------------------------------- спрайт агента
function drawAgent(c, a, t, ox = 0, oy = 0, s = 1) {
  const x = Math.round(a.x - 6 + ox), sitting = (a.doing === "work" || a.doing === "desk") && a.atSeat;
  const sofa = a.doing === "sofa";
  let y = Math.round(a.y - 18 + oy);
  const moving = a.path.length > 0;
  const step = moving ? Math.sin(a.walkT) : 0;
  const bob = moving ? Math.abs(step) * -1 : 0;
  const nod = a.nod > t ? Math.round(Math.abs(Math.sin((a.nod - t) * 14)) * 1.5) : 0;
  const vr = a.doing === "vr";
  const back = a.facing === "up";
  if (sitting || sofa) y += 3;
  const R = (dx, dy, w, h, col) => P(c, x + dx * s, y + (dy + bob) * s, w * s, h * s, col);

  P(c, x + 1 * s, Math.round(a.y + 1 + oy) * 1, 11 * s, 3 * s, "rgba(0,0,0,0.25)");                 // тень
  // ноги
  if (!sitting) { const l = Math.round(step * 1.5); R(3, 14 + Math.max(0, l), 2, 4 - Math.max(0, l), a.pants); R(7, 14 + Math.max(0, -l), 2, 4 - Math.max(0, -l), a.pants); R(3, 17, 2, 1, "#222"); R(7, 17, 2, 1, "#222"); }
  // одежда
  let shirt = a.bright, trim = shade(a.bright, -0.25);
  switch (a.dept) {
    case "dir": shirt = a.id === "producer" ? "#5a6b7d" : "#2b2d42"; trim = "#1a1b29"; break;
    case "dev": shirt = a.darkHood; trim = shade(a.darkHood, 0.15); break;
    case "eng": shirt = a.id.startsWith("godot") ? "#478cbf" : "#22242b"; trim = a.id.startsWith("godot") ? "#2f6a96" : "#4a4d57"; break;
    case "audio": shirt = a.accessory > 0.5 ? "#8e3b2f" : "#3b5f8e"; trim = "#1d1d1d"; break;
    case "qa": shirt = "#3d8b4f"; trim = "#2a6136"; break;
    case "prod": shirt = "#e9e3d6"; trim = "#b9b2a4"; break;
  }
  R(2, 8, 8, 7, shirt); R(2, 14, 8, 1, trim);
  if (a.dept === "dir") { R(5, 8, 2, 4, "#f2efe6"); if (!back) R(5.5, 9, 1, 4, a.id === "creative-director" ? "#c9a227" : "#b3261e"); if (a.id === "technical-director") { R(2, 8, 2, 7, "#3d4a5c"); R(8, 8, 2, 7, "#3d4a5c"); } }
  if (a.dept === "art" && a.accessory < 0.5) R(2, 8, 8, 2, a.accessory < 0.25 ? "#ffe14d" : "#4fc3f7");          // шарф
  if (a.dept === "prod") R(5, 9, 2, 3, "#2bd47a");                                                                 // бейдж на шнурке
  if (a.dept === "qa") { R(2, 9, 1, 5, "#ffd54f"); R(9, 9, 1, 5, "#ffd54f"); }                                     // светоотражающий жилет
  // руки
  const typing = a.doing === "work";
  const waveL = vr ? Math.sin(t * 9) : 0, waveR = vr ? Math.sin(t * 9 + 2) : 0;
  if (vr) { R(0, 5 - Math.round(waveL * 3), 2, 5, shirt); R(0, 4 - Math.round(waveL * 3), 2, 2, a.skin); R(10, 5 - Math.round(waveR * 3), 2, 5, shirt); R(10, 4 - Math.round(waveR * 3), 2, 2, a.skin); }
  else if (typing) { const k = Math.sin(t * 22) > 0 ? 1 : 0; R(1, 8, 2, 4, shirt); R(9, 8, 2, 4, shirt); R(1, 11 - k, 2, 2, a.skin); R(9, 11 - (1 - k), 2, 2, a.skin); }
  else { const sw = Math.round(step); R(0, 9 + sw, 2, 5, shirt); R(10, 9 - sw, 2, 5, shirt); R(0, 13 + sw, 2, 2, a.skin); R(10, 13 - sw, 2, 2, a.skin); }
  if (a.id === "producer" && !typing && !back) { R(9, 10, 4, 5, "#222"); R(10, 11, 2, 3, "#4fc3f7"); }       // планшет
  if (a.doing === "coffee") { R(10, 11, 3, 3, "#fff"); R(11, 12, 1, 1, "#6d4c41"); }
  // голова
  const hy = 1 + nod;
  R(2, hy, 8, 7, a.skin);
  const hair = a.hair;
  if (back) { R(2, hy, 8, 6, hair); }
  else {
    switch (a.hairStyle) {
      case 0: R(2, hy - 1, 8, 3, hair); break;
      case 1: R(1, hy - 1, 10, 3, hair); R(1, hy, 2, 7, hair); R(9, hy, 2, 7, hair); break;
      case 2: R(2, hy - 1, 8, 2, hair); R(4, hy - 3, 4, 2, hair); break;
      case 3: R(5, hy - 3, 2, 4, hair); R(3, hy, 6, 1, hair); break;
      case 4: R(2, hy - 1, 8, 2, hair); R(1, hy, 2, 3, hair); R(9, hy, 2, 3, hair); break;
      default: R(3, hy - 1, 6, 1, shade(a.skin, -0.1)); break;
    }
    const blink = t > a.blinkAt && t < a.blinkAt + 0.12;
    if (t > a.blinkAt + 0.12) a.blinkAt = t + rand(2, 6);
    const ex = a.facing === "left" ? -1 : a.facing === "right" ? 1 : 0;
    if (!vr) { R(4 + ex, hy + 3, 1, blink ? 0.5 : 1, "#1b1b1b"); R(7 + ex, hy + 3, 1, blink ? 0.5 : 1, "#1b1b1b"); R(5 + ex, hy + 5, 2, 1, shade(a.skin, -0.3)); }
    if (a.dept === "dev" && a.accessory > 0.3) { R(3 + ex, hy + 2, 3, 3, "rgba(0,0,0,0)"); c.strokeStyle = "#111"; c.lineWidth = s; c.strokeRect(x + (3.5 + ex) * s, y + (hy + 2.5 + bob) * s, 2 * s, 2 * s); c.strokeRect(x + (6.5 + ex) * s, y + (hy + 2.5 + bob) * s, 2 * s, 2 * s); }
  }
  // головные уборы и аксессуары
  if (a.dept === "art" && a.accessory >= 0.5 && a.accessory < 0.8) { R(2, hy - 2, 8, 2, "#c62828"); R(6, hy - 3, 3, 1, "#c62828"); }          // берет
  if (a.dept === "dev" || (a.dept === "eng" && a.accessory > 0.5)) { R(1, hy - 1, 10, 1, "#222"); R(0, hy + 2, 2, 4, "#333"); R(10, hy + 2, 2, 4, "#333"); }   // наушники
  if (a.dept === "audio") { R(1, hy - 2, 10, 1, "#444"); R(-1, hy + 1, 3, 5, "#111"); R(10, hy + 1, 3, 5, "#111"); R(-1, hy + 2, 1, 3, "#ffb74d"); }  // студийные
  if (a.dept === "qa" || vr) { R(1, hy + 2, 10, 3, "#151515"); R(2, hy + 2, 8, 1, "#3a3a3a"); R(1, hy, 1, 3, "#151515"); R(10, hy, 1, 3, "#151515"); if (!back) R(3, hy + 3, 6, 1, "#4fc3f7"); }  // VR-шлем
}

// ---------------------------------------------------------------- эффекты
const particles = [];   // {x,y,vx,vy,text,color,until}
const packets = [];     // {from,to,t0,dur,color}
function sendPacket(a, b) { packets.push({ a, b, t0: now(), dur: 1.4, color: DEPARTMENTS[a.dept].color }); }
const GLYPHS = { dev: ["{", "}", ";", "</>", "01", "=>"], eng: ["::", "fn", "{}", "⚙"], art: ["✦", "✎", "◆", "★"], audio: ["♪", "♫", "~"], qa: ["✓", "🐞", "!"], prod: ["%", "$", "📈"], dir: ["!", "✦", "→"] };

// ====================================================================== состояние, сеть
const agents = []; const byId = {};
let demo = false, liveOk = false, logFound = false;
const seenEvents = new Map(); let firstPoll = true;
let lastCommit = null;
let serverDone = {};

async function loadRoster() {
  let list = [];
  try { const r = await fetch("api/agents"); if (r.ok) list = (await r.json()).agents; } catch (e) { /* без сервера — по ROLES */ }
  if (!list.length) list = Object.keys(ROLES).map(id => ({ id, name: id, description: "", model: "" }));
  const known = Object.keys(ROLES).filter(id => list.some(a => a.id === id));
  const extra = list.filter(a => !ROLES[a.id]).map(a => a.id);
  const ids = known.concat(extra);
  buildMap(); buildFurniture(ids); renderStatic();
  ids.forEach((id, i) => { const a = new Agent(id, list.find(l => l.id === id), i); agents.push(a); byId[id] = a; });
  document.getElementById("totalCount").textContent = agents.length;
}

async function poll() {
  try {
    const r = await fetch("api/state", { cache: "no-store" });
    if (!r.ok) throw new Error(r.status);
    const s = await r.json();
    liveOk = true; logFound = s.logFound; serverDone = s.done || {};
    // события хуков: новые — в действие, первые (история) — только в счётчики
    const counts = new Map();
    for (const e of s.events) {
      const key = `${e.ts}|${e.kind}|${e.agent}|${e.session}`;
      const n = (counts.get(key) || 0) + 1; counts.set(key, n);
      const k2 = key + "#" + n;
      if (seenEvents.has(k2)) continue;
      seenEvents.set(k2, true);
      if (firstPoll) continue;
      const a = byId[e.agent];
      if (!a) { addChat(null, `внештатный агент <b>${escapeHtml(e.agent)}</b> ${e.kind === "invoked" ? "начал работу" : "закончил"}`, "sys"); continue; }
      if (e.kind === "invoked") a.beginTask(a.defaultTask, "live"); else a.finishTask("live");
    }
    if (firstPoll) {
      for (const id of s.active) if (byId[id]) byId[id].beginTask(byId[id].defaultTask, "live");
      addChat(null, s.logFound ? `Подключено к журналу хуков Claude Code. Активных агентов: ${s.active.length}.` : "Журнал хуков пока пуст — запустите агента в Claude Code или включите демо-режим.", "sys");
    }
    for (const a of agents) a.serverDone = serverDone[a.id] || 0;
    renderGit(s.git);
    firstPoll = false;
  } catch (e) {
    liveOk = false;
    if (firstPoll) { firstPoll = false; addChat(null, "Сервер не отвечает — запущен автономный демо-режим.", "sys"); setDemo(true); }
  }
  updateBadges();
}

function renderGit(g) {
  if (!g) return;
  document.getElementById("branch").textContent = g.branch ? `· ${g.branch}` : "";
  const ul = document.getElementById("commits"); ul.innerHTML = "";
  for (const c of g.commits.slice(0, 8)) { const li = document.createElement("li"); li.innerHTML = `<code>${c.hash}</code> ${escapeHtml(c.subject)}`; li.title = `${c.author} · ${c.date}`; ul.appendChild(li); }
  const ch = document.getElementById("changed"); ch.innerHTML = "";
  for (const f of g.changed.slice(0, 12)) { const li = document.createElement("li"); li.textContent = f; ch.appendChild(li); }
  if (!g.changed.length) ch.innerHTML = "<li>рабочая копия чистая</li>";
  const head = g.commits[0];
  if (head && lastCommit && head.hash !== lastCommit) {
    addChat(byId["devops-engineer"] || null, `новый коммит <code>${head.hash}</code>: ${escapeHtml(head.subject)}`, "git");
    const qa = byId["qa-tester"]; if (qa && !qa.busy) qa.say("Новый коммит! Проверяю билд...", 3);
  }
  if (head) lastCommit = head.hash;
  document.getElementById("projStatus").textContent = head ? `${g.branch || "main"} · ${head.hash} · ${g.changed.length} изм.` : "—";
}

function escapeHtml(s) { return String(s).replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch])); }

function addChat(a, html, kind) {
  const ul = document.getElementById("chat");
  const li = document.createElement("li"); if (kind) li.className = kind;
  const time = new Date().toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  const who = a ? `<span class="who" style="color:${DEPARTMENTS[a.dept].color}">${escapeHtml(a.title)}</span>: ` : "";
  li.innerHTML = `<span class="t">${time}</span>${who}${html}`;
  ul.prepend(li);
  while (ul.children.length > 80) ul.lastChild.remove();
}

function updateBadges() {
  document.getElementById("activeCount").textContent = agents.filter(a => a.busy).length;
  const b = document.getElementById("liveBadge"), t = document.getElementById("liveText");
  b.className = "pill " + (demo ? "demo" : liveOk ? "on" : "off");
  t.textContent = demo ? (liveOk ? "демо + live" : "демо") : liveOk ? (logFound ? "Claude Code: live" : "live, журнал пуст") : "нет связи";
}

// ---------------------------------------------------------------- демо-режим (аврал)
let demoTimer = 0;
function setDemo(on) {
  demo = on;
  document.getElementById("demoBtn").classList.toggle("active", on);
  if (on) { addChat(null, "⚡ Аврал! Демо-режим включён — вся студия в деле.", "sys"); for (let i = 0; i < 8; i++) setTimeout(demoStart, i * 400); }
  else { addChat(null, "Демо-режим выключен.", "sys"); for (const a of agents) if (a.busy && a.demoBusy) { a.demoBusy = false; a.finishTask("demo"); } }
  updateBadges();
}
function demoStart(forced) {
  const free = agents.filter(a => !a.busy && !a.visiting);
  if (!free.length) return;
  const a = forced && !forced.busy ? forced : pick(free);
  a.demoBusy = true; a.beginTask(a.defaultTask, "demo"); a.demoEnd = now() + rand(7, 16);
}
function demoTick(t) {
  if (!demo || t < demoTimer) return;
  demoTimer = t + rand(1.2, 2.4);
  const busy = agents.filter(a => a.busy && a.demoBusy);
  for (const a of busy) if (t > a.demoEnd) {
    a.demoBusy = false;
    const to = a.finishTask("demo");
    if (to && Math.random() < 0.55) setTimeout(() => { if (demo) demoStart(to); }, 6500);
    updateBadges(); return;
  }
  if (busy.length < 16) demoStart();
  updateBadges();
}

// ====================================================================== камера, ввод, отрисовка
const view = document.getElementById("view"), vctx = view.getContext("2d");
const world = document.createElement("canvas"); world.width = WPX; world.height = HPX; const wctx = world.getContext("2d");
const cam = { s: 1, x: 0, y: 0, follow: null };
let R3 = null;   // 3D-мир (office3d.js); null — запасной 2D пиксель-арт
let hover = null, dragging = null;

function resize() {
  const r = view.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
  view.width = Math.round(r.width * dpr); view.height = Math.round(r.height * dpr);
  fit();
}
function fit() {
  const dpr = window.devicePixelRatio || 1, vw = view.width / dpr, vh = view.height / dpr;
  cam.s = Math.min(vw / WPX, vh / HPX); cam.x = (vw - WPX * cam.s) / 2; cam.y = (vh - HPX * cam.s) / 2; cam.follow = null;
}
const toWorld = (sx, sy) => ({ x: (sx - cam.x) / cam.s, y: (sy - cam.y) / cam.s });
function agentAt(wx, wy) {
  let best = null, bd = 1e9;
  for (const a of agents) { const d = Math.hypot(a.x - wx, a.y - 10 - wy); if (d < 14 && d < bd) { bd = d; best = a; } }
  return best;
}
view.addEventListener("wheel", e => {
  e.preventDefault();
  const r = view.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top, w = toWorld(mx, my);
  const dpr = window.devicePixelRatio || 1, minS = Math.min(view.width / dpr / WPX, view.height / dpr / HPX) * 0.9;
  cam.s = Math.max(minS, Math.min(5, cam.s * (e.deltaY < 0 ? 1.15 : 1 / 1.15)));
  cam.x = mx - w.x * cam.s; cam.y = my - w.y * cam.s;
}, { passive: false });
view.addEventListener("mousedown", e => { dragging = { x: e.clientX, y: e.clientY, cx: cam.x, cy: cam.y, moved: false }; });
window.addEventListener("mousemove", e => {
  if (R3) return;
  const r = view.getBoundingClientRect(), w = toWorld(e.clientX - r.left, e.clientY - r.top);
  hover = agentAt(w.x, w.y); view.style.cursor = hover ? "pointer" : dragging ? "grabbing" : "grab";
  if (dragging) {
    const dx = e.clientX - dragging.x, dy = e.clientY - dragging.y;
    if (Math.abs(dx) + Math.abs(dy) > 4) { dragging.moved = true; cam.follow = null; }
    cam.x = dragging.cx + dx; cam.y = dragging.cy + dy;
  }
});
window.addEventListener("mouseup", e => {
  if (R3) return;
  if (dragging && !dragging.moved && e.target === view) {
    const r = view.getBoundingClientRect(), w = toWorld(e.clientX - r.left, e.clientY - r.top), a = agentAt(w.x, w.y);
    if (a) openDossier(a);
  }
  dragging = null;
});
window.addEventListener("resize", resize);
document.getElementById("fitBtn").onclick = () => (R3 ? (R3.fps ? R3.setMode("overview") : R3.resetCamera()) : fit());
document.getElementById("demoBtn").onclick = () => setDemo(!demo);

// ---------------------------------------------------------------- досье
let dossierAgent = null;
function openDossier(a) {
  dossierAgent = a;
  const d = DEPARTMENTS[a.dept];
  document.getElementById("dDept").textContent = d.name; document.getElementById("dDept").style.color = d.color;
  document.getElementById("dName").textContent = a.id;
  document.getElementById("dRole").textContent = a.title;
  document.getElementById("dDesc").textContent = a.meta.description || "";
  document.getElementById("dModel").textContent = a.meta.model || "—";
  refreshDossier();
  const pc = document.getElementById("dPortrait").getContext("2d"); pc.imageSmoothingEnabled = false;
  document.getElementById("dossier").classList.remove("hidden");
}
function refreshDossier() {
  const a = dossierAgent; if (!a) return;
  document.getElementById("dTask").textContent = a.busy ? `🔥 ${a.task}` : ({ coffee: "☕ пьёт кофе", sofa: "🛋 отдыхает", vr: "🥽 тестирует VR", chat: "💬 болтает у кулера", arcade: "🕹 играет в аркаду", foos: "⚽ настольный футбол", desk: "💻 за своим столом", talk: "🗣 обсуждает задачу", inspect: "🔍 проверяет работу" }[a.doing] || "🚶 гуляет по офису");
  document.getElementById("dDone").textContent = `${(a.serverDone || 0) + a.done} (в журнале хуков: ${a.serverDone || 0})`;
  const pc = document.getElementById("dPortrait").getContext("2d");
  pc.clearRect(0, 0, 96, 120);
  const fake = Object.assign(Object.create(Object.getPrototypeOf(a)), a, { x: 0, y: 0, path: [], facing: "down", doing: a.doing === "vr" ? "vr" : null, nod: 0 });
  pc.save(); pc.scale(6, 6); drawAgent(pc, fake, now(), 8, 19, 1); pc.restore();
}
document.getElementById("dClose").onclick = () => { document.getElementById("dossier").classList.add("hidden"); dossierAgent = null; };
document.getElementById("dossier").addEventListener("click", e => { if (e.target.id === "dossier") document.getElementById("dClose").onclick(); });
document.getElementById("dCall").onclick = () => { if (dossierAgent) { dossierAgent.callToDirector(); cam.follow = dossierAgent; document.getElementById("dClose").onclick(); } };
document.getElementById("dFollow").onclick = () => { if (dossierAgent) { if (R3 && R3.fps) R3.setMode("overview"); cam.follow = dossierAgent; if (!R3) cam.s = Math.max(cam.s, 2.4); document.getElementById("dClose").onclick(); } };
window.addEventListener("keydown", e => { if (e.key === "Escape") document.getElementById("dClose").onclick(); });

// ---------------------------------------------------------------- кадр
function drawDynamic(c, t) {
  // мигающие лампочки серверов, кофемашина, аркада
  for (const f of animated) {
    const x = f.x * T, y = f.y * T;
    if (f.t === "rack") for (let j = 0; j < 7; j++) for (let i = 0; i < 3; i++) {
      const on = Math.sin(t * (3 + i + j) + f.x * 1.3 + j * 7) > 0.2;
      P(c, x + 5 + i * 6, y + 4 + j * 5, 2, 1, on ? (i === 2 ? "#ff5252" : "#69f0ae") : "#1b3a2b");
    } else if (f.t === "coffee") {
      for (let i = 0; i < 3; i++) { const k = (t * 0.8 + i / 3) % 1; P(c, x + 12 + Math.sin(t * 3 + i) * 2, y - 8 - k * 14, 2, 2, `rgba(255,255,255,${0.6 * (1 - k)})`); }
    } else if (f.t === "arcade") {
      P(c, x + 6, y - 6, f.w * T - 12, 14, `hsl(${(t * 60) % 360},70%,45%)`);
      P(c, x + 8 + ((t * 20) % 14), y - 2, 3, 3, "#fff");
    }
  }
  // экраны занятых агентов светятся ярче
  for (const a of agents) if (a.busy && a.atSeat) { const s = a.seat; P(c, s.x * T - 10, s.y * T - 18, 36, 3, `rgba(${a.dept === "art" ? "255,111,174" : "79,195,247"},${0.4 + 0.3 * Math.sin(t * 6)})`); }
  // агенты по глубине
  const order = agents.slice().sort((p, q) => p.y - q.y);
  for (const a of order) drawAgent(c, a, t);
  // пакеты данных
  for (let i = packets.length - 1; i >= 0; i--) {
    const p = packets[i], k = (t - p.t0) / p.dur;
    if (k >= 1) { packets.splice(i, 1); continue; }
    const x = p.a.x + (p.b.x - p.a.x) * k, y = p.a.y - 20 + (p.b.y - p.a.y) * k - Math.sin(k * Math.PI) * 60;
    for (let j = 0; j < 5; j++) { const kk = Math.max(0, k - j * 0.03); const tx = p.a.x + (p.b.x - p.a.x) * kk, ty = p.a.y - 20 + (p.b.y - p.a.y) * kk - Math.sin(kk * Math.PI) * 60; P(c, tx - 2 + j * 0.4, ty - 2, 4 - j * 0.6, 4 - j * 0.6, p.color); }
    P(c, x - 3, y - 3, 6, 6, "#fff"); P(c, x - 2, y - 2, 4, 4, p.color);
  }
}

function roundRect(c, x, y, w, h, r) { c.beginPath(); c.moveTo(x + r, y); c.arcTo(x + w, y, x + w, y + h, r); c.arcTo(x + w, y + h, x, y + h, r); c.arcTo(x, y + h, x, y, r); c.arcTo(x, y, x + w, y, r); c.closePath(); }
function bubble(c, sx, sy, text, kind, color) {
  c.font = "600 12px 'Segoe UI', sans-serif";
  const maxW = 190, words = text.split(" "), lines = []; let line = "";
  for (const w of words) { const test = line ? line + " " + w : w; if (c.measureText(test).width > maxW && line) { lines.push(line); line = w; } else line = test; }
  lines.push(line);
  const w = Math.max(...lines.map(l => c.measureText(l).width)) + 14, h = lines.length * 15 + 8;
  const x = Math.round(sx - w / 2), y = Math.round(sy - h - 10);
  c.fillStyle = kind === "think" ? "rgba(240,240,255,0.92)" : "#fffdf5"; c.strokeStyle = color || "#222"; c.lineWidth = 2;
  roundRect(c, x, y, w, h, kind === "think" ? 10 : 6); c.fill(); c.stroke();
  if (kind === "think") { c.beginPath(); c.arc(sx - 4, y + h + 5, 3, 0, 7); c.fill(); c.stroke(); c.beginPath(); c.arc(sx - 8, y + h + 11, 2, 0, 7); c.fill(); }
  else { c.beginPath(); c.moveTo(sx - 5, y + h); c.lineTo(sx, y + h + 7); c.lineTo(sx + 5, y + h); c.fillStyle = "#fffdf5"; c.fill(); c.stroke(); c.fillRect(sx - 4, y + h - 2, 8, 3); }
  c.fillStyle = "#1d1b26";
  lines.forEach((l, i) => c.fillText(l, x + 7, y + 17 + i * 15));
}

// экранная точка: (wx, wy) — пиксели карты, h — высота над полом в «пикселях» (в 2D просто сдвиг вверх)
function screenOf(wx, wy, h = 0) { return R3 ? R3.project(wx, wy, h) : [cam.x + wx * cam.s, cam.y + (wy - h) * cam.s]; }
const zoomK = () => (R3 ? R3.zoomK() : cam.s);

function drawOverlay(c, t, dt) {
  const S = (wx, wy, h = 0) => screenOf(wx, wy, h) || [-9999, -9999];
  const zk = zoomK();
  // названия комнат
  c.font = `700 ${Math.max(10, Math.min(15, 6 * zk))}px 'Segoe UI', sans-serif`;
  const fps = !!(R3 && R3.fps);
  for (const r of ROOMS) {
    if (fps) break;                                            // от первого лица таблички комнат не нужны
    const [sx, sy] = R3 ? S(r.x0 * T + 26, r.y0 * T + 30, 40) : S(r.x0 * T + 20, r.y0 * T + 22);
    const col = DEPARTMENTS[r.id] ? DEPARTMENTS[r.id].color : ROOM_COLORS[r.id];
    const label = ROOM_NAMES[r.id]; const w = c.measureText(label).width + 12;
    c.fillStyle = "rgba(15,13,22,0.72)"; roundRect(c, sx - 6, sy - 13, w, 18, 5); c.fill();
    c.fillStyle = col; c.fillText(label, sx, sy);
  }
  if (!R3) { const [sx, sy] = S(62 * T, 51.6 * T); c.fillStyle = "rgba(15,13,22,0.7)"; c.font = "700 12px 'Segoe UI'"; c.fillText("🅿 GRABZ STUDIO · вход", sx, sy); }
  const vis = (a, d) => !R3 || R3.canSee(a.x, a.y, d);
  // частицы кода и вдохновения
  for (const a of agents) if (a.busy && a.doing === "work" && Math.random() < dt * 3.2 && vis(a, 18)) {
    const g = GLYPHS[a.dept] || GLYPHS.dev;
    particles.push({ x: a.x + rand(-8, 8), y: a.y - (R3 ? 10 : 0), h: 26, vx: rand(-10, 10), vh: rand(18, 30), text: pick(g), color: DEPARTMENTS[a.dept].color, t0: t, life: rand(1, 1.6) });
  }
  c.textAlign = "center";
  for (let i = particles.length - 1; i >= 0; i--) {
    const p = particles[i], k = (t - p.t0) / p.life;
    if (k >= 1) { particles.splice(i, 1); continue; }
    p.x += p.vx * dt; p.h += p.vh * dt;
    const [sx, sy] = S(p.x, p.y, p.h);
    c.globalAlpha = 1 - k; c.fillStyle = p.color; c.font = `700 ${Math.round(Math.min(22, 9 + 3 * zk))}px Consolas, monospace`; c.fillText(p.text, sx, sy);
  }
  c.globalAlpha = 1;
  // бейджи задач, имена, реплики
  const showAll = zk >= 2.2;
  for (const a of agents) {
    if (!vis(a, 25)) continue;
    const [sx, sy] = S(a.x, a.y, 22);
    const col = DEPARTMENTS[a.dept].color;
    if (a.busy) {
      c.font = "700 11px 'Segoe UI', sans-serif";
      const label = "🔥 " + a.task, w = c.measureText(label).width + 12;
      c.fillStyle = col; roundRect(c, sx - w / 2, sy - 20, w, 16, 8); c.fill();
      c.fillStyle = "#14121c"; c.fillText(label, sx, sy - 8);
    }
    if ((showAll && vis(a, 10)) || a === hover || a === cam.follow) {
      c.font = "600 10px 'Segoe UI', sans-serif";
      const [nx, ny] = fps ? [sx, sy + 2] : R3 ? S(a.x, a.y, 0) : S(a.x, a.y + 6);
      const w = c.measureText(a.title).width + 8;
      c.fillStyle = "rgba(15,13,22,0.78)"; roundRect(c, nx - w / 2, ny, w, 13, 4); c.fill();
      c.fillStyle = col; c.fillText(a.title, nx, ny + 10);
    }
  }
  c.textAlign = "left";
  for (const a of agents) {
    if (!vis(a, 25)) continue;
    const [sx, sy0] = S(a.x, a.y, 22); const sy = sy0 - (a.busy ? 24 : 0);
    if (a.bubble && t < a.bubble.until) bubble(c, sx, sy - (a.busy ? 6 : 0), a.bubble.text, "say", DEPARTMENTS[a.dept].color);
    else if (a.thought && t < a.thought.until) bubble(c, sx, sy, a.thought.text, "think", "#9a93b8");
  }
  if (hover && !R3) {
    const [sx, sy] = S(hover.x, hover.y - 10);
    c.strokeStyle = "#fff"; c.lineWidth = 2; c.beginPath(); c.ellipse(sx, sy + 12 * cam.s, 9 * cam.s, 4 * cam.s, 0, 0, 7); c.stroke();
  }
}

let last = now(), dossierRefresh = 0;
function frame() {
  const t = now(), dt = Math.min(0.05, t - last); last = t;
  for (const a of agents) a.update(dt, t);
  demoTick(t);
  const dpr = window.devicePixelRatio || 1;
  vctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  if (R3) {
    if (cam.follow) R3.follow(cam.follow, dt);
    R3.render(t, dt);
    vctx.clearRect(0, 0, view.width, view.height);
  } else {
    if (cam.follow) {
      const vw = view.width / dpr, vh = view.height / dpr;
      cam.x += (vw / 2 - cam.follow.x * cam.s - cam.x) * Math.min(1, dt * 4);
      cam.y += (vh / 2 - cam.follow.y * cam.s - cam.y) * Math.min(1, dt * 4);
    }
    wctx.drawImage(bg, 0, 0);
    drawDynamic(wctx, t);
    vctx.fillStyle = "#14121c"; vctx.fillRect(0, 0, view.width, view.height);
    vctx.imageSmoothingEnabled = false;
    vctx.drawImage(world, cam.x, cam.y, WPX * cam.s, HPX * cam.s);
  }
  drawOverlay(vctx, t, dt);
  if (dossierAgent && t > dossierRefresh) { refreshDossier(); dossierRefresh = t + 0.25; }
  document.getElementById("clock").textContent = new Date().toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
  requestAnimationFrame(frame);
}

(async function main() {
  await loadRoster();
  if (window.THREE && THREE.OrbitControls) {
    document.body.classList.add("mode3d");
    try { R3 = initOffice3D(document.getElementById("gl")); }
    catch (e) { console.error("3D не запустился, остаёмся в 2D", e); R3 = null; document.body.classList.remove("mode3d"); }
  }
  resize();
  addChat(null, `Добро пожаловать в GRABZ STUDIO! В офисе ${agents.length} агентов.`, "sys");
  await poll();
  setInterval(poll, 1000);
  requestAnimationFrame(frame);
})();
