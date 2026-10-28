namespace Finance.Contracts;

public sealed record GrantConsentRequest(Guid PersonId, string PartnerCode, int Months);

public sealed record ConsentResponse(Guid Id, string PartnerCode, int Months, DateTimeOffset GrantedAt, DateTimeOffset? WithdrawnAt, bool Active);

public sealed record ApplyRequest(Guid PersonId, Guid Id, Guid ConsentId, long RequestedCents);

public sealed record ApplicationResponse(Guid Id, string PartnerCode, string Status, long RequestedCents, long ApprovedCents, long AvailableCents, int TermDays, string? Reason, DateTimeOffset? DecidedAt);

public sealed record DrawRequest(Guid PersonId, Guid ApplicationId, Guid OrderId);

public sealed record DrawResponse(Guid Id, Guid OrderId, string SupplierName, long PrincipalCents, long RepayCents, long RepaidCents, DateOnly DueOn, string Status);

public sealed record DataShareResponse(DateTimeOffset At, string PartnerCode, int Months, long TotalSalesCents);

public sealed record CreditSummaryResponse(List<ConsentResponse> Consents, List<ApplicationResponse> Applications, List<DrawResponse> Draws, List<DataShareResponse> Shared);
