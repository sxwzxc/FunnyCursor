using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Windows.UI;

namespace MouseBeautifier
{
    public sealed class AppSettings
    {
        // ---- Click effects ----
        public bool EnableClickEffects { get; set; } = true;
        public string ClickPreset { get; set; } = "sparkle";
        public string ClickColor { get; set; } = "#FF4DA6FF";
        public int ClickParticleCount { get; set; } = 40;
        public double ClickSpeed { get; set; } = 600;
        public double ClickGravity { get; set; } = 900;

        // ---- Rope + hanging icon ----
        public bool EnableRope { get; set; } = true;
        public double RopeLength { get; set; } = 170;
        public int RopeSegments { get; set; } = 18;
        public double RopeGravity { get; set; } = 1500;
        public double RopeDamping { get; set; } = 0.9;
        public double RopeStiffness { get; set; } = 0.6;
        public string IconType { get; set; } = "star";
        public string CustomIconPath { get; set; } = "";
        public double IconSize { get; set; } = 38;
        public string IconColor { get; set; } = "#FFFFC83D";
        public string RopeColor { get; set; } = "#FF9BE7FF";
        public double RopeWidth { get; set; } = 3;

        // ---- Trail ----
        public bool EnableTrail { get; set; } = true;
        public string TrailColor { get; set; } = "#FF7CF2FF";
        public double TrailLength { get; set; } = 0.5;
        public double TrailWidth { get; set; } = 6;

        // ---- Glow ----
        public bool EnableGlow { get; set; } = true;
        public string GlowColor { get; set; } = "#FF66CCFF";
        public double GlowSize { get; set; } = 64;
        public double GlowIntensity { get; set; } = 0.5;

        // ---- General ----
        public bool StartWithWindows { get; set; } = false;

        public void Reset()
        {
            EnableClickEffects = true;
            ClickPreset = "sparkle";
            ClickColor = "#FF4DA6FF";
            ClickParticleCount = 40;
            ClickSpeed = 600;
            ClickGravity = 900;

            EnableRope = true;
            RopeLength = 170;
            RopeSegments = 18;
            RopeGravity = 1500;
            RopeDamping = 0.9;
            RopeStiffness = 0.6;
            IconType = "star";
            CustomIconPath = "";
            IconSize = 38;
            IconColor = "#FFFFC83D";
            RopeColor = "#FF9BE7FF";
            RopeWidth = 3;

            EnableTrail = true;
            TrailColor = "#FF7CF2FF";
            TrailLength = 0.5;
            TrailWidth = 6;

            EnableGlow = true;
            GlowColor = "#FF66CCFF";
            GlowSize = 64;
            GlowIntensity = 0.5;

            StartWithWindows = false;
        }
    }

    public static class SettingsManager
    {
        public static AppSettings Current { get; private set; } = new();

        public static event Action? Changed;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseBeautifier", "settings.json");

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "MouseBeautifier";

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) Current = loaded;
                }
            }
            catch
            {
                Current = new AppSettings();
            }
            ApplyStartup();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best effort */ }
            ApplyStartup();
            Changed?.Invoke();
        }

        private static void ApplyStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;
                if (Current.StartWithWindows)
                {
                    var exe = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(AppName, exe);
                }
                else if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { /* no admin / locked */ }
        }
    }

    public static class ColorsUtil
    {
        public static Color Parse(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.FromArgb(255, 255, 255, 255);
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            if (h.Length == 8 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint v))
            {
                return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            }
            return Color.FromArgb(255, 255, 255, 255);
        }
    }
}
