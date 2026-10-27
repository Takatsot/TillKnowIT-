using Messaging.Application.Sending;
using Messaging.Contracts;
using Messaging.Domain;
using Messaging.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Contracts;
using ShopCore.Domain;

namespace Messaging.Application.Tests;

public class MessagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    // ---- templates ----

    [Fact]
    public void Rendering_fills_every_placeholder_and_ignores_unused_values()
    {
        var result = TemplateRenderer.Render("{shop}: {total}", new Dictionary<string, string> { ["shop"] = "Mama's", ["total"] = "R61.00", ["spare"] = "x" });
        Assert.Equal("Mama's: R61.00", result.Value);
    }

    [Fact]
    public void A_missing_value_is_an_error_so_a_half_filled_message_never_goes_out()
    {
        var result = TemplateRenderer.Render("{shop} {total} {date} {total}", new Dictionary<string, string> { ["shop"] = "Mama's" });
        Assert.Equal("template.value_missing", result.Error!.Code);
        Assert.Contains("total, date", result.Error.Message);
    }

    // ---- destinations ----

    [Theory]
    [InlineData(MessageChannel.Whatsapp, "082 123 4567", "+27821234567")]
    [InlineData(MessageChannel.Sms, "+27 82 123 4567", "+27821234567")]
    [InlineData(MessageChannel.Sms, "0027821234567", "+27821234567")]
    [InlineData(MessageChannel.Email, " owner@shop.co.za ", "owner@shop.co.za")]
    public void Destinations_are_cleaned(MessageChannel channel, string input, string expected) =>
        Assert.Equal(expected, Destinations.Normalise(channel, input).Value);

    [Theory]
    [InlineData(MessageChannel.Whatsapp, "12345", "destination.phone_invalid")]
    [InlineData(MessageChannel.Sms, "", "destination.phone_invalid")]
    [InlineData(MessageChannel.Email, "not-an-email", "destination.email_invalid")]
    [InlineData(MessageChannel.Email, "a@b", "destination.email_invalid")]
    [InlineData(MessageChannel.Push, "not-a-guid", "destination.push_invalid")]
    public void Bad_destinations_are_refused(MessageChannel channel, string input, string code) =>
        Assert.Equal(code, Destinations.Normalise(channel, input).Error!.Code);

    // ---- sms cost ----

    [Theory]
    [InlineData(0, 1)]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Plain_sms_is_160_characters_then_153_per_part(int length, int parts) =>
        Assert.Equal(parts, MessageCosts.SmsSegments(new string('a', length)));

    [Fact]
    public void An_emoji_or_foreign_letter_switches_to_the_shorter_unicode_limits_and_some_characters_count_twice()
    {
        Assert.Equal(1, MessageCosts.SmsSegments(new string('a', 69) + "😀"[..1]));
        Assert.Equal(2, MessageCosts.SmsSegments(new string('a', 70) + "Ж"));
        Assert.Equal(1, MessageCosts.SmsSegments(new string('a', 158) + "€"));   // € counts as 2, so 160
        Assert.Equal(2, MessageCosts.SmsSegments(new string('a', 159) + "€"));   // 161
    }

    // ---- delivery state machine ----

    private static OutboundMessage Queued() => OutboundMessage.Queue(MessageChannel.Sms, "0821234567", null, "Hello", "t", null, null, Now).Value!;

    [Fact]
    public void A_queued_message_is_due_immediately_with_a_clean_destination()
    {
        var m = Queued();
        Assert.Equal("+27821234567", m.Destination);
        Assert.Equal(MessageStatus.Queued, m.Status);
        Assert.Equal(Now, m.NextAttemptAt);
    }

    [Fact]
    public void Failures_retry_after_1_5_15_and_60_minutes_then_give_up_on_the_fifth()
    {
        var m = Queued();
        var expected = new[] { 1, 5, 15, 60 };
        for (var i = 0; i < 4; i++)
        {
            m.RecordFailure(Now, "timeout", permanent: false);
            Assert.Equal(MessageStatus.Queued, m.Status);
            Assert.Equal(Now.AddMinutes(expected[i]), m.NextAttemptAt);
        }
        m.RecordFailure(Now, "timeout", permanent: false);
        Assert.Equal(MessageStatus.Failed, m.Status);
        Assert.Equal(5, m.Attempts);
    }

    [Fact]
    public void A_permanent_failure_stops_at_once_and_a_sent_message_forgets_its_error()
    {
        var gone = Queued();
        gone.RecordFailure(Now, "number does not exist", permanent: true);
        Assert.Equal(MessageStatus.Failed, gone.Status);

        var m = Queued();
        m.RecordFailure(Now, "timeout", permanent: false);
        m.MarkSent(Now, 30, "ref-1");
        Assert.Equal(MessageStatus.Sent, m.Status);
        Assert.Null(m.Error);
        Assert.Equal(30, m.CostCents);
    }

    [Fact]
    public void Bad_messages_are_not_queued()
    {
        Assert.Equal("destination.phone_invalid", OutboundMessage.Queue(MessageChannel.Sms, "123", null, "Hi", "t", null, null, Now).Error!.Code);
        Assert.Equal("message.empty", OutboundMessage.Queue(MessageChannel.Sms, "0821234567", null, " ", "t", null, null, Now).Error!.Code);
        Assert.Equal("message.too_long", OutboundMessage.Queue(MessageChannel.Sms, "0821234567", null, new string('a', 700), "t", null, null, Now).Error!.Code);
    }

    // ---- fakes ----

    private sealed class FakeStore : IMessagingStore
    {
        public List<MessageTemplate> Templates { get; } = [];
        public List<OutboundMessage> Messages { get; } = [];
        public Task<MessageTemplate?> FindTemplate(string code, string language, MessageChannel channel, CancellationToken ct)
        {
            var matches = Templates.Where(t => t.Code == code && t.Channel == channel && (t.Language == language || t.Language == "en")).ToList();
            return Task.FromResult(matches.FirstOrDefault(t => t.Language == language) ?? matches.FirstOrDefault());
        }
        public Task<OutboundMessage?> FindByDedupeKey(string key, CancellationToken ct) => Task.FromResult(Messages.FirstOrDefault(m => m.DedupeKey == key));
        public Task Add(OutboundMessage m, CancellationToken ct) { Messages.Add(m); return Task.CompletedTask; }
        public Task<List<OutboundMessage>> Due(DateTimeOffset now, int take, CancellationToken ct) =>
            Task.FromResult(Messages.Where(m => m.Status == MessageStatus.Queued && m.NextAttemptAt <= now).Take(take).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeProvider(MessageChannel channel, Func<OutboundMessage, SendOutcome> reply) : IMessageChannelProvider
    {
        public MessageChannel Channel => channel;
        public Task<SendOutcome> Send(OutboundMessage m, CancellationToken ct) => Task.FromResult(reply(m));
    }

    private readonly FakeStore _store = new();

    private MessageQueue Queue() => new(_store, new FakeTimeProvider(Now));

    private static QueueMessageRequest Request(string channel = "whatsapp", string language = "en", Dictionary<string, string>? values = null, string? key = null) =>
        new("slip.sale", language, channel, "0821234567", values ?? new() { ["shop"] = "Mama's", ["total"] = "R61.00" }, null, key);

    // ---- queue ----

    [Fact]
    public async Task Queueing_renders_the_template_and_keeps_the_subject()
    {
        _store.Templates.Add(new MessageTemplate("slip.sale", "en", MessageChannel.Email, "Total {total}", "Slip from {shop}"));

        var result = await Queue().Handle(Request("email"), "owner@shop.co.za");

        Assert.True(result.IsSuccess);
        var m = Assert.Single(_store.Messages);
        Assert.Equal("Total R61.00", m.Body);
        Assert.Equal("Slip from Mama's", m.Subject);
    }

    [Fact]
    public async Task A_language_without_a_template_falls_back_to_english_but_a_translation_wins_when_it_exists()
    {
        _store.Templates.Add(new MessageTemplate("slip.sale", "en", MessageChannel.Whatsapp, "Thanks {shop}"));
        _store.Templates.Add(new MessageTemplate("slip.sale", "zu", MessageChannel.Whatsapp, "Ngiyabonga {shop}"));
        var queue = Queue();

        await queue.Queue(Request(language: "xh"), default);
        await queue.Queue(Request(language: "zu"), default);

        Assert.Equal(["Thanks Mama's", "Ngiyabonga Mama's"], _store.Messages.Select(m => m.Body));
    }

    [Fact]
    public async Task No_template_a_missing_value_a_bad_channel_or_a_bad_number_is_refused_and_nothing_is_queued()
    {
        var queue = Queue();
        Assert.Equal("template.not_found", (await queue.Queue(Request(), default)).Error!.Code);

        _store.Templates.Add(new MessageTemplate("slip.sale", "en", MessageChannel.Whatsapp, "Thanks {shop} {url}"));
        Assert.Equal("template.value_missing", (await queue.Queue(Request(), default)).Error!.Code);
        Assert.Equal("message.channel_invalid", (await queue.Queue(Request("carrier-pigeon"), default)).Error!.Code);
        Assert.Equal("destination.phone_invalid", (await queue.Queue(Request(values: new() { ["shop"] = "x", ["url"] = "y" }) with { Destination = "12" }, default)).Error!.Code);

        Assert.Empty(_store.Messages);
    }

    [Fact]
    public async Task The_same_dedupe_key_is_only_ever_queued_once()
    {
        _store.Templates.Add(new MessageTemplate("slip.sale", "en", MessageChannel.Whatsapp, "Thanks {shop}"));
        var queue = Queue();

        var first = await queue.Queue(Request(key: "daily:shop1:2026-10-26"), default);
        var second = await queue.Queue(Request(key: "daily:shop1:2026-10-26"), default);

        Assert.False(first.Value!.AlreadyQueued);
        Assert.True(second.Value!.AlreadyQueued);
        Assert.Equal(first.Value.MessageId, second.Value.MessageId);
        Assert.Single(_store.Messages);
    }

    // ---- sending ----

    private SendDueMessagesHandler Sender(params IMessageChannelProvider[] providers) =>
        new(_store, providers, new DefaultPricing(), new FakeTimeProvider(Now));

    private sealed class DefaultPricing : IMessagePricing
    {
        public long CostCents(MessageChannel channel, string body) => channel == MessageChannel.Sms ? 30L * MessageCosts.SmsSegments(body) : 45;
    }

    [Fact]
    public async Task Due_messages_are_sent_and_costed()
    {
        _store.Messages.Add(Queued());
        var result = await Sender(new FakeProvider(MessageChannel.Sms, _ => new SendOutcome(true, ProviderRef: "p1"))).Handle(new SendDueMessages(), default);

        Assert.Equal(new SendDueResult(1, 0, 0), result.Value);
        Assert.Equal(MessageStatus.Sent, _store.Messages[0].Status);
        Assert.Equal(30, _store.Messages[0].CostCents);
        Assert.Equal("p1", _store.Messages[0].ProviderRef);
    }

    [Fact]
    public async Task A_provider_that_fails_or_throws_is_retried_later_not_hammered_now()
    {
        _store.Messages.Add(Queued());
        _store.Messages.Add(Queued());
        var calls = 0;
        var provider = new FakeProvider(MessageChannel.Sms, _ => ++calls == 1 ? new SendOutcome(false, Error: "timeout") : throw new InvalidOperationException("boom"));
        var sender = Sender(provider);

        var first = await sender.Handle(new SendDueMessages(), default);
        var second = await sender.Handle(new SendDueMessages(), default);

        Assert.Equal(new SendDueResult(0, 2, 0), first.Value);
        Assert.Equal(new SendDueResult(0, 0, 0), second.Value);      // both are waiting out their backoff
        Assert.All(_store.Messages, m => Assert.Equal(1, m.Attempts));
    }

    [Fact]
    public async Task A_channel_with_no_provider_fails_visibly_instead_of_pretending_to_send()
    {
        _store.Messages.Add(Queued());

        var result = await Sender().Handle(new SendDueMessages(), default);

        Assert.Equal(new SendDueResult(0, 0, 1), result.Value);
        Assert.Equal(MessageStatus.Failed, _store.Messages[0].Status);
        Assert.Contains("No provider", _store.Messages[0].Error);
    }

    [Fact]
    public async Task A_permanent_provider_failure_is_not_retried()
    {
        _store.Messages.Add(Queued());
        var result = await Sender(new FakeProvider(MessageChannel.Sms, _ => new SendOutcome(false, Permanent: true, Error: "not a number"))).Handle(new SendDueMessages(), default);
        Assert.Equal(new SendDueResult(0, 0, 1), result.Value);
    }

    // ---- slips from ShopCore ----

    [Fact]
    public async Task A_slip_request_becomes_one_queued_message_even_if_the_event_arrives_twice()
    {
        _store.Templates.Add(new MessageTemplate("slip.sale", "en", MessageChannel.Whatsapp, "Thanks {shop}\n{lines}\n{total} {date} {url}"));
        var handler = new SlipRequestedHandler(Queue());
        var slip = new SlipRequested(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "+27821234567", "en",
            SlipFormatter.Values("Mama's", [new SlipLine("Bread", 2, 1800)], 3600, Now, "https://x/y"));

        await handler.Handle(slip, default);
        await handler.Handle(slip, default);

        var m = Assert.Single(_store.Messages);
        Assert.Contains("2 x Bread  R36.00", m.Body);
        Assert.Equal($"slip:{slip.SlipRequestId:N}", m.DedupeKey);
    }

    [Fact]
    public async Task A_slip_that_cannot_be_queued_leaves_its_reason_on_the_outbox()
    {
        _store.Templates.Add(new MessageTemplate("slip.sale", "en", MessageChannel.Whatsapp, "Thanks {shop}"));
        var slip = new SlipRequested(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "123", "en", new() { ["shop"] = "x" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new SlipRequestedHandler(Queue()).Handle(slip, default));

        Assert.Contains("destination.phone_invalid", ex.Message);
    }

    // ---- the seeded templates ----

    private static Dictionary<string, string> ValuesFor(string code) => code switch
    {
        "slip.sale" => SlipFormatter.Values("Mama's", [new SlipLine("Bread", 1, 1800)], 1800, Now, "https://x/y"),
        "daily.summary" => new() { ["shop"] = "s", ["sales"] = "1", ["profit"] = "1", ["customers"] = "1", ["low"] = "1", ["expiring"] = "1", ["cashup"] = "ok", ["cancellations"] = "0" },
        "alert.loss" => new() { ["shop"] = "s", ["message"] = "m" },
        "order.new" => new() { ["shop"] = "s", ["order"] = "AB12CD34", ["lines"] = "1 x Coke", ["payment"] = "payable on delivery" },
        "order.update" => new() { ["shop"] = "s", ["order"] = "AB12CD34", ["supplier"] = "Fresh Foods", ["summary"] = "ok", ["total"] = "R10.00" },
        "wallet.purchase" => new() { ["child"] = "Thandi", ["amount"] = "R10.00", ["balance"] = "R40.00" },
        "wallet.topup" => new() { ["child"] = "Thandi", ["amount"] = "R100.00", ["balance"] = "R140.00" },
        "wallet.low_balance" => new() { ["child"] = "Thandi", ["balance"] = "R5.00" },
        "wallet.overdrawn" => new() { ["child"] = "Thandi", ["amount"] = "R8.00" },
        "wallet.sale_rejected" => new() { ["amount"] = "R8.00", ["reason"] = "allowance.exceeded", ["child"] = "Thandi", ["shop"] = "s", ["sold"] = "2026-10-26 10:00" },
        "group.update" => new() { ["shop"] = "s", ["product"] = "Coke", ["supplier"] = "Fresh Foods", ["summary"] = "ok" },
        "device.offline" => new() { ["shop"] = "s", ["device"] = "d", ["hours"] = "48" },
        _ when code.StartsWith("compliance.reminder.", StringComparison.Ordinal) => new() { ["shop"] = "s", ["document"] = "d", ["date"] = "2026-11-01", ["days"] = "7" },
        _ => throw new InvalidOperationException($"No sample values for {code}; add them here and to the sender."),
    };

    [Fact]
    public void Every_seeded_template_can_be_rendered_with_the_values_its_sender_provides()
    {
        var rows = MessagingSeed.Rows();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var code = (string)row.GetType().GetProperty("Code")!.GetValue(row)!;
            var body = (string)row.GetType().GetProperty("Body")!.GetValue(row)!;
            var subject = (string?)row.GetType().GetProperty("Subject")!.GetValue(row);
            Assert.True(TemplateRenderer.Render(body, ValuesFor(code)).IsSuccess, $"{code} body");
            if (subject is not null) Assert.True(TemplateRenderer.Render(subject, ValuesFor(code)).IsSuccess, $"{code} subject");
        }
    }

    [Fact]
    public void The_sms_slip_fits_in_a_single_message_with_typical_values()
    {
        var sms = MessagingSeed.Rows().Select(r => (Code: (string)r.GetType().GetProperty("Code")!.GetValue(r)!, Channel: r.GetType().GetProperty("Channel")!.GetValue(r)!.ToString(), Body: (string)r.GetType().GetProperty("Body")!.GetValue(r)!))
            .Single(t => t.Code == "slip.sale" && t.Channel == "Sms");
        var text = TemplateRenderer.Render(sms.Body, SlipFormatter.Values("Mama's Spaza", [], 12345, Now, "https://slip.tillknowit.co.za/s/aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789abcdefg")).Value!;

        Assert.Equal(1, MessageCosts.SmsSegments(text));
    }
}

internal static class QueueExtensions
{
    /// <summary>Test shorthand: queue a request to an explicit destination.</summary>
    public static Task<TillKnowIT.SharedKernel.Result<QueueMessageResult>> Handle(this IMessageQueue queue, QueueMessageRequest request, string destination) =>
        queue.Queue(request with { Destination = destination }, default);
}
