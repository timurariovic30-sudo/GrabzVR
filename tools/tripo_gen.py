#!/usr/bin/env python3
"""
tripo_gen.py - generate stylized low-poly VR models with the Tripo3D API (v3).

Pipeline:  prompt / reference image  ->  text-to-model | image-to-model
           ->  mesh/decimate (smart retopology to low poly, textures baked)
           ->  download .glb into Assets/Art/GeneratedModels/

Only the Python standard library is used (no pip install needed).
The API key is read from the TRIPO_API_KEY environment variable or from the
.env file in the project root (that file is git-ignored - never commit it).

Examples:
  python tools/tripo_gen.py "low poly wooden crate" Prop_Crate
  python tools/tripo_gen.py "cartoon guard robot" NPC_Guard --kind character
  python tools/tripo_gen.py --image refs/lamp.png Prop_Lamp
  python tools/tripo_gen.py --balance
"""
import argparse
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

API = "https://openapi.tripo3d.ai/v3"
ROOT = Path(__file__).resolve().parent.parent          # Unity project root
OUT_DIR = ROOT / "Assets" / "Art" / "GeneratedModels"
FACE_LIMITS = {"prop": 3500, "character": 5000}
STYLE_SUFFIX = ", stylized low poly game asset, simple flat hand-painted colors, chunky readable shapes"
POLL_SECONDS = 3
TIMEOUT_SECONDS = 15 * 60


# ---------------------------------------------------------------- key / http
def load_key() -> str:
    key = os.environ.get("TRIPO_API_KEY", "").strip()
    env = ROOT / ".env"
    if not key and env.exists():
        for line in env.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line.startswith("TRIPO_API_KEY="):
                key = line.split("=", 1)[1].strip().strip('"').strip("'")
    if not key:
        sys.exit("ERROR: TRIPO_API_KEY not found (set env var or put it in .env in project root)")
    return key


