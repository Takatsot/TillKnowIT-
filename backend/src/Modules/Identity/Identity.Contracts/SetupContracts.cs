namespace Identity.Contracts;

public sealed record MembershipResponse(Guid MembershipId, Guid OrganisationId, string OrganisationName, string OrganisationType, string RoleCode);

public sealed record SetupStepResponse(string Item, bool Done, DateTimeOffset? CompletedAt, string? Note);

/// <summary>The field agent's checklist. Verified is true once every step is done and the agent has verified the shop.</summary>
public sealed record SetupChecklistResponse(List<SetupStepResponse> Steps, bool AllDone, string ShopStatus);

/// <summary>A shop waiting for a field agent: how far the checklist has got.</summary>
public sealed record PendingShopResponse(Guid ShopId, string Name, string? Area, int StepsDone, int StepsTotal);

public sealed record CompleteSetupStepRequest(Guid AgentPersonId, string? Note);

public sealed record VerifyShopRequest(Guid AgentPersonId);
