using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RepoInstaller
{
    internal sealed class CloudPackage
    {
        public string key { get; set; }
        public string version { get; set; }
        public string file { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
    }

    internal sealed class CloudManifest
    {
        public int schema { get; set; }
        public string game_version { get; set; }
        public string synced_at { get; set; }
        public List<CloudPackage> packages { get; set; } = new List<CloudPackage>();
        public Announcement announcement { get; set; }

        public CloudPackage Find(string key)
        {
            if (packages == null) return null;
            foreach (var p in packages)
                if (string.Equals(p.key, key, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }
    }

    internal sealed class CloudStatus
    {
        public string state { get; set; }
        public string message { get; set; }
        public string game_version { get; set; }
        public string mirror_game_version { get; set; }
        public int package_count { get; set; }
        public string checked_at { get; set; }
    }

    /// <summary>云端节点（地址由 Nodes 在启动时解析，随云端 nodes.json 变化，无需重新打包）</summary>
    internal static class Cloud
    {
        private static readonly object _gate = new object();
        private static Task<string> _init;

        private static string _root = Nodes.DefaultRoot;
        private static string _base = Nodes.DefaultRoot + "dl/repomods/";
        private static string _nodeName = "内置默认";

        /// <summary>云端节点根地址（以 / 结尾）</summary>
        public static string Root { get { return _root; } }

        /// <summary>云端 repomods 目录（以 / 结尾）</summary>
        public static string Base { get { return _base; } }

        /// <summary>当前选中的节点名（UI 展示用）</summary>
        public static string NodeName { get { return _nodeName; } }

        /// <summary>
        /// 解析并探测可用节点，只执行一次。调用后 Base / Root 即指向选中的节点。
        /// 节点清单来自云端 nodes.json，因此换服务器不需要重新打包。
        /// </summary>
        public static Task<string> InitAsync(Action<string> log = null)
        {
            lock (_gate)
            {
                if (_init == null) _init = InitCoreAsync(log);
                return _init;
            }
        }

        private sealed class NodeProbe
        {
            public CloudNode node;
            public bool ok;
        }

        private static async Task<string> InitCoreAsync(Action<string> log)
        {
            List<CloudNode> nodes;
            try
            {
                nodes = await Nodes.ResolveAsync(log, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log?.Invoke("节点清单解析失败：" + ex.Message);
                nodes = null;
            }

            if (nodes == null || nodes.Count == 0)
            {
                nodes = new List<CloudNode>
                {
                    new CloudNode { name = "内置默认", root = Nodes.DefaultRoot, priority = 1000 }
                };
            }

            // 并发探测，按 priority 取第一个可用的（总耗时 = 最慢的一个）
            NodeProbe[] probes = await Task.WhenAll(nodes.Select(async n => new NodeProbe
            {
                node = n,
                ok = await ProbeNodeAsync(n.Root).ConfigureAwait(false)
            })).ConfigureAwait(false);

            NodeProbe pick = probes.Where(p => p.ok).OrderBy(p => p.node.priority).FirstOrDefault();
            if (pick != null)
            {
                _root = pick.node.Root;
                _base = pick.node.Root + "dl/repomods/";
                _nodeName = pick.node.name;
                log?.Invoke("云端节点：已选用 " + _nodeName + "（" + _root + "）");
                return _nodeName;
            }

            foreach (NodeProbe p in probes)
                log?.Invoke("云端节点：不可达，跳过 " + p.node.name + "（" + p.node.Root + "）");

            _root = Nodes.DefaultRoot;
            _base = Nodes.DefaultRoot + "dl/repomods/";
            _nodeName = "内置默认";
            log?.Invoke("云端节点：全部不可达，回退内置地址 " + Nodes.DefaultRoot);
            return _nodeName;
        }

        private static async Task<bool> ProbeNodeAsync(string root)
        {
            try
            {
                using (var http = NewClient(6))
                {
                    string url = root + "dl/repomods/status.json?probe=" +
                                 DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string json = await http.GetStringAsync(url).ConfigureAwait(false);
                    var st = JsonSerializer.Deserialize<CloudStatus>(json, Opts);
                    return st != null && !string.IsNullOrEmpty(st.state);
                }
            }
            catch { return false; }
        }

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static HttpClient NewClient(int timeoutSec = 15)
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.Timeout = TimeSpan.FromSeconds(timeoutSec);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoModHelper/2.0 (+github.com/shibry88-netizen/RepoModHelper)");
            return http;
        }

        public static async Task<CloudStatus> StatusAsync(CancellationToken ct)
        {
            string url = Base + "status.json?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var http = NewClient(12))
            {
                string json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
                var st = JsonSerializer.Deserialize<CloudStatus>(json, Opts);
                if (st == null || string.IsNullOrEmpty(st.state))
                    throw new InvalidOperationException("status.json 内容不合法");
                return st;
            }
        }

        public static async Task<CloudManifest> ManifestAsync(CancellationToken ct)
        {
            string url = Base + "manifest.json?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var http = NewClient(20))
            {
                string json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
                var m = JsonSerializer.Deserialize<CloudManifest>(json, Opts);
                if (m == null || m.packages == null || m.packages.Count == 0)
                    throw new InvalidOperationException("manifest.json 内容不合法");
                return m;
            }
        }

        /// <summary>把 Thunderstore 直链改写成“经二号中转”的地址；非 Thunderstore 返回 null</summary>
        public static string RelayUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            foreach (var host in new[] { "https://thunderstore.io/", "http://thunderstore.io/" })
                if (url.StartsWith(host, StringComparison.OrdinalIgnoreCase))
                    return Root + "relay/ts/" + url.Substring(host.Length);
            foreach (var host in new[] { "https://ccdn.thunderstore.io/", "http://ccdn.thunderstore.io/" })
                if (url.StartsWith(host, StringComparison.OrdinalIgnoreCase))
                    return Root + "relay/cdn/" + url.Substring(host.Length);
            return null;
        }

        /// <summary>中转站健康检查（失败不影响主流程）</summary>
        public static async Task<bool> RelayAliveAsync(CancellationToken ct)
        {
            try
            {
                using (var http = NewClient(6))
                {
                    string body = await http.GetStringAsync(Root + "relay/healthz", ct).ConfigureAwait(false);
                    return body != null && body.Contains("ok");
                }
            }
            catch { return false; }
        }

    }
}
