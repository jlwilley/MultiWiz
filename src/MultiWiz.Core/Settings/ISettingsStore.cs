namespace MultiWiz.Core.Settings;

/// <summary>Holds the current <see cref="AppSettings"/> and persists changes. Thread-safe.</summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

    /// <summary>Atomically replaces the settings with <c>mutate(Current)</c> and saves.</summary>
    void Update(Func<AppSettings, AppSettings> mutate);

    /// <summary>Raised after a change with the new settings, on the thread that made it.</summary>
    event EventHandler<AppSettings>? Changed;
}
