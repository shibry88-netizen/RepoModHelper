#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RepoModHelper 云端同步节点  ——  /opt/repomods/sync.py
作用：
  1. 查 Steam 官方新闻 API，得到游戏最新版本号（R.E.P.O. appid 3241660）
  2. 查 Thunderstore API，得到每个 mod 的最新版本
  3. 只保留最新版，把包下载到 /var/www/dl/repomods/packages/<ns>-<name>/<ver>.zip
  4. 写出 manifest.json（给安装器用）与 status.json（给安装器每 10s 轮询用）
状态：
  ok              云端已是最新
  mirror_pulling  检测到新版，正在拉取镜像
  game_only       游戏更新了，但 mod 源站还没更新（等作者）
  mirror_stale    拉取失败，需要人工看日志
"""

import json
import os
import re
import sys
import time
import fcntl
import hashlib
import urllib.request
import urllib.error
from datetime import datetime, timezone

# ---------------- 配置 ----------------
APPID = 3241660
ROOT = "/var/www/dl/repomods"
PKG_DIR = os.path.join(ROOT, "packages")
MANIFEST = os.path.join(ROOT, "manifest.json")
STATUS = os.path.join(ROOT, "status.json")
LOG = "/var/log/repomods-sync.log"
PUBLIC_BASE = "https://dl.lucifer.yoga/dl/repomods"
UA = "RepoModHelper-Sync/1.0 (+https://github.com/shibry88-netizen/RepoModHelper)"

# key 与安装器里的 ModPackage 一一对应
PACKAGES = [
    # --- 前置框架 / 库 ---
    {"key": "BepInExPack",           "ns": "BepInEx",          "name": "BepInExPack"},
    {"key": "REPOLib",               "ns": "Zehs",             "name": "REPOLib"},
    {"key": "MenuLib",               "ns": "REPOModding",      "name": "MenuLib"},
    {"key": "REPOConfig",            "ns": "REPOModding",      "name": "REPOConfig"},
    # --- 刷物品 / 玩法 ---
    {"key": "WeaponSpawner",         "ns": "BusiTeam",         "name": "WeaponSpawner"},
    {"key": "NoItemSpawnLimit",      "ns": "HeroHanex",        "name": "NoItemSpawnLimit"},
    {"key": "EnemyAndValuableSpawn", "ns": "soundedsquash",    "name": "Enemy_And_Valuable_Spawn_Manager"},
    {"key": "UnlockAllCosmetics",    "ns": "UnlockAllCosmetics", "name": "UnlockAllCosmetics"},
    # --- 联机体验 ---
    {"key": "MorePlayersSimple",     "ns": "cn_xc",            "name": "MorePlayersSimple", "mirror": False},
    # --- 画面 / 稳定性 / 汉化 ---
    {"key": "REPOCompatibilityPatch","ns": "DEADTROL",         "name": "REPO_Compatibility_Patch"},
    {"key": "REPOFidelity",          "ns": "Vippy",            "name": "REPOFidelity"},
    {"key": "ClearUI",               "ns": "soundedsquash",    "name": "Clear_UI"},
    {"key": "REPOFontChanger",       "ns": "ramune1256",       "name": "REPO_FontChanger"},
    {"key": "ChineseTranslation",    "ns": "toored",           "name": "Chinese_Translation"},
]

# ---------------- 工具 ----------------
def log(msg):
    line = "[%s] %s" % (datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ"), msg)
    print(line, flush=True)
    try:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def http_get(url, timeout=30, binary=False):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        data = r.read()
        if r.status and r.status >= 400:
            raise RuntimeError("HTTP %s" % r.status)
        return data if binary else data.decode("utf-8", "replace")


def ver_tuple(v):
    """'0.5.101' -> (0,5,101)；无法解析返回 (0,)"""
    if not v:
        return (0,)
    out = []
    for p in str(v).replace("-", ".").split("."):
        m = re.match(r"^(\d+)", p)
        out.append(int(m.group(1)) if m else 0)
    while len(out) < 3:
        out.append(0)
    return tuple(out)


def load_json(path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def write_json(path, obj):
    tmp = path + ".tmp"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ---------------- 检测 ----------------
def get_game_version():
    """从 Steam 官方新闻标题里取最新版本号；失败返回 None"""
    url = ("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/"
           "?appid=%d&count=10&maxlength=120&format=json" % APPID)
    data = json.loads(http_get(url, timeout=25))
    items = data.get("appnews", {}).get("newsitems", [])
    best = None
    for it in items:
        title = it.get("title") or ""
        m = re.search(r"v?\s*(\d+\.\d+(?:\.\d+){0,2})", title)
        if m:
            v = m.group(1)
            if best is None or ver_tuple(v) > ver_tuple(best["version"]):
                best = {"version": v, "title": title,
                        "date": datetime.fromtimestamp(it.get("date", 0), timezone.utc)
                                  .strftime("%Y-%m-%dT%H:%M:%SZ")}
    return best


def get_mod_latest(pkg):
    url = "https://thunderstore.io/api/experimental/package/%s/%s/" % (pkg["ns"], pkg["name"])
    d = json.loads(http_get(url, timeout=25))
    latest = d.get("latest") or {}
    return {
        "version": latest.get("version_number"),
        "download_url": latest.get("download_url") or
                        "https://thunderstore.io/package/download/%s/%s/%s/" % (
                            pkg["ns"], pkg["name"], latest.get("version_number")),
        "date": latest.get("datetime_created", ""),
    }


# ---------------- 下载 ----------------
def download_package(pkg, ver, url):
    folder = os.path.join(PKG_DIR, "%s-%s" % (pkg["ns"], pkg["name"]))
    os.makedirs(folder, exist_ok=True)
    fn = "%s-%s-%s.zip" % (pkg["ns"], pkg["name"], ver)
    dest = os.path.join(folder, fn)
    if os.path.exists(dest) and os.path.getsize(dest) > 10000:
        log("  已存在，跳过下载：%s" % fn)
        return dest
    tmp = dest + ".part"
    log("  下载 %s ..." % url)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=120) as r, open(tmp, "wb") as f:
        shutil_copy(r, f)
    os.replace(tmp, dest)
    log("  完成：%s（%.1f MB）" % (fn, os.path.getsize(dest) / 1048576.0))
    # 只保留最新版：删掉同目录下其它版本的 zip
    for other in os.listdir(folder):
        p = os.path.join(folder, other)
        if p != dest and other.endswith(".zip"):
            try:
                os.remove(p)
                log("  清理旧版：%s" % other)
            except Exception as e:
                log("  清理失败 %s：%s" % (other, e))
    return dest


def shutil_copy(src, dst, bufsize=1 << 20):
    while True:
        buf = src.read(bufsize)
        if not buf:
            break
        dst.write(buf)


# ---------------- 主流程 ----------------
def main():
    os.makedirs(PKG_DIR, exist_ok=True)

    lock_path = "/tmp/repomods-sync.lock"
    lock = open(lock_path, "w")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        log("已有同步任务在跑，退出")
        return 0

    manifest = load_json(MANIFEST, {})
    old_game = manifest.get("game_version") or ""
    old_pkgs = {p["key"]: p for p in manifest.get("packages", [])}

    # 1) 游戏版本
    game = None
    try:
        game = get_game_version()
        log(("游戏最新版本：%s（%s）" % (game["version"], game["title"][:60])) if game else "游戏版本解析失败")
    except Exception as e:
        log("⚠ 查游戏版本失败：%s（沿用上次记录）" % e)
    game_ver = game["version"] if game else old_game

    # 2) mod 最新版本
    latest = {}
    for pkg in PACKAGES:
        try:
            info = get_mod_latest(pkg)
            latest[pkg["key"]] = info
            log("mod %-20s 源站最新 %s" % (pkg["key"], info["version"]))
        except Exception as e:
            log("⚠ 查 %s 失败：%s" % (pkg["key"], e))

    # 3) 判断要不要更新
    to_pull = []
    for pkg in PACKAGES:
        info = latest.get(pkg["key"])
        if not info or not info["version"]:
            continue
        old = old_pkgs.get(pkg["key"], {})
        if ver_tuple(info["version"]) > ver_tuple(old.get("version")):
            to_pull.append((pkg, info))

    game_changed = bool(game_ver) and ver_tuple(game_ver) > ver_tuple(old_game)

    # 4) 状态判定 + 拉取
    if to_pull:
        write_json(STATUS, {
            "state": "mirror_pulling",
            "message": "云端服务器正在拉取镜像，请耐心等待…",
            "game_version": game_ver,
            "mirror_game_version": old_game,
            "pulling": [{"name": p[0]["key"], "version": p[1]["version"]} for p in to_pull],
            "checked_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        })
        log("检测到 %d 个包需要更新，开始拉取" % len(to_pull))

        packages = []
        failed = []
        for pkg in PACKAGES:
            info = latest.get(pkg["key"])
            old = old_pkgs.get(pkg["key"], {})
            if not info or not info["version"]:
                if old:
                    packages.append(old)  # 查不到源站就沿用旧记录
                continue
            try:
                if pkg.get("mirror") is False:
                    # 作者限制二次分发：只记录源站地址，不落到本地镜像
                    log("  跳过镜像（作者限制分发）：%s" % pkg["key"])
                    packages.append({
                        "key": pkg["key"],
                        "version": info["version"],
                        "file": "",
                        "url": info["download_url"],
                        "size": 0,
                        "sha256": "",
                        "source": info["download_url"],
                        "mirrored": False,
                        "updated_at": info.get("date", ""),
                    })
                    continue
                path = download_package(pkg, info["version"], info["download_url"])
                packages.append({
                    "key": pkg["key"],
                    "version": info["version"],
                    "file": "%s-%s-%s.zip" % (pkg["ns"], pkg["name"], info["version"]),
                    "url": "%s/packages/%s-%s/%s-%s-%s.zip" % (
                        PUBLIC_BASE, pkg["ns"], pkg["name"], pkg["ns"], pkg["name"], info["version"]),
                    "size": os.path.getsize(path),
                    "sha256": sha256_of(path),
                    "source": info["download_url"],
                    "updated_at": info.get("date", ""),
                })
            except Exception as e:
                log("❌ 下载 %s 失败：%s" % (pkg["key"], e))
                failed.append(pkg["key"])
                if old:
                    packages.append(old)

        if failed:
            write_json(STATUS, {
                "state": "mirror_stale",
                "message": "云端镜像更新失败（%s），请稍后重试或联系维护者" % "、".join(failed),
                "game_version": game_ver,
                "mirror_game_version": old_game,
                "checked_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            })
            log("同步失败：%s" % failed)
            return 1
    else:
        packages = [old_pkgs[p["key"]] for p in PACKAGES if p["key"] in old_pkgs]

    # 4.5) 生成公告：与上一次清单比对
    old_map = {p["key"]: p for p in (manifest.get("packages") or [])}
    new_map = {p["key"]: p for p in packages}
    added = [{"key": k, "version": new_map[k].get("version", "")} for k in new_map if k not in old_map]
    removed = [{"key": k, "version": old_map[k].get("version", "")} for k in old_map if k not in new_map]
    updated = [{"key": k, "from": old_map[k].get("version", ""), "to": new_map[k].get("version", "")}
               for k in new_map if k in old_map and old_map[k].get("version") != new_map[k].get("version")]

    announcement = manifest.get("announcement")
    if old_map and (added or removed or updated):
        now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        announcement = {"id": now, "created_at": now, "title": "云端 Mod 名单已更新",
                        "added": added, "removed": removed, "updated": updated}
        log("公告已生成：新增 %d / 移除 %d / 更新 %d" % (len(added), len(removed), len(updated)))

    # 5) 写 manifest
    new_manifest = {
        "schema": 3,
        "game": "R.E.P.O.",
        "appid": APPID,
        "game_version": game_ver,
        "game_updated_at": (game or {}).get("date", manifest.get("game_updated_at", "")),
        "synced_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "packages": packages,
        "announcement": announcement,
    }
    write_json(MANIFEST, new_manifest)

    # 6) 状态：游戏更新了但 mod 没跟上 → game_only
    no_mod_updates = not to_pull
    if game_changed and no_mod_updates:
        state, msg = ("game_only",
                      "游戏已更新到 %s，MOD 作者暂未更新，请耐心等待更新" % game_ver)
    else:
        state, msg = ("ok", "云端镜像已是最新")

    write_json(STATUS, {
        "state": state,
        "message": msg,
        "game_version": game_ver,
        "mirror_game_version": game_ver,
        "package_count": len(packages),
        "checked_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "next_check_in": 86400,
    })
    log("✔ 同步完成，state=%s，包数=%d" % (state, len(packages)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        import traceback
        log("❌ 未捕获异常：%s" % e)
        log(traceback.format_exc())
        try:
            write_json(STATUS, {
                "state": "mirror_stale",
                "message": "云端同步异常，请稍后重试",
                "checked_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            })
        except Exception:
            pass
        sys.exit(1)
