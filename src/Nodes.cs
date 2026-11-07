using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RepoInstaller
{
    internal sealed class CloudNode
    {
        public string name { get; set; }
        public string root { get; set; }
        public int priority { get; set; }

        /// <summary>规范化后的根地址（保证以 / 结尾）</summary>
        public string Root
        {
            get
            {
                string r = (root ?? "").Trim();
                if (r.Length == 0) return "";
                return r.EndsWith("/", StringComparison.Ordinal) ? r : r + "/";
            }
        }
    }

    internal sealed class NodeList
    {
        public int schema { get; set; }
        public string updated { get; set; }
        /// <summary>true = 本地强制覆盖，忽略时效与远端</summary>
        public bool force { get; set; }
        public List<CloudNode> nodes { get; set; }
    }

    /// <summary>
    /// 云端节点解析。顺序：本地强制配置 → 本地缓存(24h) → 远端 nodes.json → 内置兜底。
    /// 换云端服务器 / 中转节点时，只需改仓库里的 nodes.json，客户端无需重新打包。
    /// </summary>
    internal static class Nodes
    {
        /// <summary>编译进 exe 的最终兜底地址；其余全部不可用时用它，行为与旧版完全一致。</summary>
        public const string DefaultRoot = "https://dl.lucifer.yoga/";

        /// <summary>引导地址：远端清单的两个来源（GitHub 直连 + jsDelivr 镜像）</summary>
        private static readonly string[] BootstrapUrls = new[]
        {
            "https://raw.githubusercontent.com/shibry88-netizen/RepoModHelper/main/nodes.json",
            "https://cdn.jsdelivr.net/gh/shibry88-netizen/RepoModHelper@main/nodes.json",
        };

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>本地配置文件；手动放一个 force=true 的即可强制指定节点（应急用）</summary>
        public static string CachePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RepoModHelper",
                    "nodes.json");
            }
        }

        private static HttpClient NewClient(int timeoutSec)
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.Timeout = TimeSpan.FromSeconds(timeoutSec);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoModHelper/2.5 (+github.com/shibry88-netizen/RepoModHelper)");
            return http;
        }

        private static List<CloudNode> Clean(NodeList list)
        {
            var result = new List<CloudNode>();
            if (list == null || list.nodes == null) return result;
            foreach (var n in list.nodes)
            {
                if (n == null) continue;
                string r = n.Root;
                if (r.Length == 0) continue;
                // 只接受 https，避免被降级到明文
                if (!r.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.priority <= 0) n.priority = 100;
                if (string.IsNullOrWhiteSpace(n.name)) n.name = r;
                result.Add(n);
            }
            return result.OrderBy(n => n.priority).ToList();
        }

        private static List<CloudNode> Builtin()
        {
            return new List<CloudNode>
            {
                new CloudNode { name = "内置默认", root = DefaultRoot, priority = 1000 }
            };
        }

        /// <summary>读本地文件；fresh=true 表示可以跳过远端拉取</summary>
        private static List<CloudNode> ReadLocal(out bool fresh)
        {
            fresh = false;
            try
            {
                string path = CachePath;
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<NodeList>(json, Opts);
                var nodes = Clean(list);
                if (nodes.Count == 0) return null;

                if (list != null && list.force)
                {
                    fresh = true;
                    return nodes;
                }

                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                if (age < TimeSpan.FromHours(24)) fresh = true;

                return nodes;   // 过期时仍返回，作为远端失败后的兜底
            }
            catch
            {
                return null;
            }
        }

        private static void SaveLocal(NodeList list, List<CloudNode> nodes)
        {
            try
            {
                string path = CachePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var payload = new NodeList
                {
                    schema = list != null && list.schema > 0 ? list.schema : 1,
                    updated = list != null ? list.updated : null,
                    force = false,
                    nodes = nodes
                };
                File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 缓存写不进去不影响主流程 */ }
        }

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        /// <summary>解析出候选节点列表（已按 priority 排序，至少有 1 个）</summary>
        public static async Task<List<CloudNode>> ResolveAsync(Action<string> log, CancellationToken ct)
        {
            var local = ReadLocal(out bool fresh);
            if (local != null && fresh)
            {
                log?.Invoke("节点清单：使用本地配置/缓存（" + local.Count + " 个）");
                return local;
            }

            foreach (var url in BootstrapUrls)
            {
                try
                {
                    using (var http = NewClient(8))
                    {
                        string json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
                        var list = JsonSerializer.Deserialize<NodeList>(json, Opts);
                        var nodes = Clean(list);
                        if (nodes.Count > 0)
                        {
                            SaveLocal(list, nodes);
                            log?.Invoke("节点清单：已从云端拉取（" + nodes.Count + " 个 · updated=" + (list.updated ?? "?") + "）");
                            return nodes;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke("节点清单：引导地址不可用（" + HostOf(url) + "）" + ex.Message);
                }
            }

            if (local != null)
            {
                log?.Invoke("节点清单：远端不可用，使用过期缓存（" + local.Count + " 个）");
                return local;
            }

            log?.Invoke("节点清单：全部不可用，回退内置地址 " + DefaultRoot);
            return Builtin();
        }
    }
}
