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

public sealed record SuggestionResponse(Guid Id, string Barcode, string Name, string? Size, string CategoryCode, DateTimeOffset SuggestedAt);

/// <summary>Name, size and category are optional corrections made while approving.</summary>
public sealed record ApproveSuggestionRequest(Guid AdminPersonId, string? Name, string? Size, string? CategoryCode);

public sealed record RejectSuggestionRequest(Guid AdminPersonId, string Reason);

/// <summary>The suggestion duplicates an existing approved product; shops that used it are moved to that product.</summary>
public sealed record MergeSuggestionRequest(Guid AdminPersonId, Guid IntoProductId);

/// <summary>A suggestion was merged into an approved product. Shops that used the suggestion point at the approved one instead.</summary>
public sealed record CatalogueProductMerged(Guid FromId, Guid IntoId) : TillKnowIT.SharedKernel.DomainEvent;

public sealed record LookupResponse(string Type, string Code, Dictionary<string, string> Names, bool IsActive, int SortOrder);

/// <summary>Names add or replace translations by language code. Leave a field out to leave it as it is. A new lookup needs English.</summary>
public sealed record SaveLookupRequest(Guid AdminPersonId, Dictionary<string, string>? Names, bool? IsActive, int? SortOrder);

public sealed record CatalogueMatch(Guid Id, string Name, string Barcode);

/// <summary>Finds approved shared-catalogue products by barcode, for modules that match their own product lists to it.</summary>
public interface ICatalogueDirectory
{
    Task<Dictionary<string, CatalogueMatch>> FindApproved(IEnumerable<string> barcodes, CancellationToken ct);
}
