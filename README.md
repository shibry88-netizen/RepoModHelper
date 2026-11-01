# RepoModHelper

R.E.P.O. 一键 Mod 安装器 + 云端同步节点。装完进游戏按 **F6** 刷物品，附带高清画面、高清 UI 与模组汉化。

## 架构

| 机器 | 角色 | 说明 |
|---|---|---|
| **1 号 VPS** | 网站 | `lucifer.yoga` / `talktact.uno` |
| **2 号 VPS** | 下载分发 | `dl.lucifer.yoga` —— 本项目的 mod 镜像节点（`/var/www/dl/repomods`） |
| **3 号 VPS** | 构建机 | 所有 exe 都在这台 `dotnet publish`，不在一号/二号构建 |

```
客户端 exe ──① status.json ──▶ 二号节点（状态机）
           ──② manifest.json ─▶ 二号节点（包清单 + sha256）
           ──③ 下载 zip ──────▶ 二号节点（国内直连，约 3~5 分钟）
                 └─ 云端不可达 ─▶ 回退 Thunderstore 源站
```

## 组件与版本

| Key | 包 | 作用 |
|---|---|---|
| `BepInExPack` | BepInExPack 5.4.2305 | 前置框架（必装） |
| `REPOLib` | REPOLib 4.3.0 | 依赖库 |
| `WeaponSpawner` | BusiTeam/WeaponSpawner 1.4.1 | 刷物品（F6，房主必装，队友可选） |
| `REPOFidelity` | Vippy/REPOFidelity 1.8.2 | 高清画面（DLSS/FSR/SMAA、宽屏、FOV） |
| `ClearUI` | soundedsquash/Clear_UI 0.2.1 | 高清界面 |
| `ChineseTranslation` | toored/Chinese_Translation | 模组汉化 |

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

## 云端部署（2 号）

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

> 二号是生产下载站：**不要改 `/etc/nginx/sites-enabled/dl.*`、不要重启 nginx**，静态文件丢进 `/var/www/dl/repomods/` 即可，现有 `location ^~ /dl/` 已经覆盖。

## 构建（3 号）

```bash
# 首次：装 .NET 8 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh -o di.sh && bash di.sh --channel 8.0 --install-dir /opt/dotnet --no-path

cd src
/opt/dotnet/dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o ../out
```

产物：`out/REPO-Mod-Installer.exe`（约 63 MB，自包含，Win10/11 x64 免运行库）。

## 客户端要点

- 自动检测 Steam 库（注册表 + `libraryfolders.vdf` + 全盘常见路径 + 深度 3 浅搜），可手动浏览
- 安装前备份旧 `BepInEx` → `BepInEx_backup_时间戳`，只保留最近 2 份
- 下载包做 sha256 校验，失败自动重下
- 卸载：删除 `BepInEx` / `winhttp.dll` / `doorstop_config.ini`，并还原最近备份
- 离线：首次运行后 exe 旁会生成 `payload/` 缓存，可整目录拷走到无网机器使用

## 许可

MIT
