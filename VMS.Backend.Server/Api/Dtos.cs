namespace VMS.Backend.Server.Api;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string Role, DateTimeOffset ExpiresAt, List<string> AdditionalPermissions);

public record CameraDto(Guid Id, string Code, string Name, string IpAddress, int OnvifPort, bool IsEnabled, bool Running);
public record CreateCameraRequest(string Code, string Name, string IpAddress, int OnvifPort, string Username, string Password, Guid? RetentionPolicyId);
public record UpdateCameraRequest(string Name, string IpAddress, int OnvifPort, string Username, string Password, bool IsEnabled, Guid? RetentionPolicyId);
public record DiscoveredDeviceDto(string IpAddress, int OnvifPort, string? Model);

/// <summary>One recent "person" detection for the live known/unknown grid overlay — X/Y/Width/Height are fractional (0..1) against the source frame, PersonName null means unrecognized.</summary>
public record LiveDetectionDto(double X, double Y, double Width, double Height, string? PersonName);
public record AddDiscoveredCameraRequest(string IpAddress, int OnvifPort, string Name, string Username, string Password, Guid? RetentionPolicyId);

public record ClipRequest(string CameraId, DateTimeOffset DetectionTime, int? PreRollSeconds, int? PostRollSeconds);
public record ClipResponse(string FileName);

public record KnownPersonDto(Guid Id, string Name, DateTimeOffset EnrolledAt);

public record UserDto(Guid Id, string Username, string Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, List<string> AdditionalPermissions);
public record CreateUserRequest(string Username, string Password, string Role, List<string>? AdditionalPermissions = null);
public record UpdateUserRequest(string Role, bool IsActive, string? NewPassword, List<string>? AdditionalPermissions = null);
