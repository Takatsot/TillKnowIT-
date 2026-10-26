namespace Identity.Contracts;

public sealed record CreateShopRequest(string Name, string Area, string Language);

public sealed record CreateShopResponse(Guid ShopId);
