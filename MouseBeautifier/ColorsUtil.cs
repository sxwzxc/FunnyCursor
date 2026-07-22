using System.Globalization;
using Windows.UI;

namespace MouseBeautifier
{
    internal static class ColorsUtil
    {
        public static Color Parse(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return Color.FromArgb(255, 255, 255, 255);
            }

            string value = hex.Trim().TrimStart('#');
            if (value.Length == 6)
            {
                value = "FF" + value;
            }

            if (value.Length == 8 &&
                uint.TryParse(
                    value,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint argb))
            {
                return Color.FromArgb(
                    (byte)(argb >> 24),
                    (byte)(argb >> 16),
                    (byte)(argb >> 8),
                    (byte)argb);
            }

            return Color.FromArgb(255, 255, 255, 255);
        }
    }
}
