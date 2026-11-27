#!/usr/bin/env python3
"""GRABZ STUDIO — живой офис студии Claude Code Game Studios.

Локальный сервер (только 127.0.0.1): раздаёт страницу симулятора и API, которое раз в секунду опрашивает браузер.
  GET /api/agents  — 49 агентов из .claude/agents/*.md (имя, описание, модель)
  GET /api/state   — события хуков (production/session-logs/agent-audit.log: Agent invoked / completed),
                     активные агенты, последние коммиты git и изменённые файлы
Запуск: python server.py [--port 8765] [--no-browser]   (или start_studio.bat)
Только стандартная библиотека Python 3.8+.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import threading
import time
import webbrowser
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
AGENTS_DIR = os.path.join(ROOT, ".claude", "agents")
AUDIT_LOG = os.path.join(ROOT, "production", "session-logs", "agent-audit.log")

LINE_RE = re.compile(r"^(?P<ts>[^|]+?)\s*\|\s*(?P<session>[^|]*?)\s*\|\s*Agent (?P<kind>invoked|completed):\s*(?P<agent>\S+)")

_cache = {"git": (0.0, None)}
_lock = threading.Lock()


def read_agents():
    agents = []
    if not os.path.isdir(AGENTS_DIR):
        return agents
    for fn in sorted(os.listdir(AGENTS_DIR)):
        if not fn.endswith(".md"):
            continue
        meta = {"id": fn[:-3], "name": fn[:-3], "description": "", "model": ""}
        try:
            with open(os.path.join(AGENTS_DIR, fn), encoding="utf-8") as f:
                text = f.read(4000)
            m = re.match(r"^---\s*\n(.*?)\n---", text, re.S)
            if m:
                for line in m.group(1).splitlines():
                    k, _, v = line.partition(":")
                    k, v = k.strip(), v.strip().strip('"')
                    if k in ("name", "description", "model"):
                        meta[k] = v
        except OSError:
            pass
        agents.append(meta)
    return agents


def read_events(limit=400):
    """Последние события из журнала хуков. Возвращает (events, active, done_counts)."""
    events, active, done = [], {}, {}
    if not os.path.isfile(AUDIT_LOG):
        return events, [], done
    try:
        with open(AUDIT_LOG, encoding="utf-8", errors="replace") as f:
            lines = f.readlines()[-4000:]
    except OSError:
        return events, [], done
    for i, line in enumerate(lines):
        m = LINE_RE.match(line.strip())
        if not m:
            continue
        agent, kind, ts = m.group("agent"), m.group("kind"), m.group("ts").strip()
        events.append({"seq": i, "ts": ts, "kind": kind, "agent": agent, "session": m.group("session")[:8]})
        if kind == "invoked":
            active[agent] = active.get(agent, 0) + 1
        else:
            done[agent] = done.get(agent, 0) + 1
            if active.get(agent):
                active[agent] -= 1
    # «висящие» старше 2 часов считаем завершёнными (сессия оборвалась)
    return events[-limit:], [a for a, n in active.items() if n > 0], done


def git(*args):
    try:
        out = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                             errors="replace", timeout=8)
        return out.stdout
    except Exception:
        return ""


def read_git():
    now = time.time()
    with _lock:
        t, cached = _cache["git"]
        if cached is not None and now - t < 5:
            return cached
    commits = []
    for line in git("log", "-15", "--date=iso", "--pretty=format:%h\x1f%an\x1f%ad\x1f%s").splitlines():
        parts = line.split("\x1f")
        if len(parts) == 4:
            commits.append({"hash": parts[0], "author": parts[1], "date": parts[2], "subject": parts[3]})
    changed = [l[3:] for l in git("status", "--porcelain").splitlines() if len(l) > 3][:30]
    branch = git("rev-parse", "--abbrev-ref", "HEAD").strip()
    result = {"commits": commits, "changed": changed, "branch": branch}
    with _lock:
        _cache["git"] = (now, result)
    return result


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=HERE, **kw)

    def log_message(self, fmt, *args):  # тихо: браузер дёргает API каждую секунду
        pass

    def send_json(self, obj):
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        path = urlparse(self.path).path
        if path == "/api/agents":
            return self.send_json({"agents": read_agents()})
        if path == "/api/state":
            events, active, done = read_events()
            return self.send_json({"time": time.time(), "events": events, "active": active, "done": done,
                                   "git": read_git(), "logFound": os.path.isfile(AUDIT_LOG)})
        if path.startswith("/api/"):
            self.send_error(404)
            return
        return super().do_GET()


def main():
    ap = argparse.ArgumentParser(description="GRABZ STUDIO office simulator")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--no-browser", action="store_true")
    args = ap.parse_args()
    server = None
    for port in range(args.port, args.port + 20):
        try:
            server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
            break
        except OSError:
            continue
    if server is None:
        print("Не удалось занять порт", file=sys.stderr)
        sys.exit(1)
    url = f"http://127.0.0.1:{server.server_address[1]}/"
    print(f"GRABZ STUDIO: {url}  (Ctrl+C — остановить)")
    print(f"  агенты: {AGENTS_DIR}")
    print(f"  журнал хуков: {AUDIT_LOG} {'(есть)' if os.path.isfile(AUDIT_LOG) else '(пока пуст — включите демо-режим)'}")
    if not args.no_browser:
        threading.Timer(0.6, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
