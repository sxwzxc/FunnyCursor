using System;

namespace MouseBeautifier.Core
{
    /// <summary>
    /// Stable configuration boundary shared by the WinUI shell and render loop.
    /// Implementations own persistence and platform-specific startup registration.
    /// </summary>
    public interface ISettingsService
    {
        AppSettings Current { get; }

        event EventHandler? Changed;

        void Load();

        void Save();

        void Reset();
    }
}
