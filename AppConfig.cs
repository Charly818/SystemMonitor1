using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SystemMonitor
{
    public class AppConfig
    {
        public static AppConfig Cur = new AppConfig();
        static string FilePath { get { return Path.Combine(Log.Dir, "config.json"); } }
        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

        public bool SetupDone;

        // ---- скло
        public int GlassMode = -1;            // -1 авто, 0 суцільний, 1 Acrylic (запасний), 2 Windows 11 Acrylic
        public int CornerRadius = 22;
        public string TintColor = "#FFFFFF";
        public string SolidColor = "#14161C";
        public string TextColor = "#F2F2F2";
        public int TintStrength = 14;
        public int Highlight = 55;
        public int Border = 70;
        public bool TempTint = true;
        public bool AnimatedSheen = true;
        public int FontSize = 13;
        public int Padding = 14;
        public int GraphHeight = 40;

        // ---- фон (GIF / картинка)
        public string BackgroundPath = "";
        public int BackgroundOpacity = 35;
        public bool BackgroundOnPanel = true;
        public bool BackgroundOnPlayer = true;

        // ---- віджети та гаряча клавіша
        public bool ShowPanel = true;
        public bool ShowPlayer = false;
        public bool ShowRadar = false;
        public int HotkeyVk = 0x78;           // F9
        public int HotkeyMods = 0;
        public int RadarSize = 170;
        public int RadarSensitivity = 60;

        // ---- дані
        public string MouseName = "pulsar,hator";
        public string PingHost = "1.1.1.1";

        public Dictionary<string, bool> Rows = DefaultRows();
        public Dictionary<string, double[]> Positions = new Dictionary<string, double[]>();

        public static Dictionary<string, bool> DefaultRows()
        {
            Dictionary<string, bool> d = new Dictionary<string, bool>();
            d["cpu"] = true; d["gpu"] = true; d["vram"] = true; d["watt"] = false; d["fan"] = false;
            d["fps"] = true; d["low1"] = true; d["ft"] = true; d["load"] = true; d["ram"] = true;
            d["disk"] = false; d["ping"] = true; d["mouse"] = true; d["top"] = false; d["net"] = true;
            d["day"] = false; d["clock"] = false; d["game"] = false;
            return d;
        }

        public int EffectiveGlassMode()
        {
            if (GlassMode >= 0) return GlassMode;
            return Environment.OSVersion.Version.Build >= 22621 ? 2 : 1;
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    AppConfig c = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts);
                    if (c != null) Cur = c;
                }
            }
            catch (Exception e) { Log.Write("config: " + e.Message); }

            if (Cur.Rows == null) Cur.Rows = DefaultRows();
            foreach (KeyValuePair<string, bool> kv in DefaultRows())
                if (!Cur.Rows.ContainsKey(kv.Key)) Cur.Rows[kv.Key] = kv.Value;
            if (Cur.Positions == null) Cur.Positions = new Dictionary<string, double[]>();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
            }
            catch (Exception e) { Log.Write("config save: " + e.Message); }
        }

        public static readonly string[] PresetNames = { "Liquid Glass", "Темне скло", "Неон", "Сакура", "Ліс", "Суцільний" };

        public static void ApplyPreset(string name)
        {
            AppConfig c = Cur;
            c.GlassMode = -1;
            switch (name)
            {
                case "Liquid Glass":
                    c.TintColor = "#FFFFFF"; c.TintStrength = 14; c.Highlight = 65; c.Border = 75; c.CornerRadius = 24; c.TextColor = "#F2F2F2"; break;
                case "Темне скло":
                    c.TintColor = "#14161C"; c.TintStrength = 45; c.Highlight = 20; c.Border = 35; c.CornerRadius = 16; c.TextColor = "#F2F2F2"; break;
                case "Неон":
                    c.TintColor = "#0A1830"; c.TintStrength = 50; c.Highlight = 40; c.Border = 90; c.CornerRadius = 14; c.TextColor = "#9EF7FF"; break;
                case "Сакура":
                    c.TintColor = "#FFB6D0"; c.TintStrength = 28; c.Highlight = 70; c.Border = 80; c.CornerRadius = 28; c.TextColor = "#FFF4F8"; break;
                case "Ліс":
                    c.TintColor = "#16301F"; c.TintStrength = 42; c.Highlight = 30; c.Border = 45; c.CornerRadius = 20; c.TextColor = "#E6FFE9"; break;
                case "Суцільний":
                    c.GlassMode = 0; c.SolidColor = "#14161C"; c.CornerRadius = 10; c.Border = 20; c.TextColor = "#F2F2F2"; break;
            }
            c.Save();
        }
    }
}
