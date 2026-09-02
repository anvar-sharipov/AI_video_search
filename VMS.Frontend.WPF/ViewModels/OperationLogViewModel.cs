using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Read-only viewer over the append-only audit trail (IAuditLogger/AuditLog table) — no per-row actions, just a list.</summary>
public partial class OperationLogViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<AuditLogEntryDto> Entries { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            Entries.Clear();
            var entries = await api.GetAuditLogAsync();
            foreach (var e in entries)
            {
                Entries.Add(e);
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
}
