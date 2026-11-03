# RepoModHelper

R.E.P.O. 一键 Mod 安装器 + 云端同步节点。装完进游戏按 **F6** 刷物品，附带高清画面、高清 UI 与模组汉化。

## 架构

```
客户端 exe ──① status.json ──▶ 云端节点（状态机）
           ──② manifest.json ─▶ 云端节点（包清单 + sha256）
           ──③ 下载 zip ──────▶ 云端节点（直连，约 3~5 分钟）
                 ├─ 需要源站文件 ─▶ 二号 /relay/ 中转（Thunderstore 反代）
                 └─ 云端不可达 ─▶ 回退源站，仍优先走中转
```

## 组件与版本

共 14 个包，全部为 **0.5.0（Unity 6）之后更新** 的版本。

| 分类 | Key | 包 | 作用 |
|---|---|---|---|
| 前置 | `BepInExPack` | BepInExPack 5.4.2305 | mod 加载框架（必装） |
| 前置 | `REPOLib` | REPOLib 4.3.0 | 依赖库 |
| 前置 | `MenuLib` | MenuLib 2.6.0 | 菜单库，多包依赖 |
| 前置 | `REPOConfig` | REPOConfig 1.2.7 | 游戏内 mod 设置界面（改键/开关） |
| 刷物品 | `WeaponSpawner` | WeaponSpawner 1.4.1 | F6 刷物品/武器/贵重物 |
| 刷物品 | `NoItemSpawnLimit` | NoItemSpawnLimit 1.0.9 | 解除卡车物品上限 |
| 刷物品 | `EnemyAndValuableSpawn` | Enemy & Valuable Spawn Manager 0.7.3 | 刷怪/贵重物开关 |
| 外观 | `UnlockAllCosmetics` | UnlockAllCosmetics 1.0.5 | 解锁全部 557 个皮肤（F7 手动触发），纯客户端 |
| 联机 | `MorePlayersSimple` | MorePlayersSimple 1.2.7 | 20 人联机（**不镜像、默认不勾**，见下） |
| 画面 | `REPOFidelity` | REPOFidelity 1.8.2 | 高清画面（DLSS/FSR/SMAA、宽屏、FOV） |
| 画面 | `ClearUI` | Clear UI 0.2.1 | 高清界面 |
| 画面 | `REPOFontChanger` | Font Changer 1.3.0 | 换字体，修中文「口口口口」 |
| 稳定 | `REPOCompatibilityPatch` | REPO Compatibility Patch 1.1.2 | 多 mod 同装时的稳定性补丁（含 `patchers/`） |
| 汉化 | `ChineseTranslation` | Chinese Translation 0.5.101 | 本体 + 100 多个 mod 的汉化 |

> ⚠️ **MorePlayersSimple 不参与镜像**：作者 1.2.7 起改为闭源，明确限制二次分发与整合包使用。因此该包只记录源站直连地址（`mirror: false`），客户端点了才从 Thunderstore 直连下载，且默认不勾选。使用时请保留作者署名 cn_xc 与出处。

## 中转站（relay）

国内 / 网吧无 VPN 时，Thunderstore 直连很慢。二号提供按需中转：

| 原始地址 | 中转地址 |
|---|---|
| `https://thunderstore.io/package/download/<ns>/<name>/<ver>/` | `https://dl.lucifer.yoga/relay/ts/package/download/...` |
| `https://ccdn.thunderstore.io/live/repository/packages/<file>.zip` | `https://dl.lucifer.yoga/relay/cdn/live/repository/packages/...` |

- 客户端自动改写：有中转就走中转，失败回退直连（日志会写来源）
- 上游 302 到 CDN 时用 `proxy_redirect` 改写成本机 `/relay/cdn/`，客户端只需跟一次跳转
- 只放行 Thunderstore 下载路径，其余 403（不是开放代理）
- `proxy_buffering off` + `proxy_max_temp_file_size 0`，156MB 的包直接流式转发不落盘
- 支持 Range（断点续传，206）
- 配置在 `server/nginx/relay.conf`，被 `dl.lucifer.yoga` 的 443 server 块 include
- 健康检查：`GET /relay/healthz` → `relay ok`

实测（2026-11-01）：经中转 3.33 MB/s，直连源站 3.51 MB/s，损耗约 5%，VPS load 0.00。

