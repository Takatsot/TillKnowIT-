using Identity.Contracts;
using Marketplace.Application.Orders;
using Marketplace.Domain;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.Reporting;

namespace Marketplace.Application.Tests;

public class StatementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _supplier = Guid.NewGuid();

    private PurchaseOrder Sent(OrderPaymentMethod pay, long price = 10000, int cases = 1)
    {
        var o = PurchaseOrder.Create(Guid.NewGuid(), _supplier, Guid.NewGuid(), [new(Guid.NewGuid(), "Coke", "6001234567890", 24, cases, price, true)], pay, 0, 0, Now).Value!;
        o.Send(OrderChannel.App, Now);
        return o;
    }

    private static PurchaseOrder Delivered(PurchaseOrder o)
    {
        o.Respond(o.Lines.Select(l => new LineDecision(l.Id, "confirm", null, null)).ToList(), new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 26), Now);
        o.MarkDelivered(new Dictionary<Guid, int>(), Now);
        return o;
    }

    [Fact]
    public void A_delivered_order_that_is_not_paid_is_owing_and_the_totals_add_up()
    {
        var paid = Delivered(Sent(OrderPaymentMethod.OnDelivery, 10000));
        paid.SetPaymentStatus(OrderPaymentStatus.Paid);
        var owing = Delivered(Sent(OrderPaymentMethod.OnDelivery, 25000));
        var held = Delivered(Sent(OrderPaymentMethod.InApp, 5000));
        held.SetPaymentStatus(OrderPaymentStatus.Held);
        var notDeliveredYet = Sent(OrderPaymentMethod.OnDelivery, 99900);

        var table = StatementBuilder.Build("Fresh Foods", [(paid, "A"), (owing, "B"), (held, "C"), (notDeliveredYet, "D")], "2026-10-01 to 2026-10-31");

        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(["Period: 2026-10-01 to 2026-10-31", "Delivered: R 400.00", "Paid: R 100.00", "Held, to be released: R 50.00", "Still owing: R 250.00"], table.Notes);
        var owingRow = table.Rows.Single(r => (string)r[2]! == "B");
        Assert.Equal((25000L, 25000L), ((long)owingRow[6]!, (long)owingRow[7]!));
        Assert.Equal(0L, (long)table.Rows.Single(r => (string)r[2]! == "D")[7]!);   // not delivered, so nothing is owed yet
    }

    [Fact]
    public void Drafts_are_left_out_and_a_cancelled_order_counts_for_nothing()
    {
        var draft = PurchaseOrder.Create(Guid.NewGuid(), _supplier, Guid.NewGuid(), [new(Guid.NewGuid(), "Coke", null, 24, 1, 10000, true)], OrderPaymentMethod.OnDelivery, 0, 0, Now).Value!;
        var cancelled = Sent(OrderPaymentMethod.OnDelivery);
        cancelled.Cancel();

        var table = StatementBuilder.Build("Fresh Foods", [(draft, "A"), (cancelled, "B")], "p");

        var row = Assert.Single(table.Rows);
        Assert.Equal(("cancelled", 0L, 0L), ((string)row[3]!, (long)row[6]!, (long)row[7]!));
    }

    [Fact]
    public void The_statement_downloads_in_every_format()
    {
        var table = StatementBuilder.Build("Fresh Foods", [(Delivered(Sent(OrderPaymentMethod.OnDelivery)), "A")], "p");

        foreach (var format in new[] { ReportFormat.Csv, ReportFormat.Xlsx, ReportFormat.Pdf })
            Assert.NotEmpty(ReportExporter.Export(table, format, "statement").Content);
    }
}
