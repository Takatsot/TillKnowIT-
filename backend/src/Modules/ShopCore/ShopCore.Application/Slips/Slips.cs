using Identity.Contracts;
using Messaging.Contracts;
using Billing.Contracts;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Slips;

/// <summary>Where slip links point and the secret that signs them. Set by the host from configuration.</summary>
public sealed record SlipOptions(byte[] Key, string BaseUrl);

public sealed record SlipSale(Guid SaleId, Guid ShopId, DateTimeOffset SoldAt, long TotalCents, long VatCents, string PaymentMethod, bool Cancelled, List<SlipLine> Lines);

public interface ISlipStore
{
    Task<SlipSale?> GetSale(Guid saleId, CancellationToken ct);
    Task<int> CountRequests(Guid saleId, CancellationToken ct);
    Task Add(SlipRequest slip, CancellationToken ct);
    Task Save(CancellationToken ct);
}

/// <summary>E3-6: "Slip?" at the till. QR is free and shows a link; WhatsApp, SMS and email queue a message.</summary>
public sealed record RequestSlip(Guid SaleId, RequestSlipRequest Request) : ICommand<SlipResponse>;

internal sealed class RequestSlipHandler(
    ISlipStore store, ITillAccessChecker access, IShopDirectory shops, IDestinationChecker destinations, SlipOptions options, TimeProvider clock,
    IMessageAllowance? allowance = null, IUsageMeter? meter = null, IBranchDirectory? branches = null)
    : ICommandHandler<RequestSlip, SlipResponse>
{
    public async Task<Result<SlipResponse>> Handle(RequestSlip command, CancellationToken ct)
    {
        var r = command.Request;
        if (!SlipChannels.TryParse(r.Channel, out var channel))
            return Result<SlipResponse>.Fail("slip.channel_invalid", "Channel must be qr, whatsapp, sms or email.");

        var allowed = await access.Check(r.ShopId, r.DeviceId, r.StaffId, "slip.send", ct);
        if (!allowed.IsSuccess) return Result<SlipResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        // The sale must belong to this shop, so one shop cannot send slips for another's sales.
        var sale = await store.GetSale(command.SaleId, ct);
        if (sale is null || sale.ShopId != r.ShopId) return Result<SlipResponse>.Fail("sale.not_found", "Sale not found.");

        // B8-3: a free plan has a monthly bundle of messages. When it is used up the slip is shown as a QR code instead, so the till is never stuck and the customer still gets a slip.
        string? note = null;
        if (channel != SlipChannel.Qr && allowance is not null && (await allowance.Check(r.ShopId, ct)).Exhausted)
        {
            channel = SlipChannel.Qr;
            note = "Your message bundle for this month is used up, so this slip is shown as a QR code.";
        }

        string? destination = null;
        if (channel != SlipChannel.Qr)
        {
            var checkedDestination = destinations.Check(r.Channel, r.Destination);
            if (!checkedDestination.IsSuccess) return Result<SlipResponse>.Fail(checkedDestination.Error!.Code, checkedDestination.Error.Message);
            destination = checkedDestination.Value;
        }

        if (await store.CountRequests(sale.SaleId, ct) >= SlipRequest.MaxPerSale)
            return Result<SlipResponse>.Fail("slip.limit_reached", $"A sale can have at most {SlipRequest.MaxPerSale} slips.");

        var shop = await shops.Get(r.ShopId, ct);
        var url = $"{options.BaseUrl.TrimEnd('/')}/{SlipToken.Create(sale.SaleId, options.Key)}";
        var values = SlipFormatter.Values(shop?.Name ?? "the shop", sale.Lines, sale.TotalCents, sale.SoldAt, url);

        var slip = SlipRequest.Request(r.ShopId, sale.SaleId, r.StaffId, r.DeviceId, channel, destination, string.IsNullOrWhiteSpace(r.Language) ? "en" : r.Language!, values, clock.GetUtcNow());
        await store.Add(slip, ct);
        await store.Save(ct);
        if (channel != SlipChannel.Qr && meter is not null && branches is not null)
            await meter.Record(await branches.HeadOf(r.ShopId, ct), PlanFeatures.Messages, 1, $"slip:{slip.Id:N}", clock.GetUtcNow(), ct);
        return Result<SlipResponse>.Ok(new SlipResponse(channel == SlipChannel.Qr ? "shown" : "queued", url, note));
    }
}

/// <summary>The page behind the QR code. The token is the only credential, and it shows only the slip.</summary>
public sealed record GetSlip(string Token) : IQuery<SlipView>;

internal sealed class GetSlipHandler(ISlipStore store, IShopDirectory shops, SlipOptions options) : IQueryHandler<GetSlip, SlipView>
{
    public async Task<Result<SlipView>> Handle(GetSlip query, CancellationToken ct)
    {
        // Every failure looks the same, so a probe learns nothing about which sales exist.
        const string notFound = "slip.not_found";
        if (!SlipToken.TryRead(query.Token, options.Key, out var saleId)) return Result<SlipView>.Fail(notFound, "Slip not found.");
        var sale = await store.GetSale(saleId, ct);
        if (sale is null) return Result<SlipView>.Fail(notFound, "Slip not found.");

        var shop = await shops.Get(sale.ShopId, ct);
        return Result<SlipView>.Ok(new SlipView(shop?.Name ?? "", SlipFormatter.Date(sale.SoldAt),
            sale.Lines.Select(l => new SlipLineView(l.Name, l.Quantity, l.PriceCents, l.UnitDivisor, l.DiscountCents)).ToList(), sale.TotalCents, sale.VatCents, sale.PaymentMethod, sale.Cancelled));
    }
}
