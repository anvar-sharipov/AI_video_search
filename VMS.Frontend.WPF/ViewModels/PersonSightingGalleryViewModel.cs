using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>One saved screenshot in the drill-down gallery — time + known/unknown label.</summary>
public class PersonSightingThumbnailViewModel(Guid id, byte[]? bytes, string? personName, DateTimeOffset timestamp)
{
    public Guid Id { get; } = id;
    public byte[]? Bytes { get; } = bytes;
    public string? PersonName { get; } = personName;
    public DateTimeOffset Timestamp { get; } = timestamp;
    public string Label { get; } = $"{timestamp:HH:mm:ss} — {personName ?? LocalizationService.Get("Overlay_UnknownPerson")}";
}

/// <summary>Backs the screenshot gallery dialog opened from one person-sighting report row — the individual sightings (and their saved screenshots) behind one camera+day total. Also lets an operator delete a mistaken/unwanted sighting, or export everything currently shown to disk, split into Known/&lt;name&gt; and Unknown folders.</summary>
public partial class PersonSightingGalleryViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public string HeaderText { get; }
    public ObservableCollection<PersonSightingThumbnailViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isBusy = true;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public PersonSightingGalleryViewModel(ApiClient api, PersonSightingReportRowDto row)
    {
        _api = api;
        HeaderText = $"{row.CameraName} — {row.Date:yyyy-MM-dd}";
        _ = LoadAsync(row);
    }

    private async Task LoadAsync(PersonSightingReportRowDto row)
    {
        try
        {
            var dayStart = new DateTimeOffset(row.Date.ToDateTime(TimeOnly.MinValue));
            var dayEnd = dayStart.AddDays(1).AddTicks(-1);
            var sightings = await _api.GetPersonSightingsAsync(row.CameraId, dayStart, dayEnd);

            foreach (var s in sightings)
            {
                var bytes = await _api.GetPersonSightingThumbnailBytesAsync(s.Id);
                Items.Add(new PersonSightingThumbnailViewModel(s.Id, bytes, s.PersonName, s.Timestamp));
            }

            if (Items.Count == 0)
            {
                StatusMessage = LocalizationService.Get("PersonSightingReport_NoData");
            }
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(PersonSightingThumbnailViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            LocalizationService.Get("PersonSightingGallery_ConfirmDeleteText"),
            LocalizationService.Get("PersonSightingGallery_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _api.DeletePersonSightingAsync(item.Id);
            Items.Remove(item);
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>Called from code-behind after a folder picker chooses the destination — writes every currently-loaded screenshot as a .jpg under Known/&lt;name&gt;/ or Unknown/, so an operator can hand the files off outside the app.</summary>
    public async Task ExportAsync(string folderPath)
    {
        var saved = 0;
        try
        {
            foreach (var item in Items)
            {
                if (item.Bytes is null)
                {
                    continue;
                }

                var subFolder = item.PersonName is null
                    ? Path.Combine(folderPath, "Unknown")
                    : Path.Combine(folderPath, "Known", SanitizeForPath(item.PersonName));
                Directory.CreateDirectory(subFolder);

                var fileName = $"{item.Timestamp:yyyy-MM-dd_HH-mm-ss}_{item.Id:N}.jpg";
                await File.WriteAllBytesAsync(Path.Combine(subFolder, fileName), item.Bytes);
                saved++;
            }

            StatusMessage = LocalizationService.Get("PersonSightingGallery_ExportSuccess", saved, folderPath);
        }
        catch (IOException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private static string SanitizeForPath(string name)
    {
        var sanitized = name;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }
        return sanitized;
    }
}
