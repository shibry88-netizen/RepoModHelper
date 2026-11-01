using System;
using System.Collections.Generic;
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

    /// <summary>云端节点（二号机 dl.lucifer.yoga）</summary>
    internal static class Cloud
    {
        public const string Base = "https://dl.lucifer.yoga/dl/repomods/";

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
    }
}
