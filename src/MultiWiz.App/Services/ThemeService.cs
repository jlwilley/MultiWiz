using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using MultiWiz.Core.Settings;

namespace MultiWiz.App.Services;

/// <summary>Applies <see cref="GeneralSettings.Theme"/> to the application and follows later changes.</summary>
public sealed class ThemeService : IDisposable
{
    private readonly ISettingsStore _settings;
    private ThemePreference? _applied;
    private bool _started;

    public ThemeService(ISettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>Applies the current theme and starts listening for changes. UI thread.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Apply(_settings.Current.General.Theme);
        _settings.Changed += OnSettingsChanged;
    }

    public void Dispose()
    {
        if (_started)
        {
            _settings.Changed -= OnSettingsChanged;
            _started = false;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var theme = settings.General.Theme;
        Dispatcher.UIThread.Post(() => Apply(theme));
    }

    private void Apply(ThemePreference theme)
    {
        if (_applied == theme || Application.Current is not { } application)
        {
            return;
        }

        _applied = theme;
        application.RequestedThemeVariant = theme switch
        {
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }
}
