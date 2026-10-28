namespace ShopCore.Contracts;

/// <summary>ToProductId is only needed when the receiving branch sells the product under another barcode; otherwise it is matched by barcode.</summary>
public sealed record TransferLineRequest(Guid ProductId, int Quantity, Guid? ToProductId);

/// <summary>Id is made on the till, so sending the same transfer twice is one transfer.</summary>
public sealed record SendTransferRequest(Guid DeviceId, Guid StaffId, Guid Id, Guid ToShopId, List<TransferLineRequest> Lines, string? Note);

public sealed record ReceivedLine(Guid ProductId, int Quantity);

/// <summary>Lines is what actually arrived. Leave it out if everything came.</summary>
public sealed record ReceiveTransferRequest(Guid DeviceId, Guid StaffId, List<ReceivedLine>? Lines);

public sealed record CancelTransferRequest(Guid DeviceId, Guid StaffId);

public sealed record TransferLineResponse(Guid ProductId, string Name, int Quantity, int? ReceivedQuantity, int Shortfall);

public sealed record TransferResponse(Guid Id, Guid FromShopId, Guid ToShopId, string Status, DateTimeOffset SentAt, DateTimeOffset? ClosedAt, string? Note, List<TransferLineResponse> Lines);
