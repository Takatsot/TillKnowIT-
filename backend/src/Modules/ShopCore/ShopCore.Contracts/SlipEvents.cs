using TillKnowIT.SharedKernel;

namespace ShopCore.Contracts;

/// <summary>
/// A customer asked for a slip by WhatsApp, SMS or email. Values hold the words for the message template:
/// shop, lines, total, date, url. Messaging turns this into a queued message; ShopCore never talks to a provider.
/// </summary>
public sealed record SlipRequested(
    Guid SlipRequestId, Guid ShopId, Guid SaleId, string Channel, string Destination, string Language, Dictionary<string, string> Values)
    : DomainEvent;
