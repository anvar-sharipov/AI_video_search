namespace VMS.Frontend.WPF.Api;

public record LoginResponseDto(string Token, string Username, string Role, DateTimeOffset ExpiresAt);

public record CameraDto(Guid Id, string Code, string Name, string IpAddress, int OnvifPort, bool IsEnabled, bool Running);

public record CameraStatusDto(string Code, bool Running, DateTimeOffset OnboardedAt, string MainStreamUri, string SubStreamUri);

public record CreateCameraRequestDto(string Code, string Name, string IpAddress, int OnvifPort, string Username, string Password, Guid? RetentionPolicyId);

public record BoundingBoxDto(double X, double Y, double Width, double Height);

public record SearchResultDto(
    Guid Id, string CameraId, DateTimeOffset Timestamp, string ObjectType,
    string? ColorAttribute, double Confidence, BoundingBoxDto BoundingBox, string VideoChunkLocation);

public record ClipRequestDto(string CameraId, DateTimeOffset DetectionTime, int? PreRollSeconds, int? PostRollSeconds);

public record ClipResponseDto(string FileName);

public record ArchiveCoverageSegmentDto(DateTimeOffset Start, DateTimeOffset End, string FileName);

public record AuditLogEntryDto(
    Guid Id, DateTimeOffset Timestamp, Guid? UserId, string Username, string Action,
    string? TargetType, string? TargetId, string? Details, bool IsSuccess);
