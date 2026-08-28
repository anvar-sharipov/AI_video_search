namespace VMS.Backend.Server.Api;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string Role, DateTimeOffset ExpiresAt);

public record CameraDto(Guid Id, string Code, string Name, string IpAddress, int OnvifPort, bool IsEnabled, bool Running);
public record CreateCameraRequest(string Code, string Name, string IpAddress, int OnvifPort, string Username, string Password, Guid? RetentionPolicyId);

public record ClipRequest(string CameraId, DateTimeOffset DetectionTime, int? PreRollSeconds, int? PostRollSeconds);
public record ClipResponse(string FileName);
