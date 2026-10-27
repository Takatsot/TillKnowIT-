namespace Customers.Contracts;

/// <summary>Id is made on the till, so adding the same customer twice is one customer. Phone is matched, so an existing customer with that number is returned.</summary>
public sealed record CreateCustomerRequest(Guid DeviceId, Guid StaffId, Guid Id, string Name, string Phone, string? Email, bool MessageConsent, string? ConsentSource);

public sealed record UpdateCustomerRequest(Guid DeviceId, Guid StaffId, string Name, string Phone, string? Email, bool MessageConsent, string? ConsentSource);

public sealed record CustomerResponse(Guid Id, string Name, string Phone, string? Email, bool MessageConsent, DateTimeOffset? ConsentAt, int Points);

public sealed record MergeCustomersRequest(Guid CallerPersonId, Guid SourceCustomerId);

public sealed record PointsEntryResponse(string Type, int Points, string? Note, DateTimeOffset At, Guid? SaleId);

public sealed record PointsResponse(Guid CustomerId, int Points, long ValueCents, int MinRedeemPoints, List<PointsEntryResponse> Recent);

/// <summary>Reference makes redeeming safe to repeat: the same reference gives back the same redemption.</summary>
public sealed record RedeemPointsRequest(Guid DeviceId, Guid StaffId, int Points, string Reference);

/// <summary>ValueCents is what the points are worth. The till pays it as a "voucher" part of the sale with this RedemptionReference.</summary>
public sealed record RedeemPointsResponse(string RedemptionReference, int Points, long ValueCents, int BalanceAfter);

public sealed record AdjustPointsRequest(Guid DeviceId, Guid ApprovedBy, Guid StaffId, int Points, string Reason);

/// <summary>How other modules reach a customer without reading the customer tables: for a reminder, whether they agreed to be messaged and where.</summary>
public sealed record CustomerInfo(Guid Id, string Name, string Phone, string? Email, bool MessageConsent);

public interface ICustomerDirectory
{
    /// <summary>Follows a merge: asking for a customer that was merged into another gives the one that carries on.</summary>
    Task<CustomerInfo?> Get(Guid shopId, Guid customerId, CancellationToken ct);
}
