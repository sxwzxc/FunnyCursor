namespace MouseBeautifier.Core
{
    public sealed class AppSettings
    {
        public bool EnableClickEffects { get; set; } = true;
        public string ClickPreset { get; set; } = "sparkle";
        public string ClickColor { get; set; } = "#FF4DA6FF";
        public int ClickParticleCount { get; set; } = 40;
        public double ClickSpeed { get; set; } = 600;
        public double ClickGravity { get; set; } = 900;

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
        public string RopeStyle { get; set; } = "neon";

        public bool EnableTrail { get; set; } = true;
        public string TrailColor { get; set; } = "#FF7CF2FF";
        public double TrailLength { get; set; } = 0.5;
        public double TrailWidth { get; set; } = 6;

        public bool EnableGlow { get; set; } = true;
        public string GlowColor { get; set; } = "#FF66CCFF";
        public double GlowSize { get; set; } = 64;
        public double GlowIntensity { get; set; } = 0.5;

        public bool EnableOrbit { get; set; }
        public int OrbitCount { get; set; } = 56;
        public double OrbitRadius { get; set; } = 88;
        public double OrbitSpeed { get; set; } = 26;
        public double OrbitSize { get; set; } = 2.8;
        public double OrbitStrokeWidth { get; set; } = 0.8;
        public string OrbitColor { get; set; } = "#FFA786FF";

        public bool StartWithWindows { get; set; }

        public void Reset()
        {
            AppSettings defaults = new();

            EnableClickEffects = defaults.EnableClickEffects;
            ClickPreset = defaults.ClickPreset;
            ClickColor = defaults.ClickColor;
            ClickParticleCount = defaults.ClickParticleCount;
            ClickSpeed = defaults.ClickSpeed;
            ClickGravity = defaults.ClickGravity;

            EnableRope = defaults.EnableRope;
            RopeLength = defaults.RopeLength;
            RopeSegments = defaults.RopeSegments;
            RopeGravity = defaults.RopeGravity;
            RopeDamping = defaults.RopeDamping;
            RopeStiffness = defaults.RopeStiffness;
            IconType = defaults.IconType;
            CustomIconPath = defaults.CustomIconPath;
            IconSize = defaults.IconSize;
            IconColor = defaults.IconColor;
            RopeColor = defaults.RopeColor;
            RopeWidth = defaults.RopeWidth;
            RopeStyle = defaults.RopeStyle;

            EnableTrail = defaults.EnableTrail;
            TrailColor = defaults.TrailColor;
            TrailLength = defaults.TrailLength;
            TrailWidth = defaults.TrailWidth;

            EnableGlow = defaults.EnableGlow;
            GlowColor = defaults.GlowColor;
            GlowSize = defaults.GlowSize;
            GlowIntensity = defaults.GlowIntensity;

            EnableOrbit = defaults.EnableOrbit;
            OrbitCount = defaults.OrbitCount;
            OrbitRadius = defaults.OrbitRadius;
            OrbitSpeed = defaults.OrbitSpeed;
            OrbitSize = defaults.OrbitSize;
            OrbitStrokeWidth = defaults.OrbitStrokeWidth;
            OrbitColor = defaults.OrbitColor;

            StartWithWindows = defaults.StartWithWindows;
        }
    }
}
