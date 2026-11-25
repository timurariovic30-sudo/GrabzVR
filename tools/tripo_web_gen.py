#!/usr/bin/env python3
"""
tripo_web_gen.py - generate a low-poly model on the Tripo Studio WEBSITE (your subscription credits)
and drop the .glb into Assets/Art/GeneratedModels/ (Unity then auto-applies the retro 128px style).

Uses Playwright with the real Chrome and a separate, persistent browser profile
(tools/.tripo_browser_profile, git-ignored). First run: a Chrome window opens - log in to Tripo
once by hand; the login is remembered for next runs.

    python tools/tripo_web_gen.py --login                       # just open the site to log in
    python tools/tripo_web_gen.py "wooden crate" Prop_Crate     # props: 3500 triangles
    python tools/tripo_web_gen.py "cartoon guard" NPC_Guard --kind character   # 5000 triangles
    python tools/tripo_web_gen.py --from-downloads Prop_Crate   # move newest Tripo .glb from Downloads

Website automation is fragile (labels may change) and may be against the site's terms - run it
at human pace, one model at a time. The paid-API route is tools/tripo_gen.py.
"""
import argparse
import re
import shutil
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "Assets" / "Art" / "GeneratedModels"
PROFILE = Path(__file__).resolve().parent / ".tripo_browser_profile"
URL = "https://studio.tripo3d.ai/ru/workspace/generate"
FACES = {"prop": 3500, "character": 5000}
STYLE = ", stylized low poly game asset, simple hand-painted texture, chunky shapes"

# labels in Russian UI | English UI
L = {
    "smart":    r"Умная сетка|Smart Mesh",
    "topology": r"^(Топология|Topology)$",
    "tri":      r"Треугольник|Triangle",
    "create":   r"^(Создать|Generate|Create)\b",
    "texture":  r"^(Текстура|Texture)\b",
    "export":   r"^(Экспорт|Export)$",
    "busy":     r"Генерация|Generating|В очереди|Queued",
}


def safe_name(n):
    return "".join(c for c in n if c.isalnum() or c in "_-")


def from_downloads(name):
    dl = Path.home() / "Downloads"
    files = sorted(dl.glob("*.glb"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not files:
        sys.exit(f"No .glb in {dl}")
    dest = OUT_DIR / f"{safe_name(name)}.glb"
    shutil.move(str(files[0]), dest)
    print(f"Moved {files[0].name} -> {dest.relative_to(ROOT)}")


def wait_not_busy(page, what, timeout_s=600):
    """Waits until the viewport no longer shows 'Generating...' / 'Queued'."""
    t0 = time.time()
    time.sleep(4)
    busy = page.locator("main, body").get_by_text(re.compile(L["busy"]))
    while time.time() - t0 < timeout_s:
        visible = [b for b in busy.all() if b.is_visible()]
        if not visible:
            print(f"  {what}: done ({time.time() - t0:.0f}s)")
            return
        print(f"  {what}: working... {time.time() - t0:.0f}s", flush=True)
        time.sleep(5)
    sys.exit(f"Timeout waiting for {what}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("prompt", nargs="?")
    ap.add_argument("name", nargs="?")
    ap.add_argument("--kind", choices=FACES, default="prop")
    ap.add_argument("--faces", type=int)
    ap.add_argument("--texture-res", default="512", choices=["512", "1k", "2k"])
    ap.add_argument("--no-style", action="store_true")
    ap.add_argument("--login", action="store_true", help="only open the site so you can log in")
    ap.add_argument("--from-downloads", metavar="NAME", help="move newest .glb from Downloads and exit")
    a = ap.parse_args()

    if a.from_downloads:
        return from_downloads(a.from_downloads)
    if not a.login and not (a.prompt and a.name):
        ap.error('need: "prompt" Name')

    from playwright.sync_api import sync_playwright

    with sync_playwright() as pw:
        ctx = pw.chromium.launch_persistent_context(
            str(PROFILE), channel="chrome", headless=False, accept_downloads=True,
            viewport={"width": 1500, "height": 900}, locale="ru-RU")
        page = ctx.pages[0] if ctx.pages else ctx.new_page()
        page.goto(URL, wait_until="domcontentloaded")

        if a.login:
            print("Log in to Tripo in the opened window, then close the window.")
            page.wait_for_event("close", timeout=0)
            return

        prompt_box = page.locator("textarea")
        try:
            prompt_box.first.wait_for(state="attached", timeout=30000)
        except Exception:
            sys.exit("Prompt box not found - are you logged in? Run with --login first.")
        if page.get_by_text(re.compile(r"^(Войти|Log ?in|Sign ?in)$")).first.is_visible():
            sys.exit("Not logged in. Run: python tools/tripo_web_gen.py --login")

        # 1) Smart Mesh (low poly) mode + text input
        page.get_by_role("button", name=re.compile(L["smart"])).first.click()
        if not prompt_box.first.is_visible():
            # text mode is the last (pencil) icon of the input-type switcher above the prompt box
            switch = page.locator("textarea").locator("xpath=ancestor::div[.//button][3]").locator("button").nth(3)
            switch.click()
        # 2) topology: triangles + face limit
        faces = str(a.faces or FACES[a.kind])
        page.get_by_text(re.compile(L["topology"])).first.click()
        page.get_by_text(re.compile(L["tri"])).first.click()
        num = page.locator("input[type=number]:visible").last
        num.fill(faces)
        num.press("Tab")
        page.keyboard.press("Escape")
        # 3) prompt + create
        text = a.prompt if a.no_style else a.prompt + STYLE
        prompt_box.first.fill(text[:1000])
        page.get_by_role("button", name=re.compile(L["create"])).last.click()
        print(f"Generating '{a.name}' ({faces} tris)...")
        page.get_by_role("button", name=re.compile(L["texture"])).first.wait_for(timeout=600000)
        wait_not_busy(page, "mesh")
        # 4) texture
        page.get_by_role("button", name=re.compile(L["texture"])).first.click()
        wait_not_busy(page, "texture")
        # 5) export GLB
        page.get_by_role("button", name=re.compile(L["export"])).last.click()
        page.locator("input[type=text]:visible").last.fill(safe_name(a.name))
        page.get_by_role("button", name=re.compile(r"^(4k|2k|1k|512)\b")).first.click()
        page.get_by_text(a.texture_res, exact=True).first.click()
        with page.expect_download(timeout=180000) as dl:
            page.get_by_role("button", name=re.compile(L["export"])).last.click()
        dest = OUT_DIR / f"{safe_name(a.name)}.glb"
        dest.parent.mkdir(parents=True, exist_ok=True)
        dl.value.save_as(str(dest))
        print(f"DONE -> {dest.relative_to(ROOT)} ({dest.stat().st_size // 1024} KB). "
              f"Unity will build {dest.stem}/{dest.stem}.prefab automatically.")
        ctx.close()


if __name__ == "__main__":
    main()
