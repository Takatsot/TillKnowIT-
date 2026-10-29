namespace Identity.Contracts;

public sealed record RegisterDeviceRequest(string Name);

/// <summary>EnrolmentCode is shown once. The owner types it into the till the first time it is set up.</summary>
public sealed record RegisterDeviceResponse(Guid DeviceId, string EnrolmentCode);

public sealed record CheckInRequest(string? EnrolmentCode);

/// <summary>Status is "pending", "approved" or "disabled".</summary>
/// <summary>Token proves this till on later calls (send it as X-Device-Token). It is refreshed on every check-in.</summary>
public sealed record DeviceStatusResponse(Guid DeviceId, string Status, bool WipeRequested, string? Token = null);
