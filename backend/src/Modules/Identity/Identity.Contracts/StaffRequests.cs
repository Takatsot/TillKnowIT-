namespace Identity.Contracts;

public sealed record AddStaffRequest(string Name, string Phone, string RoleCode, string Pin);

public sealed record AddStaffResponse(Guid MembershipId);

public sealed record UpdateStaffRequest(string? Pin, bool? Disabled);

public sealed record VerifyPinRequest(string Pin);
