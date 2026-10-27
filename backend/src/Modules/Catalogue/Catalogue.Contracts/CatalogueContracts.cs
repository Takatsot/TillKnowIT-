namespace Catalogue.Contracts;

/// <summary>Lets other modules check a lookup value (for example a category) without reading Catalogue's tables.</summary>
public interface ILookupChecker
{
    /// <summary>True when the lookup exists and is switched on.</summary>
    Task<bool> IsActive(string type, string code, CancellationToken ct);
}

public sealed record CatalogueProductResponse(Guid Id, string Barcode, string Name, string? Size, string CategoryCode, string? PhotoUrl);

public sealed record SuggestProductRequest(string Barcode, string Name, string? Size, string CategoryCode);

/// <summary>Status is "suggested", "approved" or "rejected". A barcode that is already known returns the existing entry.</summary>
public sealed record SuggestProductResponse(Guid Id, string Status, bool AlreadyKnown);
