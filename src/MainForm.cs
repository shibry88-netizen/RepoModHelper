using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RepoInstaller
{
    internal sealed class MainForm : Form
    {
        private TextBox _txtDir;
        private Label _lblVer, _lblStatus, _lblCloud;
        private CheckedListBox _clb;
        private ProgressBar _bar;
        private RichTextBox _log;
        private Button _btnInstall, _btnUninstall, _btnBrowse, _btnAuto, _btnOpen, _btnAll, _btnNone;
        private CancellationTokenSource _cts;
        private bool _busy;
        private string _gameDir = "";
        private bool _relayAlive = false;
        private CloudManifest _manifestCache;
        private Dictionary<string, string> _installedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CheckBox _chkShowAll;
        private bool _showInstalled = false;

        public MainForm()
        {
            Text = "R.E.P.O. 一键 Mod 安装器 v2.4.2（云端镜像 + 中转加速 · 增量安装 · 适配 0.5.x）";
            ClientSize = new Size(760, 690);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(24, 26, 30);
            ForeColor = Color.Gainsboro;
            Font = PickFont();

            BuildUi();
            Load += async (s, e) =>
            {
                await AutoDetectAsync();
                await ResolveCloudAsync();
                await ProbeCloudAsync();
                await StartupChecksAsync();
            };
        }

        private static Font PickFont()
        {
            foreach (var name in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "Segoe UI" })
            {
                try
                {
                    var f = new Font(name, 9F);
                    if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return f;
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, 9F);
        }

        private void BuildUi()
        {
            var lblTitle = new Label
            {
                Text = "R.E.P.O. 一键 Mod 安装器",
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 220, 200),
                Location = new Point(16, 12),
                Size = new Size(720, 30)
            };

            var lblSub = new Label
            {
                Text = "刷物品(F6) + 高清画面 + 高清UI + 汉化　|　云端节点加速 + 中转站转发（网吧/无 VPN 也能快）",
                ForeColor = Color.Silver,
                Location = new Point(16, 44),
                Size = new Size(730, 20)
            };

            var grpDir = new GroupBox
            {
                Text = "① 游戏目录",
                ForeColor = Color.Gainsboro,
                Location = new Point(16, 74),
                Size = new Size(728, 96)
            };

            _txtDir = new TextBox
            {
                Location = new Point(12, 26),
                Size = new Size(500, 24),
                ReadOnly = true,
                BackColor = Color.FromArgb(40, 42, 48),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnAuto = FlatBtn("自动检测", 520, 25, 90);
            _btnAuto.Click += async (s, e) => await AutoDetectAsync(true);

            _btnBrowse = FlatBtn("浏览…", 618, 25, 96);
            _btnBrowse.Click += (s, e) => BrowseDir();

            _lblVer = new Label
            {
                Text = "版本：—",
                Location = new Point(12, 60),
                Size = new Size(700, 20),
                ForeColor = Color.FromArgb(160, 170, 180)
            };

            grpDir.Controls.AddRange(new Control[] { _txtDir, _btnAuto, _btnBrowse, _lblVer });

            var grpMods = new GroupBox
            {
                Text = "② 要安装的组件",
                ForeColor = Color.Gainsboro,
                Location = new Point(16, 178),
                Size = new Size(728, 214)
            };

            _clb = new CheckedListBox
            {
                Location = new Point(12, 24),
                Size = new Size(560, 176),
                CheckOnClick = true,
                BackColor = Color.FromArgb(40, 42, 48),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            foreach (var p in Core.Packages) _clb.Items.Add(p, !p.DefaultOff);   // 作者限制分发的包默认不勾
            _clb.ItemCheck += (s, e) =>
            {
                if (_clb.Items[e.Index] is ModPackage p && p.Required && e.NewValue == CheckState.Unchecked)
                    e.NewValue = CheckState.Checked;
            };

            _btnAll = FlatBtn("全选", 584, 24, 128);
            _btnAll.Click += (s, e) => { for (int i = 0; i < _clb.Items.Count; i++) _clb.SetItemChecked(i, true); };
            _btnNone = FlatBtn("全不选", 584, 58, 128);
            _btnNone.Click += (s, e) => { for (int i = 0; i < _clb.Items.Count; i++) if (!(_clb.Items[i] as ModPackage).Required) _clb.SetItemChecked(i, false); };

            var lblTip = new Label
            {
                Text = "优先走云端节点，\r\n失败自动回退源站。\r\n\r\n只保留最新版，\r\n旧版会被清理。",
                Location = new Point(584, 92),
                Size = new Size(132, 110),
                ForeColor = Color.FromArgb(140, 150, 160)
            };

            grpMods.Controls.AddRange(new Control[] { _clb, _btnAll, _btnNone, lblTip });

            _lblCloud = new Label
            {
                Text = "云端：检测中…",
                Location = new Point(16, 396),
                Size = new Size(496, 20),
                ForeColor = Color.FromArgb(120, 220, 200)
            };

            _chkShowAll = new CheckBox
            {
                Text = "显示已安装的模组",
                Location = new Point(520, 392),
                Size = new Size(224, 24),
                ForeColor = Color.Gainsboro
            };
            _chkShowAll.CheckedChanged += (o, ev) => { _showInstalled = _chkShowAll.Checked; RefreshPackageList(); };

            _bar = new ProgressBar
            {
                Location = new Point(16, 422),
                Size = new Size(728, 20),
                Style = ProgressBarStyle.Continuous,
                Maximum = 100
            };

            _lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(16, 448),
                Size = new Size(728, 20),
                ForeColor = Color.FromArgb(120, 220, 200)
            };

            _log = new RichTextBox
            {
                Location = new Point(16, 472),
                Size = new Size(728, 148),
                ReadOnly = true,
                BackColor = Color.FromArgb(16, 18, 21),
                ForeColor = Color.FromArgb(180, 200, 190),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5F),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = false
            };

            _btnInstall = FlatBtn("③ 一键安装 / 更新", 16, 632, 240, true);
            _btnInstall.Click += async (s, e) => await InstallClickAsync();

            _btnUninstall = FlatBtn("卸载 / 还原原版", 268, 632, 150);
            _btnUninstall.Click += (s, e) => UninstallClick();

            _btnOpen = FlatBtn("打开游戏目录", 430, 632, 130);
            _btnOpen.Click += (s, e) =>
            {
                if (Directory.Exists(_gameDir)) Process.Start("explorer.exe", "\"" + _gameDir + "\"");
            };

            var btnCopy = FlatBtn("复制日志", 572, 632, 90);
            btnCopy.Click += (s, e) =>
            {
                try { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); } catch { }
            };

            var btnQuit = FlatBtn("关闭", 674, 632, 70);
            btnQuit.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                lblTitle, lblSub, grpDir, grpMods, _lblCloud, _chkShowAll, _bar, _lblStatus, _log,
                _btnInstall, _btnUninstall, _btnOpen, btnCopy, btnQuit
            });

            Log("欢迎使用 RepoModHelper v2.0。安装优先走云端节点（二号机），失败自动回退 Thunderstore。");
            Log("");
        }

        private Button FlatBtn(string text, int x, int y, int w, bool primary = false, int h = 38)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(42, 120, 105) : Color.FromArgb(48, 51, 58),
                ForeColor = primary ? Color.White : Color.Gainsboro,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 75, 85);
            b.FlatAppearance.BorderSize = 1;
            return b;
        }

        // ---------- 线程安全 UI ----------

        private void Ui(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch { }
        }

        private void Log(string msg)
        {
            if (msg == null) return;
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void SetStatus(string s) => _lblStatus.Text = s;

        private DialogResult Ask(string msg, string title = "请确认")
        {
            if (InvokeRequired)
                return (DialogResult)Invoke(new Func<DialogResult>(() =>
                    MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning)));
            return MessageBox.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        }

        private void Info(string msg, string title = "提示")
        {
            Ui(() => MessageBox.Show(this, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        // ---------- 探测 ----------

        private async Task AutoDetectAsync(bool manual = false)
        {
            if (_busy) return;
            SetStatus("正在查找游戏目录…");
            if (manual) Log("— 重新检测游戏目录 —");

            var list = await Task.Run(() => Core.DetectGameDirs(s => Ui(() => SetStatus("检测中：" + s))));
            if (list.Count == 0)
            {
                SetStatus("未自动找到 R.E.P.O.，请点“浏览…”手动选择含 REPO.exe 的文件夹");
                Log("❌ 未自动找到游戏目录。请点“浏览…”选择 steamapps\\common\\REPO。");
                return;
            }
            SetDir(list[0]);
            Log("✅ 找到游戏目录：" + list[0]);
            if (list.Count > 1)
            {
                Log("  另外还发现 " + (list.Count - 1) + " 个候选目录，如需切换请点“浏览…”：");
                foreach (var d in list.Skip(1)) Log("   · " + d);
            }
            SetStatus("已就绪，可以安装");
        }

        private async Task ResolveCloudAsync()
        {
            try
            {
                string name = await Cloud.InitAsync(msg => Log(msg));
                Ui(() => _lblCloud.Text = "云端节点：" + name + " · 正在连接…");
            }
            catch (Exception ex)
            {
                Log("⚠ 节点解析失败，回退内置地址：" + ex.Message);
            }
        }

        private async Task ProbeCloudAsync()
        {
            try
            {
                var st = await Cloud.StatusAsync(CancellationToken.None);
                Ui(() =>
                {
                    _lblCloud.Text = "云端：state=" + st.state + " · 镜像游戏版本 " + (st.game_version ?? "?") +
                                     " · 检查于 " + (st.checked_at ?? "?");
                    _lblCloud.ForeColor = st.state == "ok" ? Color.FromArgb(120, 220, 200) : Color.FromArgb(240, 190, 110);
                });
                Log("云端节点在线：" + st.state + " — " + st.message);

                _relayAlive = await Cloud.RelayAliveAsync(CancellationToken.None);
                Ui(() => Log(_relayAlive
                    ? "中转站可用：Thunderstore 下载将经 " + Cloud.NodeName + " 加速"
                    : "⚠ 中转站不可用（/relay/healthz 无响应），将直连源站"));
            }
            catch (Exception ex)
            {
                Ui(() =>
                {
                    _lblCloud.Text = "云端：不可达（将直连 Thunderstore 安装）";
                    _lblCloud.ForeColor = Color.FromArgb(230, 130, 130);
                });
                Log("⚠ 云端节点不可达：" + ex.Message);
            }
        }

        private void SetDir(string dir)
        {
            _gameDir = dir ?? "";
            Ui(() =>
            {
                _txtDir.Text = _gameDir;
                _lblVer.Text = "版本：R.E.P.O. " + Core.GameVersion(_gameDir);
            });
        }

        private void BrowseDir()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择 R.E.P.O. 游戏目录（里面能看到 REPO.exe）";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string dir = dlg.SelectedPath;
                if (!Core.IsGameDir(dir))
                {
                    var r = MessageBox.Show(
                        "这个文件夹里没找到 REPO.exe。\n\n仍要继续吗？",
                        "目录可能不正确", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r != DialogResult.Yes) return;
                }
                SetDir(dir);
                Log("手动选择目录：" + dir);
            }
        }

        // ---------- 安装 ----------

        private async Task InstallClickAsync()
        {
            if (_busy) { MessageBox.Show("正在处理中，请稍候…", "请稍候"); return; }

            if (string.IsNullOrEmpty(_gameDir) || !Directory.Exists(_gameDir))
            {
                MessageBox.Show("请先确认游戏目录（点“自动检测”或“浏览…”）。", "还没选目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Core.GameRunning())
            {
                MessageBox.Show("检测到游戏正在运行，请先完全退出 R.E.P.O.，再点安装。", "游戏正在运行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selected = new List<ModPackage>();
            foreach (var item in _clb.CheckedItems) selected.Add((ModPackage)item);
            if (selected.Count == 0) { MessageBox.Show("至少要勾选一个组件。"); return; }

            if (!Core.CanWrite(_gameDir))
            {
                MessageBox.Show(
                    "没有写入权限，或该目录被还原/保护软件锁定了：\n" + _gameDir +
                    "\n\n处理办法：\n① 右键本程序 → 以管理员身份运行；\n② 网吧还原系统可能只读，请联系网管。",
                    "无法写入", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _busy = true;
            _cts = new CancellationTokenSource();
            SetBusy(true);

            try { await InstallFlowAsync(selected); }
            catch (OperationCanceledException) { Log("已取消。"); SetStatus("已取消"); }
            catch (Exception ex)
            {
                Log("❌ 出错：" + ex.Message);
                SetStatus("安装失败：" + ex.Message);
                TryWriteLogFile();
                MessageBox.Show("安装过程中出错：\n" + ex.Message + "\n\n详细信息见下方日志，可点“复制日志”。",
                    "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
                SetBusy(false);
                TryWriteLogFile();
            }
        }

        private void SetBusy(bool busy)
        {
            _btnInstall.Enabled = !busy;
            _btnUninstall.Enabled = !busy;
            _btnBrowse.Enabled = !busy;
            _btnAuto.Enabled = !busy;
            _btnAll.Enabled = !busy;
            _btnNone.Enabled = !busy;
        }

        /// <summary>等待云端镜像拉取完成，每 10 秒轮询一次</summary>
        private async Task<CloudStatus> WaitForMirrorAsync(int maxSeconds)
        {
            CloudStatus last = null;
            int rounds = maxSeconds / 10;
            for (int i = 1; i <= rounds; i++)
            {
                await Task.Delay(10000, _cts.Token);
                try
                {
                    last = await Cloud.StatusAsync(_cts.Token);
                    int waited = i * 10;
                    Ui(() =>
                    {
                        SetStatus("等待云端拉取镜像…（已等待 " + waited + " 秒，state=" + last.state + "）");
                        _lblCloud.Text = "云端：" + last.state + " · " + last.message;
                    });
                    if (last.state != "mirror_pulling") return last;
                }
                catch (Exception ex)
                {
                    Ui(() => Log("  第 " + i + " 次轮询失败：" + ex.Message));
                }
            }
            return last;
        }

        private async Task InstallFlowAsync(List<ModPackage> list)
        {
            string dir = _gameDir;
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string cache = Core.PickCacheDir(exeDir);

            Ui(() =>
            {
                Log("");
                Log("================ 开始安装 ================");
                Log("游戏目录：" + dir);
                Log("缓存目录：" + cache);
            });

            bool useCloud = false;
            CloudManifest manifest = null;

            // ---- A. 云端状态机 ----
            try
            {
                Ui(() => SetStatus("连接云端节点 " + Cloud.NodeName + " …"));
                var st = await Cloud.StatusAsync(_cts.Token);
                Ui(() =>
                {
                    _lblCloud.Text = "云端：state=" + st.state + " · 镜像游戏 " + st.game_version;
                    Log("云端状态：" + st.state + " — " + st.message);
                });
                useCloud = true;

                if (st.state == "mirror_stale")
                {
                    var r = Ask("云端镜像同步失败：\n" + st.message + "\n\n是否改为直连源站（Thunderstore）安装？");
                    if (r != DialogResult.Yes) { Ui(() => SetStatus("已取消")); return; }
                    useCloud = false;
                }
                else if (st.state == "game_only")
                {
                    var r = Ask("游戏已更新到 " + st.game_version + "，MOD 作者暂未更新，请耐心等待更新。\n\n" +
                                "现在装的是为旧版本写的 MOD，进入游戏可能崩溃或功能失效。\n\n仍要继续安装吗？");
                    if (r != DialogResult.Yes) { Ui(() => SetStatus("已取消：等待 MOD 作者更新")); return; }
                }
                else if (st.state == "mirror_pulling")
                {
                    Info("云端服务器正在拉取镜像，请耐心等待…\n\n" +
                         "检测到新版 MOD，云端正在从源站同步。程序每 10 秒检查一次，拉取完成后会自动继续安装。\n（最长等待 10 分钟）");
                    var done = await WaitForMirrorAsync(600);
                    if (done == null || done.state == "mirror_pulling")
                    {
                        var r = Ask("云端镜像同步超时（已等 10 分钟）。\n\n是否改为直连 Thunderstore 安装？");
                        if (r != DialogResult.Yes) { Ui(() => SetStatus("已取消")); return; }
                        useCloud = false;
                    }
                    else if (done.state == "mirror_stale")
                    {
                        var r = Ask("云端同步失败了：" + done.message + "\n\n是否改为直连 Thunderstore 安装？");
                        if (r != DialogResult.Yes) return;
                        useCloud = false;
                    }
                    else
                    {
                        Ui(() => Log("✅ 云端镜像已拉取完成（state=" + done.state + "），继续安装"));
                    }
                }
            }
            catch (Exception ex)
            {
                Ui(() =>
                {
                    _lblCloud.Text = "云端：不可达 → 直连源站";
                    _lblCloud.ForeColor = Color.FromArgb(230, 130, 130);
                    Log("⚠ 云端节点不可达：" + ex.Message + " → 直连 Thunderstore");
                });
                useCloud = false;
            }

            // ---- B. 取 manifest ----
            if (useCloud)
            {
                try
                {
                    manifest = await Cloud.ManifestAsync(_cts.Token);
                    Ui(() => Log("云端 manifest：游戏 " + manifest.game_version + "，同步于 " + manifest.synced_at +
                                 "，共 " + manifest.packages.Count + " 个包"));
                    string local = Core.GameVersion(dir);
                    if (!string.IsNullOrEmpty(manifest.game_version) && local.Contains(".") &&
                        string.CompareOrdinal(local, manifest.game_version) > 0)
                        Ui(() => Log("⚠ 本机游戏版本(" + local + ")可能高于云端镜像(" + manifest.game_version + ")，若报错请等云端同步"));
                }
                catch (Exception ex)
                {
                    Ui(() => Log("⚠ 拉取 manifest 失败：" + ex.Message + " → 直连 Thunderstore"));
                    useCloud = false;
                }
            }

            // ---- C. 备份 / 清理旧版 ----
            Ui(() => SetStatus("备份原有文件…"));
            // 增量安装：不再整体重命名/清空 BepInEx，只处理本次勾选的包
            Ui(() => Log("安装方式：增量（其余已装 mod 原样保留）"));

            // ---- D. 逐包下载 + 解压 ----
            int total = list.Count;
            var usedFiles = new List<string>();

            for (int i = 0; i < total; i++)
            {
                var p = list[i];
                int basePct = (int)Math.Round(i * 100.0 / total);
                string shortName = p.Name.Split('←')[0].Trim();
                Ui(() =>
                {
                    _bar.Value = Math.Min(100, basePct);
                    SetStatus("[" + (i + 1) + "/" + total + "] " + shortName);
                    Log("→ " + p.Name);
                });

                var cp = useCloud ? manifest?.Find(p.Key) : null;
                string cacheName = (cp != null && !string.IsNullOrEmpty(cp.file)) ? cp.file : p.FileName;
                string expectSha = cp?.sha256;

                // 云端给了 sha256 就必须哈希一致才复用缓存，避免装回旧版本
                string zip = null;
                if (!string.IsNullOrEmpty(expectSha))
                {
                    string cand = Path.Combine(cache, cacheName);
                    if (File.Exists(cand) && string.Equals(Core.Sha256Of(cand), expectSha, StringComparison.OrdinalIgnoreCase))
                        zip = cand;
                }
                else
                {
                    zip = Core.FindUsableZip(cache, p);
                }

                if (zip != null)
                {
                    Ui(() => Log("  使用本地缓存：" + Path.GetFileName(zip)));
                }
                else
                {
                    string dest = Path.Combine(cache, cacheName);
                    string url;
                    string label;

                    if (cp != null && !string.IsNullOrEmpty(cp.url))
                    {
                        url = cp.url;
                        expectSha = cp.sha256;
                        label = "云端 " + cp.version;
                    }
                    else
                    {
                        url = p.Url;
                        label = "源站直连";
                    }

                    var prog = new Progress<double>(v =>
                    {
                        int pct = Math.Min(100, basePct + (int)(v * 85.0 / total));
                        Ui(() => { if (pct > _bar.Value) _bar.Value = pct; });
                    });

                    // 优先经二号中转（国内直连 Thunderstore 慢），失败自动回退直连
                    await DownloadWithFallbackAsync(url, dest, prog, label);
                    zip = dest;
                }

                // 校验 sha256（云端提供）
                if (!string.IsNullOrEmpty(expectSha))
                {
                    string got = Core.Sha256Of(zip);
                    if (!string.Equals(got, expectSha, StringComparison.OrdinalIgnoreCase))
                    {
                        Ui(() => Log("  ⚠ 校验失败，重新下载该包…"));
                        try { File.Delete(zip); } catch { }
                        await DownloadWithFallbackAsync(p.Url, zip, new Progress<double>(_ => { }), "重试下载");
                    }
                    else Ui(() => Log("  ✔ sha256 校验通过"));
                }

                usedFiles.Add(zip);

                Ui(() => _bar.Value = Math.Min(100, basePct + (int)(85.0 / total)));
                string zipToUse = zip;
                await Task.Run(() => Core.PrepareIncremental(dir, p, s => Ui(() => Log(s))));
                await Task.Run(() => Core.ExtractZip(zipToUse, dir, p, s => Ui(() => Log(s))));
                if (!Core.MarkerPresent(dir, p))
                    Ui(() => Log("  ⚠ 解压后未在预期位置找到该 mod，可能被杀软拦截或包结构变化"));
                Ui(() => _bar.Value = Math.Min(100, (int)Math.Round((i + 1) * 100.0 / total)));
            }

            await Task.Run(() => Core.TrimCache(cache, usedFiles, s => Ui(() => Log(s))));

            // 记录已安装版本（供下次启动的“已安装/未安装”检测）
            var rec = new List<KeyValuePair<string, string>>();
            foreach (var p in list)
            {
                var cpv = useCloud ? manifest?.Find(p.Key) : null;
                rec.Add(new KeyValuePair<string, string>(p.Key, cpv?.version ?? ""));
            }
            await Task.Run(() => Core.RecordInstalled(dir, rec));

            // ---- E. 校验 ----
            Ui(() => SetStatus("校验安装结果…"));
            bool hasBep = File.Exists(Path.Combine(dir, "BepInEx", "core", "BepInEx.dll"));
            bool hasLoader = File.Exists(Path.Combine(dir, "winhttp.dll"));
            bool hasWs = Directory.Exists(Path.Combine(dir, "BepInEx", "plugins", "WeaponSpawner"));

            Ui(() =>
            {
                Log("");
                Log("---------------- 结果 ----------------");
                Log((hasBep ? "✅" : "❌") + " BepInEx 核心");
                Log((hasLoader ? "✅" : "❌") + " 注入器 winhttp.dll" + (hasLoader ? "" : "（缺失，检查杀毒拦截）"));
                if (hasWs) Log("✅ WeaponSpawner 已就位（进关卡按 F6）");
                Log("");
                Log("用法：从 Steam 正常启动游戏，进关卡后按 F6。");
                Log("机房提示：还原/无盘系统重启会清空，下次上机重跑一次即可（有缓存很快）。");
                _bar.Value = 100;
                SetStatus("安装完成 ✔");
            });

            RefreshPackageList();

            Info("安装完成！\n\n1) 从 Steam 启动 R.E.P.O.\n2) 进关卡后按 F6 打开刷物品菜单\n\n" +
                 "数据源：" + (manifest != null ? "云端节点 " + Cloud.NodeName : "Thunderstore 直连") +
                 "\n本次安装的均为最新版，旧版已清理。", "完成");
        }


        /// <summary>下载：优先经二号中转，失败回退 Thunderstore 直连</summary>
        private async Task DownloadWithFallbackAsync(string url, string dest, IProgress<double> prog, string label)
        {
            string relay = Cloud.RelayUrl(url);
            var attempts = new List<(string u, string lab)>();
            if (relay != null && _relayAlive)
                attempts.Add((relay, label + " · 经二号中转"));
            attempts.Add((url, label + " · 直连源站"));

            Exception last = null;
            foreach (var a in attempts)
            {
                try
                {
                    Ui(() => Log("  来源：" + a.lab));
                    await Core.DownloadAsync(a.u, dest, prog, x => Ui(() => Log(x)), _cts.Token);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    Ui(() => Log("  该来源失败：" + ex.Message));
                }
            }
            throw last ?? new Exception("下载失败");
        }

        private void TryWriteLogFile()
        {
            try
            {
                if (string.IsNullOrEmpty(_gameDir) || !Directory.Exists(_gameDir)) return;
                File.WriteAllText(Path.Combine(_gameDir, "REPO_Mod_Installer.log"), _log.Text, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ---------- 启动检测：已安装 / 公告 ----------

        private async Task StartupChecksAsync()
        {
            RefreshPackageList();
            try
            {
                _manifestCache = await Cloud.ManifestAsync(CancellationToken.None);
                RefreshPackageList();

                var ann = _manifestCache?.announcement;
                if (ann != null && !string.IsNullOrEmpty(ann.id))
                {
                    var st = ClientState.Load();
                    if (!string.Equals(st.seenAnnouncement, ann.id, StringComparison.Ordinal))
                    {
                        ShowAnnouncementDialog(ann);
                        st.seenAnnouncement = ann.id;
                        ClientState.Save(st);
                        Log("已展示云端公告（id=" + ann.id + "），下次不再重复弹出");
                    }
                    else Log("云端名单无新变化（公告已看过）");
                }
            }
            catch (Exception ex) { Log("（名单/公告检查失败：" + ex.Message + "）"); }
        }

        private string NameOf(string key)
        {
            foreach (var p in Core.Packages)
                if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                    return p.Name.Split('←')[0].Trim();
            return key;
        }

        private void ShowAnnouncementDialog(Announcement a)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.IsNullOrEmpty(a.title) ? "云端 Mod 名单已更新" : a.title);
            if (!string.IsNullOrEmpty(a.created_at)) sb.AppendLine("（" + a.created_at + "）");
            sb.AppendLine();

            if (a.added != null && a.added.Count > 0)
            {
                sb.AppendLine("✅ 新增 " + a.added.Count + " 个：");
                foreach (var i in a.added) sb.AppendLine("   · " + NameOf(i.key) + "  " + i.version);
                sb.AppendLine();
            }
            if (a.removed != null && a.removed.Count > 0)
            {
                sb.AppendLine("🗑 移除 " + a.removed.Count + " 个：");
                foreach (var i in a.removed) sb.AppendLine("   · " + NameOf(i.key));
                sb.AppendLine();
            }
            if (a.updated != null && a.updated.Count > 0)
            {
                sb.AppendLine("⬆ 版本更新 " + a.updated.Count + " 个：");
                foreach (var u in a.updated) sb.AppendLine("   · " + NameOf(u.key) + "  " + u.from + " → " + u.to);
                sb.AppendLine();
            }
            sb.AppendLine("点「一键安装」即可补齐未安装 / 需要更新的模组。");

            Ui(() => MessageBox.Show(this, sb.ToString(), "云端更新公告", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        /// <summary>每次启动与安装后重新检测，默认只显示未安装与可更新的包</summary>
        private void RefreshPackageList()
        {
            _installedMap = Core.ReadInstalledMap(_gameDir);
            var show = new List<ModPackage>();
            int installed = 0, outdated = 0, missing = 0;

            foreach (var p in Core.Packages)
            {
                string latest = _manifestCache?.Find(p.Key)?.version;
                string iv;
                var stt = Core.GetState(_gameDir, p, latest, _installedMap, out iv);
                p.StatusText = null;

                if (stt == Core.PkgState.Installed)
                {
                    installed++;
                    p.StatusText = "已安装";
                    if (_showInstalled) show.Add(p);
                }
                else if (stt == Core.PkgState.Outdated)
                {
                    outdated++;
                    p.StatusText = "可更新 " + iv + "→" + latest;
                    show.Add(p);
                }
                else
                {
                    missing++;
                    p.StatusText = "未安装";
                    show.Add(p);
                }
            }

            _clb.BeginUpdate();
            _clb.Items.Clear();
            foreach (var p in show) _clb.Items.Add(p, !p.DefaultOff);
            _clb.EndUpdate();

            if (string.IsNullOrEmpty(_gameDir) || !Directory.Exists(_gameDir))
                _lblStatus.Text = "请先选择游戏目录";
            else
                _lblStatus.Text = "已安装 " + installed + " 个 · 可更新 " + outdated + " 个 · 未安装 " + missing + " 个";
        }

        // ---------- 卸载 ----------

        private void UninstallClick()
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_gameDir) || !Directory.Exists(_gameDir))
            {
                MessageBox.Show("请先选择游戏目录。");
                return;
            }
            if (Core.GameRunning())
            {
                MessageBox.Show("请先完全退出游戏。", "游戏正在运行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var r = MessageBox.Show(
                "将删除：BepInEx 文件夹、winhttp.dll、doorstop_config.ini\n（有备份会自动还原）\n\n确定还原成原版吗？",
                "卸载 / 还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r != DialogResult.OK) return;

            try
            {
                Core.Uninstall(_gameDir, s => Log(s));
                try
                {
                    var bak = Directory.GetDirectories(_gameDir, "BepInEx_backup_*").OrderByDescending(d => d).FirstOrDefault();
                    if (bak != null)
                    {
                        string target = Path.Combine(_gameDir, "BepInEx");
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        Directory.Move(bak, target);
                        Log("已还原备份：" + Path.GetFileName(bak));
                    }
                }
                catch (Exception ex) { Log("还原备份失败：" + ex.Message); }

                Log("✅ 已卸载，游戏恢复原版。");
                SetStatus("已还原原版");
                MessageBox.Show("已还原为原版。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("❌ 卸载失败：" + ex.Message);
                MessageBox.Show("卸载失败：\n" + ex.Message, "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
