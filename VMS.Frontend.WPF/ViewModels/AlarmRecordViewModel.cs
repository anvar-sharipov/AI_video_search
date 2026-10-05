using VMS.Frontend.WPF.Api;

namespace VMS.Frontend.WPF.ViewModels;

public class AlarmRecordViewModel(AlarmRecordDto dto)
{
    public SearchResultDto Detection => dto.Detection;
    public bool IsAcknowledged => dto.IsAcknowledged;

    public string CameraId => Detection.CameraId;
    public string ObjectType => Detection.ObjectType;
    public DateTimeOffset Timestamp => Detection.Timestamp;

    public string Summary =>
        $"{Detection.CameraId} — {(Detection.ColorAttribute is null ? "" : Detection.ColorAttribute + " ")}{Detection.ObjectType}" +
        $"{(Detection.PersonName is null ? "" : $" \"{Detection.PersonName}\"")}{(Detection.PlateNumber is null ? "" : $" [{Detection.PlateNumber}]")}" +
        $" ({Detection.Confidence:P0}) @ {Detection.Timestamp:yyyy-MM-dd HH:mm:ss}";
}
