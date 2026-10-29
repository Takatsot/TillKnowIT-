namespace Identity.Contracts;

public sealed record StaffListItem(Guid StaffId, string Name, string Phone, string Role, bool Disabled);

/// <summary>Status is pending, approved or disabled.</summary>
public sealed record DeviceListItem(Guid DeviceId, string Name, string Status, DateTimeOffset? LastSeenAt, bool WipeRequested);
