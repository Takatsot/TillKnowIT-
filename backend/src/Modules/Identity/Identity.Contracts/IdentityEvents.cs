using TillKnowIT.SharedKernel;

namespace Identity.Contracts;

public sealed record ShopCreated(Guid ShopId, Guid OwnerPersonId, string Name) : DomainEvent;
