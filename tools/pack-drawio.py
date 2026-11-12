#!/usr/bin/env python3
"""
生成 draw.io 内置服务的资源包（WebAssets/Drawio/drawio.pak）。

用法：
    python3 pack-drawio.py <drawio-webapp 目录> <输出 drawio.pak> [版本号]

drawio-webapp 目录指 draw.io 源码仓库 v32.6.10 的 src/main/webapp。
脚本会挑选运行时需要的官方前端资源（不含 Java 后端与开发模式源码），
把文本资源用 Brotli 预压缩后打成一个二进制包；C# 服务启动时整包读入内存，
按请求直接返回压缩后的字节，避免磁盘 IO 与实时压缩。

依赖：pip install brotli
"""
import collections
import hashlib
import json
import os
import struct
import sys

import brotli

SRC = sys.argv[1]
OUT = sys.argv[2]
TAG = sys.argv[3] if len(sys.argv) > 3 else "32.6.10"

TEXT_EXT = {".html", ".htm", ".js", ".mjs", ".css", ".json", ".xml", ".svg", ".txt",
            ".properties", ".md", ".csv", ".wsf", ".ts"}
MIME = {
    ".html": "text/html; charset=utf-8", ".htm": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8", ".js": "application/javascript; charset=utf-8",
    ".mjs": "application/javascript; charset=utf-8", ".json": "application/json; charset=utf-8",
    ".map": "application/json; charset=utf-8", ".svg": "image/svg+xml",
    ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".gif": "image/gif",
    ".ico": "image/x-icon", ".webp": "image/webp", ".ttf": "font/ttf", ".woff": "font/woff",
    ".woff2": "font/woff2", ".otf": "font/otf", ".eot": "application/vnd.ms-fontobject",
    ".xml": "application/xml; charset=utf-8", ".txt": "text/plain; charset=utf-8",
    ".properties": "text/plain; charset=utf-8", ".md": "text/markdown; charset=utf-8",
    ".pdf": "application/pdf", ".mp3": "audio/mpeg", ".mp4": "video/mp4", ".wav": "audio/wav",
    ".csv": "text/csv; charset=utf-8",
}
TOP = {"index.html", "favicon.ico", "shortcuts.svg", "export3.html", "clear.html",
       "open.html", "onedrive3.html", "teams.html", "vsdxImporter.html",
       "monday-app-association.json", "LICENSE"}
DIRS = ["connect", "images", "img", "resources", "shapes", "stencils", "templates",
        "styles", "plugins", "math4"]


def selected(rel):
    parts = rel.split("/")
    if rel in TOP:
        return True
    if parts[0] in DIRS:
        return True
    if parts[0] == "mxgraph":
        return len(parts) >= 2 and (parts[1] == "mxClient.js" or parts[1] in ("css", "images"))
    if parts[0] == "js":
        if len(parts) == 1 or not rel.endswith(".js"):
            return False
        if parts[1] == "grapheditor":
            return False
        if parts[1] == "diagramly":
            # 发行版 app.min.js 只会在运行时动态加载 Extensions.js（Lucidchart 粘贴）
            return parts[-1] == "Extensions.js"
        return True
    return False


def mime(path):
    return MIME.get(os.path.splitext(path)[1].lower(), "application/octet-stream")


entries = {}
blobs = []
offset = 0
raw_total = stored_total = 0

for root, _dirs, names in os.walk(SRC):
    for name in names:
        full = os.path.join(root, name)
        rel = os.path.relpath(full, SRC).replace(os.sep, "/")
        if not selected(rel):
            continue
        data = open(full, "rb").read()
        if rel == "index.html":
            # 提前预加载主程序包，缩短首次打开等待时间（不改动 draw.io 自身逻辑）
            marker = b'<script src="js/bootstrap.js"></script>'
            preload = b'<link rel="preload" href="js/app.min.js" as="script">\n\t' + marker
            if marker in data:
                data = data.replace(marker, preload, 1)
        ext = os.path.splitext(rel)[1].lower()
        enc = "none"
        stored = data
        if ext in TEXT_EXT:
            try:
                packed = brotli.compress(data, quality=11 if len(data) < 6 * 1024 * 1024 else 9)
                if len(packed) < len(data):
                    stored, enc = packed, "br"
            except Exception:
                pass
        entries[rel] = [offset, len(stored), len(data), enc, mime(rel),
                        hashlib.sha1(data).hexdigest()[:20]]
        blobs.append(stored)
        offset += len(stored)
        raw_total += len(data)
        stored_total += len(stored)

# Apache-2.0 许可证随包内置，可通过 /LICENSE.txt 查看
lic = os.path.normpath(os.path.join(SRC, "../../../LICENSE"))
if os.path.exists(lic):
    data = open(lic, "rb").read()
    packed = brotli.compress(data, quality=11)
    entries["LICENSE.txt"] = [offset, len(packed), len(data), "br",
                              "text/plain; charset=utf-8",
                              hashlib.sha1(data).hexdigest()[:20]]
    blobs.append(packed)
    offset += len(packed)

index = json.dumps({"v": TAG, "files": entries}, separators=(",", ":")).encode("utf-8")
with open(OUT, "wb") as f:
    f.write(b"DMPAK1\n")
    f.write(struct.pack("<I", len(index)))
    f.write(index)
    for blob in blobs:
        f.write(blob)

print(f"drawio {TAG}: {len(entries)} files, raw {raw_total / 1048576:.1f}MB, "
      f"packed {os.path.getsize(OUT) / 1048576:.1f}MB")
