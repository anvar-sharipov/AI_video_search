namespace VMS.Frontend.WPF.Api;

public record LoginResponseDto(string Token, string Username, string Role, DateTimeOffset ExpiresAt, List<string> AdditionalPermissions);

public record CameraDto(Guid Id, string Code, string Name, string IpAddress, int OnvifPort, bool IsEnabled, bool Running);

public record CameraStatusDto(string Code, bool Running, DateTimeOffset OnboardedAt, string MainStreamUri, string SubStreamUri);

public record CreateCameraRequestDto(string Code, string Name, string IpAddress, int OnvifPort, string Username, string Password, Guid? RetentionPolicyId);

public record UpdateCameraRequestDto(string Name, string IpAddress, int OnvifPort, string Username, string Password, bool IsEnabled, Guid? RetentionPolicyId);

public record DiscoveredDeviceDto(string IpAddress, int OnvifPort, string? Model);

/// <summary>One recent "person" detection for the live known/unknown grid overlay — X/Y/Width/Height are fractional (0..1) against the source frame, PersonName null means unrecognized.</summary>
public record LiveDetectionDto(double X, double Y, double Width, double Height, string? PersonName);

public record PersonSightingReportRowDto(string CameraId, string CameraName, DateOnly Date, int Total, int Known, int Unknown);
public record PersonSightingDto(Guid Id, string CameraId, DateTimeOffset Timestamp, string? PersonName);

public record AddDiscoveredCameraRequestDto(string IpAddress, int OnvifPort, string Name, string Username, string Password, Guid? RetentionPolicyId);

public record BoundingBoxDto(double X, double Y, double Width, double Height);

public record SearchResultDto(
    Guid Id, string CameraId, DateTimeOffset Timestamp, string ObjectType,
    string? ColorAttribute, double Confidence, BoundingBoxDto BoundingBox, string VideoChunkLocation,
    string? PersonName = null, string? PlateNumber = null,
    // Elasticsearch's own kNN cosine-similarity score — only ever set for face/reverse-image
    // search results; null for plain attribute/text search, never faked for those.
    double? MatchScore = null);

public record KnownPersonDto(Guid Id, string Name, DateTimeOffset EnrolledAt);

public record ClipRequestDto(string CameraId, DateTimeOffset DetectionTime, int? PreRollSeconds, int? PostRollSeconds);

public record ClipResponseDto(string FileName);

public record ArchiveCoverageSegmentDto(DateTimeOffset Start, DateTimeOffset End, string FileName);

public record AuditLogEntryDto(
    Guid Id, DateTimeOffset Timestamp, Guid? UserId, string Username, string Action,
    string? TargetType, string? TargetId, string? Details, bool IsSuccess);

public record EMapDto(Guid Id, string Name, DateTimeOffset CreatedAt);
public record EMapPinDto(Guid Id, Guid EMapId, Guid CameraId, string CameraCode, string CameraName, double X, double Y);
public record CreateEMapPinRequestDto(Guid CameraId, double X, double Y);

public record CountingLinePointDto(double X, double Y);
public record CountingLineDto(string CameraCode, List<CountingLinePointDto> Points, bool LeftToRightIsIn);
public record SetCountingLineRequestDto(List<CountingLinePointDto> Points, bool LeftToRightIsIn);
public record PeopleCountDto(string CameraId, DateTimeOffset From, DateTimeOffset To, int In, int Out);

public record AlarmRecordDto(SearchResultDto Detection, bool IsAcknowledged);

public record UserDto(Guid Id, string Username, string Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, List<string> AdditionalPermissions);
public record CreateUserRequestDto(string Username, string Password, string Role, List<string>? AdditionalPermissions = null);
public record UpdateUserRequestDto(string Role, bool IsActive, string? NewPassword, List<string>? AdditionalPermissions = null);

public record CameraGroupMemberDto(Guid CameraId, string Code, string Name);
public record CameraGroupDto(Guid Id, string Name, List<CameraGroupMemberDto> Members);
public record SaveCameraGroupRequestDto(string Name, List<Guid> CameraIds);

public record VideoWallCellDto(Guid Id, int Row, int Column, int RowSpan, int ColumnSpan, Guid? CameraId, string? CameraCode, string? CameraName);
public record VideoWallLayoutDto(Guid Id, string Name, int Rows, int Columns, List<VideoWallCellDto> Cells);
public record SaveVideoWallCellRequestDto(int Row, int Column, int RowSpan, int ColumnSpan, Guid? CameraId);
public record SaveVideoWallLayoutRequestDto(string Name, int Rows, int Columns, List<SaveVideoWallCellRequestDto> Cells);
