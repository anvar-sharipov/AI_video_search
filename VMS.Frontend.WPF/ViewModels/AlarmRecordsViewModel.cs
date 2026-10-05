using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

/// <summary>Backs the Alarm Records screen — see AlarmEndpoints for why "every indexed detection is an alarm record" instead of a separate rule engine.</summary>
public partial class AlarmRecordsViewModel : ObservableObject
{
    private readonly ApiClient _api;

    public ObservableCollection<AlarmRecordViewModel> Records { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public AlarmRecordsViewModel(ApiClient api)
    {
        _api = api;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var alarms = await _api.GetAlarmsAsync(size: 200);
            Records.Clear();
            foreach (var a in alarms)
            {
                Records.Add(new AlarmRecordViewModel(a));
            }
            StatusMessage = string.Empty;
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
    private async Task AcknowledgeAsync(AlarmRecordViewModel? record)
    {
        if (record is null || record.IsAcknowledged)
        {
            return;
        }

        try
        {
            await _api.AcknowledgeAlarmAsync(record.Detection.Id);
            await RefreshAsync();
        }
        catch (ApiException ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
