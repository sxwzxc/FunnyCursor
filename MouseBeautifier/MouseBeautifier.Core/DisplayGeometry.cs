using System;
using System.Numerics;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// A rectangle in virtual-desktop physical pixels. Left and top may be
    /// negative when a monitor is positioned above or left of the primary.
    /// </summary>
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        public PixelRect(int left, int top, int width, int height)
        {
            if (width < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => checked(Left + Width);
        public int Bottom => checked(Top + Height);
        public bool IsEmpty => Width == 0 || Height == 0;

        public bool Equals(PixelRect other) =>
            Left == other.Left &&
            Top == other.Top &&
            Width == other.Width &&
            Height == other.Height;

        public override bool Equals(object? obj) =>
            obj is PixelRect other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Left, Top, Width, Height);

        public static bool operator ==(PixelRect left, PixelRect right) =>
            left.Equals(right);

        public static bool operator !=(PixelRect left, PixelRect right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Pure coordinate conversion used by the Win32 overlay. The simulation
    /// remains in physical virtual-screen pixels; each renderer projects that
    /// shared frame into its monitor-local Win2D DIP coordinate system.
    /// </summary>
    public static class DisplayGeometry
    {
        public const float DefaultDpi = 96;

        public static Vector2 ScreenPixelsToLocalDips(
            Vector2 screenPixels,
            PixelRect monitorBounds,
            float dpiX,
            float dpiY)
        {
            ValidateDpi(dpiX, nameof(dpiX));
            ValidateDpi(dpiY, nameof(dpiY));
            return new Vector2(
                (screenPixels.X - monitorBounds.Left) *
                    DefaultDpi / dpiX,
                (screenPixels.Y - monitorBounds.Top) *
                    DefaultDpi / dpiY);
        }

        public static Vector2 LocalDipsToScreenPixels(
            Vector2 localDips,
            PixelRect monitorBounds,
            float dpiX,
            float dpiY)
        {
            ValidateDpi(dpiX, nameof(dpiX));
            ValidateDpi(dpiY, nameof(dpiY));
            return new Vector2(
                monitorBounds.Left +
                    localDips.X * dpiX / DefaultDpi,
                monitorBounds.Top +
                    localDips.Y * dpiY / DefaultDpi);
        }

        public static Matrix3x2 CreateScreenPixelsToLocalDipsTransform(
            PixelRect monitorBounds,
            float dpiX,
            float dpiY)
        {
            ValidateDpi(dpiX, nameof(dpiX));
            ValidateDpi(dpiY, nameof(dpiY));
            return Matrix3x2.CreateTranslation(
                -monitorBounds.Left,
                -monitorBounds.Top) *
                Matrix3x2.CreateScale(
                    DefaultDpi / dpiX,
                    DefaultDpi / dpiY);
        }

        private static void ValidateDpi(float dpi, string parameterName)
        {
            if (!float.IsFinite(dpi) || dpi <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
