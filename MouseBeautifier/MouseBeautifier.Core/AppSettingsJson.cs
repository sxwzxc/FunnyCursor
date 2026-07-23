using System;
using System.Text.Json;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// Versioned JSON boundary for settings. Legacy flat orbit properties are
    /// migrated once into the structured nebula model.
    /// </summary>
    public static class AppSettingsJson
    {
        public const int CurrentSchemaVersion = 2;

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static AppSettings Deserialize(string json)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);

            using JsonDocument document = JsonDocument.Parse(json);
            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ??
                new AppSettings();

            if (!TryGetProperty(
                    document.RootElement,
                    nameof(AppSettings.Nebula),
                    out JsonElement nebulaElement) ||
                nebulaElement.ValueKind != JsonValueKind.Object)
            {
                settings.Nebula = MigrateLegacyNebula(document.RootElement);
            }

            settings.SchemaVersion = CurrentSchemaVersion;
            settings.Normalize();
            return settings;
        }

        public static string Serialize(
            AppSettings settings,
            bool writeIndented = true)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Normalize();
            return JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = writeIndented,
                });
        }

        private static NebulaSettings MigrateLegacyNebula(JsonElement root)
        {
            NebulaSettings migrated = new();
            migrated.Enabled = ReadBoolean(
                root,
                "EnableOrbit",
                migrated.Enabled);
            migrated.ParticleCount = ReadInt32(
                root,
                "OrbitCount",
                migrated.ParticleCount);
            migrated.Radius = ReadDouble(
                root,
                "OrbitRadius",
                migrated.Radius);
            migrated.AngularSpeed = ReadDouble(
                root,
                "OrbitSpeed",
                migrated.AngularSpeed);

            migrated.StarSize = ReadDouble(
                root,
                "OrbitSize",
                migrated.StarSize);

            string particleColor = ReadString(
                root,
                "OrbitColor",
                "#FFA786FF");
            double particleAlpha = HexColor.ReadAlpha(particleColor);
            migrated.ParticleColor = HexColor.NormalizeRgb(
                particleColor,
                migrated.ParticleColor);
            migrated.ParticleOpacity *= particleAlpha;
            migrated.CloudColor = HexColor.NormalizeRgb(
                particleColor,
                migrated.CloudColor);
            migrated.CloudOpacity *= particleAlpha;
            migrated.TrailOpacity *= particleAlpha;

            migrated.StrokeWidth = ReadDouble(
                root,
                "OrbitStrokeWidth",
                migrated.StrokeWidth);
            migrated.StrokeColor = HexColor.NormalizeRgb(
                particleColor,
                migrated.StrokeColor);
            migrated.StrokeOpacity = particleAlpha;

            string haloColor = ReadString(
                root,
                "OrbitHaloColor",
                "#FFA786FF");
            migrated.HaloColor = HexColor.NormalizeRgb(
                haloColor,
                migrated.HaloColor);
            migrated.HaloOpacity =
                ReadDouble(
                    root,
                    "OrbitHaloIntensity",
                    migrated.HaloOpacity) *
                HexColor.ReadAlpha(haloColor);
            migrated.HaloSize = ReadDouble(
                root,
                "OrbitHaloSize",
                migrated.HaloSize);

            migrated.Normalize();
            return migrated;
        }

        private static bool ReadBoolean(
            JsonElement root,
            string name,
            bool fallback)
        {
            return TryGetProperty(root, name, out JsonElement value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;
        }

        private static int ReadInt32(
            JsonElement root,
            string name,
            int fallback)
        {
            return TryGetProperty(root, name, out JsonElement value) &&
                value.TryGetInt32(out int result)
                ? result
                : fallback;
        }

        private static double ReadDouble(
            JsonElement root,
            string name,
            double fallback)
        {
            return TryGetProperty(root, name, out JsonElement value) &&
                value.TryGetDouble(out double result) &&
                double.IsFinite(result)
                ? result
                : fallback;
        }

        private static string ReadString(
            JsonElement root,
            string name,
            string fallback)
        {
            return TryGetProperty(root, name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? fallback
                : fallback;
        }

        private static bool TryGetProperty(
            JsonElement element,
            string name,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(
                            property.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }
    }
}