class Tripo:
    def __init__(self, key: str):
        self.key = key

    def _req(self, method, path, body=None, headers=None, raw=None):
        h = {"Authorization": f"Bearer {self.key}"}
        data = raw
        if body is not None:
            data = json.dumps(body).encode()
            h["Content-Type"] = "application/json"
        h.update(headers or {})
        req = urllib.request.Request(API + path, data=data, method=method, headers=h)
        for attempt in range(4):
            try:
                with urllib.request.urlopen(req, timeout=60) as r:
                    payload = json.loads(r.read().decode())
                break
            except urllib.error.HTTPError as e:
                text = e.read().decode(errors="replace")
                if e.code in (429, 500, 502, 503, 504) and attempt < 3:
                    time.sleep(2 * (attempt + 1))
                    continue
                if '"code":2010' in text.replace(" ", ""):
                    sys.exit("ERROR: not enough Tripo API credits. Top up at https://platform.tripo3d.ai "
                             "(API credits are separate from the website's free credits).")
                sys.exit(f"ERROR: HTTP {e.code} on {method} {path}: {text}")
            except urllib.error.URLError as e:
                if attempt < 3:
                    time.sleep(2 * (attempt + 1))
                    continue
                sys.exit(f"ERROR: network problem on {method} {path}: {e}")
        if payload.get("code", 0) != 0:
            sys.exit(f"ERROR: API code {payload.get('code')}: {payload.get('message') or payload}")
        return payload.get("data", {})

    def balance(self):
        return self._req("GET", "/account/balance")

    def upload(self, image: Path) -> str:
        boundary = uuid.uuid4().hex
        ctype = mimetypes.guess_type(image.name)[0] or "application/octet-stream"
        body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{image.name}\"\r\n"
                f"Content-Type: {ctype}\r\n\r\n").encode() + image.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
        data = self._req("POST", "/files", raw=body,
                         headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
        return data["file_token"]

    def create(self, path, body) -> str:
        return self._req("POST", path, body=body)["task_id"]

    def wait(self, task_id, label):
        start = time.time()
        last = None
        while True:
            t = self._req("GET", f"/tasks/{task_id}")
            status, progress = t.get("status"), t.get("progress", 0)
            if (status, progress) != last:
                print(f"  [{label}] {status} {progress}%", flush=True)
                last = (status, progress)
            if status == "success":
                return t
            if status in ("failed", "cancelled", "banned", "expired"):
                sys.exit(f"ERROR: {label} task {task_id} {status}: "
                         f"{t.get('error_code')} {t.get('error_message', '')}")
            if time.time() - start > TIMEOUT_SECONDS:
                sys.exit(f"ERROR: {label} task {task_id} timed out (last status {status})")
            time.sleep(POLL_SECONDS)


def download(url: str, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")      # Unity ignores .part until it is renamed
    with urllib.request.urlopen(url, timeout=120) as r, open(tmp, "wb") as f:
        while chunk := r.read(1 << 16):
            f.write(chunk)
    os.replace(tmp, dest)


# ---------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description="Generate a low-poly .glb with Tripo3D into Assets/Art/GeneratedModels")
    ap.add_argument("prompt", nargs="?", help="text prompt (omit when using --image)")
    ap.add_argument("name", nargs="?", help="output file name without extension, e.g. Hub_ModeTerminal")
    ap.add_argument("--image", help="path to a reference image (PNG/JPG/WebP) instead of a text prompt")
    ap.add_argument("--kind", choices=FACE_LIMITS, default="prop",
                    help="prop -> 3500 faces, character -> 5000 faces")
    ap.add_argument("--face-limit", type=int, help="override the face limit")
    ap.add_argument("--model", default="v3.1-20260211", help="Tripo generation model version")
    ap.add_argument("--no-style", action="store_true", help="do not append the low-poly style hint to the prompt")
    ap.add_argument("--balance", action="store_true", help="only print the account balance")
    a = ap.parse_args()

    tripo = Tripo(load_key())
    if a.balance:
        b = tripo.balance()
        print(f"Balance: {b.get('balance')} credits (frozen {b.get('frozen')})")
        return

    # with --image the single positional argument is the name
    if a.image and a.name is None:
        a.name, a.prompt = a.prompt, None
    if not a.name or not (a.prompt or a.image):
        ap.error("need: \"prompt\" Name   or   --image path Name")
    safe = "".join(c for c in a.name if c.isalnum() or c in "_-")
    dest = OUT_DIR / f"{safe}.glb"
    faces = a.face_limit or FACE_LIMITS[a.kind]

    b = tripo.balance()
    print(f"Tripo balance: {b.get('balance')} credits")

    common = {"model": a.model, "texture": True, "pbr": False, "texture_quality": "standard",
              "auto_size": True}
    t0 = time.time()
    if a.image:
        img = Path(a.image)
        if not img.is_file():
            sys.exit(f"ERROR: image not found: {img}")
        print(f"Uploading {img.name} ...")
        token = tripo.upload(img)
        gen_id = tripo.create("/generation/image-to-model", {"input": token, **common})
    else:
        prompt = a.prompt if a.no_style else a.prompt + STYLE_SUFFIX
        gen_id = tripo.create("/generation/text-to-model",
                              {"prompt": prompt[:1024], "negative_prompt": "realistic, photoreal, noisy, tiny details",
                               **common})
    print(f"1/2 generating model, task {gen_id}")
    gen = tripo.wait(gen_id, "generate")

    print(f"2/2 smart retopology to <= {faces} faces")
    low_id = tripo.create("/mesh/decimate", {"input": gen_id, "model": "v2.0", "face_limit": faces,
                                             "quad": False, "bake": True})
    low = tripo.wait(low_id, "low-poly")

    url = (low.get("output") or {}).get("model_url")
    if not url:
        sys.exit(f"ERROR: no model_url in result: {json.dumps(low)[:500]}")
    download(url, dest)          # URLs expire after ~5 min, so download right away
    spent = sum(t.get("credits_consumed") or 0 for t in (gen, low))
    print(f"DONE in {time.time() - t0:.0f}s -> {dest.relative_to(ROOT)} "
          f"({dest.stat().st_size / 1024:.0f} KB, credits used: {spent})")


if __name__ == "__main__":
    main()