- 复测（2026-11-02）：经中转 20 秒持续下载 **3.17 MB/s**；`Range: bytes=0-1023` 返回 `206`；越权路径 `/relay/cdn/foo.txt`、`/relay/ts/api/...` 均 `403`。
- ⚠️ 改 `relay.conf` 后必须 `nginx -t` 再 `systemctl reload nginx`（用 reload，不要 restart）。

## 状态机

云端每轮同步会写出 `status.json`，客户端按 `state` 走不同分支：

| state | 含义 | 客户端行为 |
|---|---|---|
| `ok` | 镜像已是最新 | 直接装 |
| `mirror_pulling` | 检测到新版，正在从源站拉取 | 提示「云端服务器正在拉取镜像，请耐心等待…」，**每 10 秒轮询一次**，拉完自动继续安装；超时 10 分钟则询问是否改直连 |
| `game_only` | 游戏更新了，但 mod 源站还没更新 | 提示「游戏已更新，MOD 作者暂未更新，请耐心等待更新」，可选强制安装 |
| `mirror_stale` | 拉取失败 | 提示错误并建议直连源站 |

判定依据：

- **游戏版本** ← Steam 官方新闻 API `ISteamNews/GetNewsForApp?appid=3241660`，从公告标题解析 `vX.Y.Z`
- **mod 版本** ← Thunderstore `api/experimental/package/<ns>/<name>/`
- **只有最新版**：同步时删除同目录旧 zip，客户端安装时清理受管插件目录

## 公告与已安装检测（v2.4）

**云端公告**：每轮同步会把「本次名单相对上一次的变化」写进 `manifest.json` 的 `announcement` 字段：

```json
"announcement": {
  "id": "2026-11-03T12:10:39Z",
  "created_at": "2026-11-03T12:10:39Z",
  "title": "云端 Mod 名单已更新",
  "added":   [{"key": "UnlockAllCosmetics", "version": "1.0.5"}],
  "removed": [],
  "updated": [{"key": "ChineseTranslation", "from": "0.5.101", "to": "0.5.102"}]
}
```

- 客户端启动读 `manifest.json`，把 `announcement.id` 与 `%LOCALAPPDATA%\RepoModHelper\state.json` 里的 `seenAnnouncement` 比对
- 不一致 → 弹窗列出新增 / 移除 / 更新，用户点确定后记录 id
- **下次启动若 id 未变就不再弹**；云端有新变化（新 id）才会再提示

**已安装检测**：每个包带一个 `Marker`（标志文件的相对路径），配合 `BepInEx/repomods-installed.json` 记录版本：

| 情况 | 判定 | 列表表现 |
|---|---|---|
| 标志文件在 + 版本与云端一致 | 已安装 | 默认隐藏（勾「显示已安装的模组」才出现） |
| 标志文件在 + 版本落后 | 可更新 | 显示「可更新 1.8.2→1.8.3」 |
| 标志文件不在 | 未安装 | 显示「未安装」 |

检测在**每次启动**和**每次安装完成**后都会重跑，状态栏显示「已安装 X · 可更新 Y · 未安装 Z」。

## 云端节点部署

```bash
mkdir -p /opt/repomods /var/www/dl/repomods/packages
cp server/sync.py /opt/repomods/sync.py && chmod +x /opt/repomods/sync.py
cp server/systemd/* /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now repomods-sync.timer repomods-watch.timer
python3 /opt/repomods/sync.py        # 首次全量拉取
```

- `repomods-sync.timer`：每 24 小时全量同步（每天 04:17 UTC）
- `repomods-watch.timer`：每 30 分钟快速探测，有新版本立即拉，避免用户长时间看到过期提示
- 日志：`/var/log/repomods-sync.log`
- 对外地址：`https://dl.lucifer.yoga/dl/repomods/status.json`

> 节点跑在生产下载站上：**不要改 `/etc/nginx/sites-enabled/dl.*`、不要重启 nginx**，静态文件丢进 `/var/www/dl/repomods/` 即可，现有 `location ^~ /dl/` 已经覆盖。

## 客户端要点

- 自动检测 Steam 库（注册表 + `libraryfolders.vdf` + 全盘常见路径 + 深度 3 浅搜），可手动浏览
- 安装前备份旧 `BepInEx` → `BepInEx_backup_时间戳`，只保留最近 2 份
- 下载包做 sha256 校验，失败自动重下
- 卸载：删除 `BepInEx` / `winhttp.dll` / `doorstop_config.ini`，并还原最近备份
- 离线：首次运行后 exe 旁会生成 `payload/` 缓存，可整目录拷走到无网机器使用

## 许可

MIT
