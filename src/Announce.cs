using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RepoInstaller
{
    internal sealed class AnnouncementItem
    {
        public string key { get; set; }
        public string version { get; set; }
    }

    internal sealed class AnnouncementUpdate
    {
        public string key { get; set; }
        public string from { get; set; }
        public string to { get; set; }
    }

    /// <summary>云端公告：本次名单相对上一次的变化</summary>
    internal sealed class Announcement
    {
        public string id { get; set; }
        public string created_at { get; set; }
        public string title { get; set; }
        public List<AnnouncementItem> added { get; set; } = new List<AnnouncementItem>();
        public List<AnnouncementItem> removed { get; set; } = new List<AnnouncementItem>();
        public List<AnnouncementUpdate> updated { get; set; } = new List<AnnouncementUpdate>();

        public int Total => (added?.Count ?? 0) + (removed?.Count ?? 0) + (updated?.Count ?? 0);
    }

    /// <summary>客户端本地状态：记住看过哪条公告</summary>
    internal sealed class ClientState
    {
        public string seenAnnouncement { get; set; }

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string FilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoModHelper");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "state.json");
        }

        public static ClientState Load()
        {
            try
            {
                string f = FilePath();
                if (File.Exists(f))
                {
                    var s = JsonSerializer.Deserialize<ClientState>(File.ReadAllText(f), Opts);
                    if (s != null) return s;
                }
            }
            catch { }
            return new ClientState();
        }

        public static void Save(ClientState s)
        {
            try { File.WriteAllText(FilePath(), JsonSerializer.Serialize(s, Opts)); } catch { }
        }
    }
}
