using Microsoft.Extensions.Logging;
using MultiWiz.App.ViewModels;
using MultiWiz.Core.Games;
using MultiWiz.Core.Legacy;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;

namespace MultiWiz.App.Services;

/// <summary>Offers to import MultiWiz 3 accounts and settings (on first run, or from the empty account list). UI thread.</summary>
public sealed class LegacyImportService
{
    private readonly ILegacyImporter _importer;
    private readonly ISettingsStore _settings;
    private readonly IRealmCatalog _realms;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly StatusService _status;
    private readonly ILogger<LegacyImportService> _logger;

    public LegacyImportService(
        ILegacyImporter importer,
        ISettingsStore settings,
        IRealmCatalog realms,
        AppPaths paths,
        IDialogService dialogs,
        StatusService status,
        ILogger<LegacyImportService> logger)
    {
        _importer = importer;
        _settings = settings;
        _realms = realms;
        _paths = paths;
        _dialogs = dialogs;
        _status = status;
        _logger = logger;
    }

    /// <summary>True when a MultiWiz 3 account file exists on this PC.</summary>
    public bool HasLegacyData => File.Exists(_paths.LegacyConfigFile);

    /// <summary>The first-run prompt: only when v3 data exists and the user has not answered before.</summary>
    public Task OfferOnStartupAsync() =>
        _settings.Current.LegacyImportHandled || !HasLegacyData ? Task.CompletedTask : OfferAsync(userRequested: false);

    public async Task OfferAsync(bool userRequested)
    {
        LegacyImportPreview? preview;
        try
        {
            preview = _importer.ReadPreview();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the MultiWiz 3 configuration");
            if (userRequested)
            {
                await _dialogs.ShowErrorAsync(
                    "Import failed",
                    "MultiWiz 3's account file could not be read.",
                    ex.Message);
            }

            return;
        }

        if (preview is null || preview.Accounts.Count == 0)
        {
            if (userRequested)
            {
                await _dialogs.ShowMessageAsync("Nothing to import", "No MultiWiz 3 accounts were found on this PC.");
            }

            return;
        }

        var choice = await _dialogs.ShowLegacyImportAsync(new LegacyImportViewModel(preview, _realms));
        switch (choice)
        {
            case LegacyImportChoice.Import:
                Import(preview);
                break;
            case LegacyImportChoice.Decline:
                _importer.Decline();
                _logger.LogInformation("MultiWiz 3 import declined");
                break;
            default:
                _logger.LogInformation("MultiWiz 3 import postponed");
                break;
        }
    }

    private void Import(LegacyImportPreview preview)
    {
        try
        {
            var added = _importer.Apply(preview);
            _logger.LogInformation("Imported {Count} MultiWiz 3 accounts", added);
            _status.Show(added switch
            {
                0 => "Your MultiWiz 3 accounts were already here, so nothing new was added.",
                1 => "Imported 1 account from MultiWiz 3.",
                _ => $"Imported {added} accounts from MultiWiz 3.",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MultiWiz 3 import failed");
            _status.Show("The MultiWiz 3 import failed. See the log for details.", isError: true);
        }
    }
}
