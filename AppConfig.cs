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
        public int ConfigVersion = 0;

        // ---- анонімна статистика (лише з дозволу користувача)
        public bool StatsAsked;
        public bool StatsEnabled;
        public string InstallId = "";

        // ---- оновлення
        public bool AutoUpdate = true;
        public string UpdateRepo = "";

        // ---- скло
        public int GlassMode = -1;            // -1 авто (Liquid Glass), 0 суцільний, 1 Acrylic (запасний), 2 Windows 11 Acrylic, 3 Liquid Glass
        public int CornerRadius = 22;
        public string TintColor = "#FFFFFF";
        public string SolidColor = "#14161C";
        public string TextColor = "#F2F2F2";
        public int TintStrength = 6;
        public int Highlight = 50;
        public int Border = 70;
        public bool TempTint = true;
        public bool AnimatedSheen = true;
        public int FontSize = 13;
        public int Padding = 14;
        public int GraphHeight = 40;

        // ---- Liquid Glass (власний рендер: розмиття + заломлення краю)
        public int GlassStrength = 100;   // 100 = повний ефект скла, менше = прозоріше
        public int Blur = 6;
        public int Refraction = 70;
        public int Dispersion = 22;
        public int Saturation = 125;
        public int Brightness = 100;
        public int GlassFps = 30;

        // ---- радар звуку
        public bool RadarHighPass = true;
        public bool RadarBlips = true;

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
            return GlassMode == 0 ? 0 : 3;   // 0 = суцільний, решта = Liquid Glass (старі режими Acrylic більше не використовуються)
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

            if (Cur.ConfigVersion < 3)
            {
                // нове, прозоріше скло замість старого молочного; старі режими Acrylic переходять на Liquid Glass
                Cur.Blur = 6; Cur.Refraction = 70; Cur.Dispersion = 22; Cur.Saturation = 125; Cur.Brightness = 100;
                Cur.TintStrength = 6; Cur.Highlight = 50; Cur.GlassStrength = 100;
                if (Cur.GlassMode != 0) Cur.GlassMode = -1;
                Cur.ConfigVersion = 3;
                Cur.Save();
            }
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

        public static readonly string[] PresetNames = { "Прозоре скло", "Liquid Glass", "Матове скло", "Темне скло", "Неон", "Сакура", "Ліс", "Суцільний" };

        public static void ApplyPreset(string name)
        {
            AppConfig c = Cur;
            c.GlassMode = -1;
            c.GlassStrength = 100;
            switch (name)
            {
                case "Прозоре скло":
                    c.TintColor = "#FFFFFF"; c.TintStrength = 5; c.Highlight = 50; c.Border = 70; c.CornerRadius = 24; c.TextColor = "#F2F2F2";
                    c.Blur = 5; c.Refraction = 70; c.Dispersion = 22; c.Saturation = 120; c.Brightness = 100; break;
                case "Liquid Glass":
                    c.TintColor = "#FFFFFF"; c.TintStrength = 8; c.Highlight = 65; c.Border = 75; c.CornerRadius = 24; c.TextColor = "#F2F2F2";
                    c.Blur = 9; c.Refraction = 65; c.Dispersion = 28; c.Saturation = 140; c.Brightness = 100; break;
                case "Матове скло":
                    c.TintColor = "#FFFFFF"; c.TintStrength = 18; c.Highlight = 55; c.Border = 60; c.CornerRadius = 22; c.TextColor = "#F2F2F2";
                    c.Blur = 18; c.Refraction = 30; c.Dispersion = 8; c.Saturation = 150; c.Brightness = 100; break;
                case "Темне скло":
                    c.TintColor = "#14161C"; c.TintStrength = 40; c.Highlight = 20; c.Border = 35; c.CornerRadius = 16; c.TextColor = "#F2F2F2";
                    c.Blur = 10; c.Refraction = 25; c.Dispersion = 5; c.Saturation = 120; c.Brightness = 100; break;
                case "Неон":
                    c.TintColor = "#0A1830"; c.TintStrength = 35; c.Highlight = 40; c.Border = 90; c.CornerRadius = 14; c.TextColor = "#9EF7FF";
                    c.Blur = 8; c.Refraction = 50; c.Dispersion = 40; c.Saturation = 200; c.Brightness = 100; break;
                case "Сакура":
                    c.TintColor = "#FFB6D0"; c.TintStrength = 20; c.Highlight = 70; c.Border = 80; c.CornerRadius = 28; c.TextColor = "#FFF4F8";
                    c.Blur = 10; c.Refraction = 55; c.Dispersion = 25; c.Saturation = 140; c.Brightness = 105; break;
                case "Ліс":
                    c.TintColor = "#16301F"; c.TintStrength = 32; c.Highlight = 30; c.Border = 45; c.CornerRadius = 20; c.TextColor = "#E6FFE9";
                    c.Blur = 10; c.Refraction = 40; c.Dispersion = 10; c.Saturation = 125; c.Brightness = 100; break;
                case "Суцільний":
                    c.GlassMode = 0; c.SolidColor = "#14161C"; c.CornerRadius = 10; c.Border = 20; c.TextColor = "#F2F2F2"; break;
            }
            c.Save();
        }
    }
}
