namespace Identity.Contracts;

public sealed record RegisterDeviceRequest(string Name);

public sealed record RegisterDeviceResponse(Guid DeviceId);

/// <summary>Status is "pending", "approved" or "disabled".</summary>
public sealed record DeviceStatusResponse(Guid DeviceId, string Status, bool WipeRequested);
