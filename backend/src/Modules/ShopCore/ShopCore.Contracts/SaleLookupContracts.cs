namespace ShopCore.Contracts;

/// <summary>Whether a sale has reached the server. Other modules use it before they undo a payment that was meant for a sale: once the sale is recorded, the payment is part of it.</summary>
public interface ISaleLookup
{
    Task<bool> Recorded(Guid saleId, CancellationToken ct);
}
