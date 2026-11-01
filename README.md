# RepoModHelper

R.E.P.O. 一键 Mod 安装器 + 云端同步节点。装完进游戏按 **F6** 刷物品，附带高清画面、高清 UI 与模组汉化。

## 架构

```
客户端 exe ──① status.json ──▶ 云端节点（状态机）
           ──② manifest.json ─▶ 云端节点（包清单 + sha256）
           ──③ 下载 zip ──────▶ 云端节点（直连，约 3~5 分钟）
                 └─ 云端不可达 ─▶ 回退 Thunderstore 源站
```

## 组件与版本

共 20 个包，全部为 **0.5.0（Unity 6）之后更新** 的版本。

| 分类 | Key | 包 | 作用 |
|---|---|---|---|
| 前置 | `BepInExPack` | BepInExPack 5.4.2305 | mod 加载框架（必装） |
| 前置 | `REPOLib` | REPOLib 4.3.0 | 依赖库 |
| 前置 | `MenuLib` | MenuLib 2.6.0 | 菜单库，多包依赖 |
| 前置 | `REPOConfig` | REPOConfig 1.2.7 | 游戏内 mod 设置界面（改键/开关） |
| 前置 | `ScalerCore` | ScalerCore 1.1.1 | 缩放库，ShrinkCart 依赖 |
| 刷物品 | `WeaponSpawner` | WeaponSpawner 1.4.1 | F6 刷物品/武器/贵重物 |
| 刷物品 | `NoItemSpawnLimit` | NoItemSpawnLimit 1.0.9 | 解除卡车物品上限 |
| 刷物品 | `EnemyAndValuableSpawn` | Enemy & Valuable Spawn Manager 0.7.3 | 刷怪/贵重物开关 |
| 玩法 | `UpgradeEveryRound` | UpgradeEveryRound 3.0.3 | 每局免费升级 |
| 玩法 | `SharedUpgradesPlus` | SharedUpgradesPlus 1.6.2 | 升级全队共享 |
| 玩法 | `ShrinkCart` | ShrinkCart 0.3.2 | 推车自动缩放 |
| 联机 | `Ping_It` | Ping It 1.3.0 | 标记物品/队友 |
| 联机 | `ReviveAll` | ReviveAll 1.6.0 | 团灭一键复活（全员装） |
| 联机 | `MapVotePlusPlus` | MapVotePlusPlus 1.0.1 | 地图投票 |
| 联机 | `MorePlayersSimple` | MorePlayersSimple 1.2.7 | 20 人联机（**不镜像、默认不勾**，见下） |
| 画面 | `REPOFidelity` | REPOFidelity 1.8.2 | 高清画面（DLSS/FSR/SMAA、宽屏、FOV） |
| 画面 | `ClearUI` | Clear UI 0.2.1 | 高清界面 |
| 画面 | `REPOFontChanger` | Font Changer 1.3.0 | 换字体，修中文「口口口口」 |
| 稳定 | `REPOCompatibilityPatch` | REPO Compatibility Patch 1.1.2 | 多 mod 同装时的稳定性补丁（含 `patchers/`） |
| 汉化 | `ChineseTranslation` | Chinese Translation 0.5.101 | 本体 + 100 多个 mod 的汉化 |

> ⚠️ **MorePlayersSimple 不参与镜像**：作者 1.2.7 起改为闭源，明确限制二次分发与整合包使用。因此该包只记录源站直连地址（`mirror: false`），客户端点了才从 Thunderstore 直连下载，且默认不勾选。使用时请保留作者署名 cn_xc 与出处。

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
