using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RepoInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class ModPackage
    {
        public string Name;
        public string Key;            // 与云端 manifest 的 key 对应
        public string Ns;             // Thunderstore namespace
        public string TsName;         // Thunderstore package name
        public string MatchKey;
        public string Url;
        public string AltUrl;
        public string FileName;
        public int Mode;              // 0 = 解到游戏根目录, 1 = 解到 BepInEx/plugins/<Folder>
        public string StripPrefix;
        public string Folder;
        public bool Required;
        public bool DirectOnly;   // 只从源站下载，不进云端镜像（作者授权限制）
        public bool DefaultOff;   // 列表里默认不勾选
        public string Marker;     // 判定“已安装”的标志文件（相对游戏目录）
        public string StatusText; // 列表里追加的状态（未安装 / 可更新）
        public override string ToString() =>
            string.IsNullOrEmpty(StatusText) ? Name : Name + "   ·" + StatusText;
    }

    internal static class Core
    {
        public const string RepoDir = "REPO.exe";

        public static readonly ModPackage[] Packages = new[]
                {
                    new ModPackage {
                        Name = "BepInExPack 5.4.2305  ← 前置框架（必装）", Key = "BepInExPack", Ns = "BepInEx", TsName = "BepInExPack", MatchKey = "BepInExPack",
                        Url = "https://thunderstore.io/package/download/BepInEx/BepInExPack/5.4.2305/",
                        AltUrl = "https://old.thunderstore.io/package/download/BepInEx/BepInExPack/5.4.2305/",
                        FileName = "BepInEx-BepInExPack-5.4.2305.zip", Mode = 0, StripPrefix = "BepInExPack/", Folder = null,
                        Marker = "winhttp.dll", Required = true, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "REPOLib 4.3.0  ← 依赖库", Key = "REPOLib", Ns = "Zehs", TsName = "REPOLib", MatchKey = "REPOLib",
                        Url = "https://thunderstore.io/package/download/Zehs/REPOLib/4.3.0/",
                        AltUrl = "https://old.thunderstore.io/package/download/Zehs/REPOLib/4.3.0/",
                        FileName = "Zehs-REPOLib-4.3.0.zip", Mode = 1, StripPrefix = "plugins/", Folder = "REPOLib",
                        Marker = "BepInEx/plugins/REPOLib", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "MenuLib 2.6.0  ← 菜单库（多个 mod 依赖）", Key = "MenuLib", Ns = "REPOModding", TsName = "MenuLib", MatchKey = "MenuLib",
                        Url = "https://thunderstore.io/package/download/REPOModding/MenuLib/2.6.0/",
                        AltUrl = "https://old.thunderstore.io/package/download/REPOModding/MenuLib/2.6.0/",
                        FileName = "REPOModding-MenuLib-2.6.0.zip", Mode = 1, StripPrefix = "", Folder = "MenuLib",
                        Marker = "BepInEx/plugins/MenuLib", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "REPOConfig 1.2.7  ← 游戏内 mod 设置界面", Key = "REPOConfig", Ns = "REPOModding", TsName = "REPOConfig", MatchKey = "REPOConfig",
                        Url = "https://thunderstore.io/package/download/REPOModding/REPOConfig/1.2.7/",
                        AltUrl = "https://old.thunderstore.io/package/download/REPOModding/REPOConfig/1.2.7/",
                        FileName = "REPOModding-REPOConfig-1.2.7.zip", Mode = 1, StripPrefix = "", Folder = "REPOConfig",
                        Marker = "BepInEx/plugins/REPOConfig", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "WeaponSpawner 1.4.1  ← 刷物品（F6）", Key = "WeaponSpawner", Ns = "BusiTeam", TsName = "WeaponSpawner", MatchKey = "WeaponSpawner",
                        Url = "https://thunderstore.io/package/download/BusiTeam/WeaponSpawner/1.4.1/",
                        AltUrl = "https://old.thunderstore.io/package/download/BusiTeam/WeaponSpawner/1.4.1/",
                        FileName = "BusiTeam-WeaponSpawner-1.4.1.zip", Mode = 1, StripPrefix = "", Folder = "WeaponSpawner",
                        Marker = "BepInEx/plugins/WeaponSpawner", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "NoItemSpawnLimit 1.0.9  ← 解除卡车物品上限", Key = "NoItemSpawnLimit", Ns = "HeroHanex", TsName = "NoItemSpawnLimit", MatchKey = "NoItemSpawnLimit",
                        Url = "https://thunderstore.io/package/download/HeroHanex/NoItemSpawnLimit/1.0.9/",
                        AltUrl = "https://old.thunderstore.io/package/download/HeroHanex/NoItemSpawnLimit/1.0.9/",
                        FileName = "HeroHanex-NoItemSpawnLimit-1.0.9.zip", Mode = 1, StripPrefix = "", Folder = "NoItemSpawnLimit",
                        Marker = "BepInEx/plugins/NoItemSpawnLimit", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "刷怪/贵重物开关 0.7.3  ← 只刷想刷的", Key = "EnemyAndValuableSpawn", Ns = "soundedsquash", TsName = "Enemy_And_Valuable_Spawn_Manager", MatchKey = "EnemyAndValuableSpawn",
                        Url = "https://thunderstore.io/package/download/soundedsquash/Enemy_And_Valuable_Spawn_Manager/0.7.3/",
                        AltUrl = "https://old.thunderstore.io/package/download/soundedsquash/Enemy_And_Valuable_Spawn_Manager/0.7.3/",
                        FileName = "soundedsquash-Enemy_And_Valuable_Spawn_Manager-0.7.3.zip", Mode = 1, StripPrefix = "", Folder = "SpawnManager",
                        Marker = "BepInEx/plugins/SpawnManager", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "20 人联机 1.2.7  ← 作者限制，默认不勾", Key = "MorePlayersSimple", Ns = "cn_xc", TsName = "MorePlayersSimple", MatchKey = "MorePlayersSimple",
                        Url = "https://thunderstore.io/package/download/cn_xc/MorePlayersSimple/1.2.7/",
                        AltUrl = "https://old.thunderstore.io/package/download/cn_xc/MorePlayersSimple/1.2.7/",
                        FileName = "cn_xc-MorePlayersSimple-1.2.7.zip", Mode = 1, StripPrefix = "plugins/", Folder = "MorePlayersSimple",
                        Marker = "BepInEx/plugins/MorePlayersSimple", Required = false, DirectOnly = true, DefaultOff = true },
                    new ModPackage {
                        Name = "REPOFidelity 1.8.2  ← 高清画面", Key = "REPOFidelity", Ns = "Vippy", TsName = "REPOFidelity", MatchKey = "REPOFidelity",
                        Url = "https://thunderstore.io/package/download/Vippy/REPOFidelity/1.8.2/",
                        AltUrl = "https://old.thunderstore.io/package/download/Vippy/REPOFidelity/1.8.2/",
                        FileName = "Vippy-REPOFidelity-1.8.2.zip", Mode = 1, StripPrefix = "", Folder = "REPOFidelity",
                        Marker = "BepInEx/plugins/REPOFidelity", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "Clear UI 0.2.1  ← 高清界面", Key = "ClearUI", Ns = "soundedsquash", TsName = "Clear_UI", MatchKey = "ClearUI",
                        Url = "https://thunderstore.io/package/download/soundedsquash/Clear_UI/0.2.1/",
                        AltUrl = "https://old.thunderstore.io/package/download/soundedsquash/Clear_UI/0.2.1/",
                        FileName = "soundedsquash-Clear_UI-0.2.1.zip", Mode = 1, StripPrefix = "", Folder = "ClearUI",
                        Marker = "BepInEx/plugins/ClearUI", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "字体替换 1.3.0  ← 修「口口口口」", Key = "REPOFontChanger", Ns = "ramune1256", TsName = "REPO_FontChanger", MatchKey = "REPOFontChanger",
                        Url = "https://thunderstore.io/package/download/ramune1256/REPO_FontChanger/1.3.0/",
                        AltUrl = "https://old.thunderstore.io/package/download/ramune1256/REPO_FontChanger/1.3.0/",
                        FileName = "ramune1256-REPO_FontChanger-1.3.0.zip", Mode = 1, StripPrefix = "", Folder = "REPOFontChanger",
                        Marker = "BepInEx/plugins/REPOFontChanger", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "稳定性补丁 1.1.2  ← 多 mod 同装必备", Key = "REPOCompatibilityPatch", Ns = "DEADTROL", TsName = "REPO_Compatibility_Patch", MatchKey = "REPOCompatibilityPatch",
                        Url = "https://thunderstore.io/package/download/DEADTROL/REPO_Compatibility_Patch/1.1.2/",
                        AltUrl = "https://old.thunderstore.io/package/download/DEADTROL/REPO_Compatibility_Patch/1.1.2/",
                        FileName = "DEADTROL-REPO_Compatibility_Patch-1.1.2.zip", Mode = 2, StripPrefix = "", Folder = null,
                        Marker = "BepInEx/plugins/REPOCompatibilityPatch.dll", Required = false, DirectOnly = false, DefaultOff = false },
                    new ModPackage {
                        Name = "Chinese Translation 0.5.101  ← 模组汉化", Key = "ChineseTranslation", Ns = "toored", TsName = "Chinese_Translation", MatchKey = "ChineseTranslation",
                        Url = "https://thunderstore.io/package/download/toored/Chinese_Translation/0.5.101/",
                        AltUrl = "https://old.thunderstore.io/package/download/toored/Chinese_Translation/0.5.101/",
                        FileName = "toored-Chinese_Translation-0.5.101.zip", Mode = 0, StripPrefix = "", Folder = null,
                        Marker = "BepInEx/plugins/XUnity.AutoTranslator", Required = false, DirectOnly = false, DefaultOff = false },
            new ModPackage {
                Name = "解锁全部皮肤 1.0.5  ← F7 解锁 557 个外观", Key = "UnlockAllCosmetics", Ns = "UnlockAllCosmetics", TsName = "UnlockAllCosmetics", MatchKey = "UnlockAllCosmetics",
                Url = "https://thunderstore.io/package/download/UnlockAllCosmetics/UnlockAllCosmetics/1.0.5/",
                AltUrl = "https://old.thunderstore.io/package/download/UnlockAllCosmetics/UnlockAllCosmetics/1.0.5/",
                FileName = "UnlockAllCosmetics-UnlockAllCosmetics-1.0.5.zip", Mode = 1, StripPrefix = "plugins/", Folder = "UnlockAllCosmetics",
                Marker = "BepInEx/plugins/UnlockAllCosmetics",
                Required = false, DirectOnly = false, DefaultOff = false },
                };

        // ---------- 目录检测 ----------

        public static bool IsGameDir(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
                if (File.Exists(Path.Combine(dir, RepoDir))) return true;
                return Directory.Exists(Path.Combine(dir, "REPO_Data"));
            }
            catch { return false; }
        }

        public static string GameVersion(string dir)
        {
            try
            {
                string exe = Path.Combine(dir, RepoDir);
                if (!File.Exists(exe)) return "未知（未找到 REPO.exe）";
                var vi = FileVersionInfo.GetVersionInfo(exe);
                string v = !string.IsNullOrWhiteSpace(vi.FileVersion) ? vi.FileVersion : vi.ProductVersion;
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                var fi = new FileInfo(exe);
                return "0.5.x（推测，文件日期 " + fi.LastWriteTime.ToString("yyyy-MM-dd") + "）";
            }
            catch { return "未知"; }
        }

        public static List<string> DetectGameDirs(Action<string> onScan = null)
        {
            var found = new List<string>();
            void Add(string p)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(p)) return;
                    string full = Path.GetFullPath(p.TrimEnd('\\', '/'));
                    if (IsGameDir(full) && !found.Contains(full)) found.Add(full);
                }
                catch { }
            }

            // 1) Steam 注册表 + libraryfolders.vdf
            string[] keys =
            {
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
            };
            foreach (var key in keys)
            {
                try
                {
                    object v = Registry.GetValue(key, "SteamPath", null);
                    if (v == null) v = Registry.GetValue(key, "InstallPath", null);
                    if (v is string s && s.Length > 0)
                    {
                        string steam = s.Replace('/', '\\');
                        onScan?.Invoke("Steam 目录：" + steam);
                        Add(Path.Combine(steam, "steamapps", "common", "REPO"));
                        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                        {
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                                Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", "REPO"));
                        }
                    }
                }
                catch { }
            }

            // 2) 各盘符常见路径（网吧常见布局）
            string[] rels =
            {
                @"Steam\steamapps\common\REPO",
                @"SteamLibrary\steamapps\common\REPO",
                @"SteamCN\steamapps\common\REPO",
                @"Games\Steam\steamapps\common\REPO",
                @"Games\SteamLibrary\steamapps\common\REPO",
                @"Program Files (x86)\Steam\steamapps\common\REPO",
                @"Program Files\Steam\steamapps\common\REPO",
                @"Games\REPO", @"游戏\REPO", @"REPO",
                @"网维大师\游戏\REPO", @"网络游戏\REPO", @"单机游戏\REPO"
            };
            foreach (var d in SafeDrives())
            {
                onScan?.Invoke("扫描盘符：" + d);
                foreach (var r in rels) Add(Path.Combine(d, r));
            }

            // 3) 浅层搜索名为 REPO 且含 REPO.exe 的目录（深度 3）
            if (found.Count == 0)
            {
                foreach (var d in SafeDrives())
                {
                    onScan?.Invoke("深度搜索：" + d);
                    foreach (var hit in ShallowFind(d, 3, 200)) Add(hit);
                    if (found.Count > 0) break;
                }
            }
            return found;
        }

        private static IEnumerable<string> SafeDrives()
        {
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); } catch { yield break; }
            foreach (var d in all)
            {
                string root = null;
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    root = d.RootDirectory.FullName;
                }
                catch { }
                if (root != null) yield return root;
            }
        }

        private static IEnumerable<string> ShallowFind(string root, int maxDepth, int cap)
        {
            var hits = new List<string>();
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((root, 0));
            int visited = 0;
            string[] skip = { "windows", "$recycle.bin", "system volume information", "programdata", "recovery", "perflogs", "config.msi", "msocache", "intel", "amd", "nvidia" };

            while (queue.Count > 0 && visited < cap && hits.Count < 5)
            {
                var (path, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(path); } catch { continue; }
                foreach (var sub in subs)
                {
                    visited++;
                    if (visited > cap) break;
                    string name = Path.GetFileName(sub);
                    if (skip.Any(s => name.Equals(s, StringComparison.OrdinalIgnoreCase))) continue;
                    if (name.Equals("REPO", StringComparison.OrdinalIgnoreCase) && IsGameDir(sub)) { hits.Add(sub); continue; }
                    queue.Enqueue((sub, depth + 1));
                }
            }
            return hits;
        }

        // ---------- 缓存 / 下载 ----------

        public static string PickCacheDir(string exeDir)
        {
            try
            {
                string p = Path.Combine(exeDir, "payload");
                Directory.CreateDirectory(p);
                string test = Path.Combine(p, ".wtest");
                File.WriteAllText(test, "1");
                File.Delete(test);
                return p;
            }
            catch { }
            string t = Path.Combine(Path.GetTempPath(), "REPO-Mod-Cache");
            Directory.CreateDirectory(t);
            return t;
        }

        public static string FindUsableZip(string cacheDir, ModPackage p)
        {
            try
            {
                string exact = Path.Combine(cacheDir, p.FileName);
                if (File.Exists(exact) && new FileInfo(exact).Length > 1000) return exact;

                var dirs = new List<string> { cacheDir, Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "payload") };
                foreach (var dir in dirs.Distinct())
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.zip"))
                    {
                        string n = Path.GetFileName(f);
                        if (n.IndexOf(p.MatchKey, StringComparison.OrdinalIgnoreCase) >= 0 && new FileInfo(f).Length > 1000)
                            return f;
                    }
                }
            }
            catch { }
            return null;
        }

        public static async Task DownloadAsync(string url, string dest, IProgress<double> prog, Action<string> log, CancellationToken ct)
        {
            using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }))
            {
                http.Timeout = TimeSpan.FromMinutes(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("REPO-Mod-Installer/1.0");
                using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    log("  开始下载：" + Path.GetFileName(dest) + (total > 0 ? "（" + (total / 1048576.0).ToString("0.0") + " MB）" : ""));
                    string tmp = dest + ".part";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    {
                        var buf = new byte[81920];
                        long got = 0;
                        int n;
                        int lastMs = 0;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await fs.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            got += n;
                            if (total > 0) prog?.Report((double)got / total);
                            int ms = Environment.TickCount;
                            if (ms - lastMs > 300) { lastMs = ms; log("    已下载 " + (got / 1048576.0).ToString("0.0") + " / " + (total / 1048576.0).ToString("0.0") + " MB"); }
                        }
                    }
                    File.Copy(tmp, dest, true);
                    try { File.Delete(tmp); } catch { }
                }
            }
        }

        // ---------- 解压 ----------

        public static int ExtractZip(string zipPath, string gameDir, ModPackage p, Action<string> log)
        {
            string baseDir;
            if (p.Mode == 1)
                baseDir = Path.Combine(gameDir, "BepInEx", "plugins", string.IsNullOrEmpty(p.Folder) ? "Mod" : p.Folder);
            else if (p.Mode == 2)
                baseDir = Path.Combine(gameDir, "BepInEx");   // 包内是 patchers/ plugins/ 这种相对 BepInEx 的布局
            else
                baseDir = gameDir;
            Directory.CreateDirectory(baseDir);
            char sep = Path.DirectorySeparatorChar;
            string baseFull = Path.GetFullPath(baseDir).TrimEnd(sep, '/');
            string baseGuard = baseFull + sep;
            int files = 0;

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(p.StripPrefix))
                    {
                        if (!name.StartsWith(p.StripPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        name = name.Substring(p.StripPrefix.Length);
                    }
                    name = name.TrimStart('/');
                    if (name.Length == 0) continue;

                    bool isDir = name.EndsWith("/");
                    string rel = name.Replace('/', sep).TrimEnd(sep);
                    if (rel.Length == 0) { if (isDir) Directory.CreateDirectory(baseFull); continue; }
                    // 过滤包根目录的说明/图标等元数据，避免丢进游戏根目录
                    if ((p.Mode == 0 || p.Mode == 2) && string.IsNullOrEmpty(p.StripPrefix) && !name.Contains("/"))
                    {
                        string lower = rel.ToLowerInvariant();
                        if (lower == "manifest.json" || lower == "icon.png" || lower == "readme.md" ||
                            lower == "changelog.md" || lower.StartsWith("license")) continue;
                    }
                    string dest = Path.GetFullPath(Path.Combine(baseFull, rel));
                    if (!dest.StartsWith(baseGuard, StringComparison.OrdinalIgnoreCase)) continue;

                    if (isDir) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    files++;
                }
            }
            log("  解压完成：" + files + " 个文件 → " + (p.Mode == 1 ? "BepInEx\\plugins\\" + p.Folder : "游戏根目录"));
            return files;
        }

        // ---------- 安装 / 卸载 ----------

        public static string BackupExisting(string gameDir, Action<string> log)
        {
            string bep = Path.Combine(gameDir, "BepInEx");
            if (!Directory.Exists(bep)) return null;
            string bak = Path.Combine(gameDir, "BepInEx_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                Directory.Move(bep, bak);
                log("已备份原有 BepInEx → " + Path.GetFileName(bak));
                return bak;
            }
            catch (Exception ex)
            {
                log("备份失败（将直接覆盖）：" + ex.Message);
                return null;
            }
        }

        public static void Uninstall(string gameDir, Action<string> log)
        {
            string[] targets = { "BepInEx", "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" };
            foreach (var t in targets)
            {
                string path = Path.Combine(gameDir, t);
                try
                {
                    if (Directory.Exists(path)) { Directory.Delete(path, true); log("已删除目录：" + t); }
                    else if (File.Exists(path)) { File.Delete(path); log("已删除文件：" + t); }
                }
                catch (Exception ex) { log("删除失败 " + t + "：" + ex.Message); }
            }
        }

        public static bool GameRunning()
        {
            try { return Process.GetProcessesByName("REPO").Length > 0; } catch { return false; }
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                string f = Path.Combine(dir, ".write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(f, "x");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }

        // ---------- 只保留最新版 / 备份轮转 / 校验 ----------

        private static readonly string[] ManagedPluginDirs =
        {
            "REPOLib", "MenuLib", "REPOConfig", "WeaponSpawner", "NoItemSpawnLimit",
            "SpawnManager",             "MorePlayersSimple",             "REPOFidelity", "ClearUI", "REPOFontChanger",
            "XUnity.AutoTranslator", "XUnity.ResourceRedirector"
        };

        /// <summary>删掉受管 mod 的旧文件，避免新旧版本共存</summary>
        public static void CleanManaged(string gameDir, Action<string> log)
        {
            string plugins = Path.Combine(gameDir, "BepInEx", "plugins");
            foreach (var d in ManagedPluginDirs)
            {
                string path = Path.Combine(plugins, d);
                try
                {
                    if (Directory.Exists(path)) { Directory.Delete(path, true); log("  清理旧插件目录：" + d); }
                }
                catch (Exception ex) { log("  清理失败 " + d + "：" + ex.Message); }
            }
            string[] files =
            {
                Path.Combine(gameDir, "BepInEx", "core", "XUnity.Common.dll"),
                Path.Combine(gameDir, "BepInEx", "patchers", "Font.dll")
            };
            foreach (var f in files)
            {
                try { if (File.Exists(f)) { File.Delete(f); log("  清理旧文件：" + Path.GetFileName(f)); } }
                catch { }
            }
            try
            {
                string tr = Path.Combine(gameDir, "BepInEx", "config", "Translation");
                if (Directory.Exists(tr)) { Directory.Delete(tr, true); log("  清理旧语言包目录：Translation"); }
            }
            catch { }
        }

        /// <summary>备份目录只保留最近 N 份</summary>
        public static void RotateBackups(string gameDir, int keep, Action<string> log)
        {
            try
            {
                var dirs = Directory.GetDirectories(gameDir, "BepInEx_backup_*")
                                    .OrderByDescending(d => d)
                                    .ToList();
                for (int i = keep; i < dirs.Count; i++)
                {
                    try { Directory.Delete(dirs[i], true); log("  删除旧备份：" + Path.GetFileName(dirs[i])); }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>缓存目录里只保留本次用到的包</summary>
        public static void TrimCache(string cacheDir, IEnumerable<string> keepFiles, Action<string> log)
        {
            try
            {
                var keep = new HashSet<string>(keepFiles.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFileName),
                                               StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(cacheDir, "*.zip"))
                {
                    if (!keep.Contains(Path.GetFileName(f)))
                    {
                        try { File.Delete(f); log("  清理旧缓存：" + Path.GetFileName(f)); } catch { }
                    }
                }
            }
            catch { }
        }

        public static string Sha256Of(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------- 已安装检测 ----------

        public enum PkgState { Missing, Installed, Outdated }

        public static string InstalledFile(string gameDir)
        {
            return Path.Combine(gameDir, "BepInEx", "repomods-installed.json");
        }

        public static Dictionary<string, string> ReadInstalledMap(string gameDir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = InstalledFile(gameDir);
                if (File.Exists(f))
                {
                    var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f));
                    if (d != null) foreach (var kv in d) map[kv.Key] = kv.Value;
                }
            }
            catch { }
            return map;
        }

        public static void RecordInstalled(string gameDir, IEnumerable<KeyValuePair<string, string>> items)
        {
            try
            {
                var map = ReadInstalledMap(gameDir);
                foreach (var kv in items) map[kv.Key] = kv.Value ?? "";
                string f = InstalledFile(gameDir);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, System.Text.Json.JsonSerializer.Serialize(map,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>判定包在本机的状态：未安装 / 已安装 / 已安装但版本落后</summary>
        public static PkgState GetState(string gameDir, ModPackage p, string latestVersion,
                                        Dictionary<string, string> installedMap, out string installedVersion)
        {
            installedVersion = null;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return PkgState.Missing;
            if (string.IsNullOrEmpty(p.Marker)) return PkgState.Missing;
            string mpath = Path.Combine(gameDir, p.Marker);
            bool present = File.Exists(mpath);
            if (!present && Directory.Exists(mpath))
            {
                // 目录级判定：目录在、且里面有 dll 就算已安装（不依赖具体文件名，作者改名也不受影响）
                try { present = Directory.GetFiles(mpath, "*.dll", SearchOption.AllDirectories).Length > 0; }
                catch { present = false; }
            }
            if (!present) return PkgState.Missing;
            if (installedMap != null) installedMap.TryGetValue(p.Key, out installedVersion);
            if (!string.IsNullOrEmpty(latestVersion) && !string.IsNullOrEmpty(installedVersion) &&
                !string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
                return PkgState.Outdated;
            return PkgState.Installed;
        }

        /// <summary>
        /// 增量安装：只清理"本次要装的那个包"自己的目录，其余已装 mod 原样保留。
        /// 这是修复"装新 mod 把旧 mod 全删掉"的关键——不再整体重命名 BepInEx。
        /// </summary>
        public static void PrepareIncremental(string gameDir, ModPackage p, Action<string> log)
        {
            if (p == null) return;

            if (p.Mode == 1 && !string.IsNullOrEmpty(p.Folder))
            {
                string dir = Path.Combine(gameDir, "BepInEx", "plugins", p.Folder);
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                        log("  清理旧版本目录：" + p.Folder);
                    }
                }
                catch (Exception ex) { log("  清理旧版本失败（将直接覆盖）：" + p.Folder + " — " + ex.Message); }
                return;
            }

            // 根目录布局的包：只清它自己的旧语言包，不碰其它 mod
            if (p.Key == "ChineseTranslation")
            {
                try
                {
                    string tr = Path.Combine(gameDir, "BepInEx", "config", "Translation");
                    if (Directory.Exists(tr)) { Directory.Delete(tr, true); log("  清理旧语言包目录：Translation"); }
                }
                catch { }
            }
        }

        /// <summary>安装后自检：标志文件/目录是否到位</summary>
        public static bool MarkerPresent(string gameDir, ModPackage p)
        {
            if (p == null || string.IsNullOrEmpty(p.Marker)) return false;
            string mpath = Path.Combine(gameDir ?? "", p.Marker);
            if (File.Exists(mpath)) return true;
            try
            {
                return Directory.Exists(mpath) &&
                       Directory.GetFiles(mpath, "*.dll", SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

    }
}
