using Catalogue.Contracts;
using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Compliance;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;

namespace ShopCore.Application.Tests;

public class ComplianceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);   // 26 Oct in the shop
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly byte[] Pdf = [.. "%PDF-1.4"u8.ToArray(), .. new byte[50]];

    // ---- rules ----

    [Theory]
    [InlineData(60, ComplianceStatus.Valid)]
    [InlineData(31, ComplianceStatus.Valid)]
    [InlineData(30, ComplianceStatus.ExpiringSoon)]
    [InlineData(0, ComplianceStatus.ExpiringSoon)]
    [InlineData(-1, ComplianceStatus.Expired)]
    public void Status_follows_the_days_left(int daysAhead, ComplianceStatus expected) =>
        Assert.Equal(expected, ComplianceRules.StatusOf(Today.AddDays(daysAhead), Today));

    [Fact]
    public void A_document_with_no_expiry_is_always_valid_and_never_reminds()
    {
        Assert.Equal(ComplianceStatus.Valid, ComplianceRules.StatusOf(null, Today));
        Assert.Null(ComplianceRules.ReminderDue(null, Today));
        Assert.Null(ComplianceRules.DaysLeft(null, Today));
    }

    [Theory]
    [InlineData(45, null)]
    [InlineData(31, null)]
    [InlineData(30, 30)]
    [InlineData(15, 30)]
    [InlineData(8, 30)]
    [InlineData(7, 7)]
    [InlineData(1, 7)]
    [InlineData(0, 7)]
    [InlineData(-1, 0)]
    [InlineData(-90, 0)]
    public void The_reminder_due_is_the_most_urgent_one_crossed(int daysAhead, int? expected) =>
        Assert.Equal(expected, ComplianceRules.ReminderDue(Today.AddDays(daysAhead), Today));

    [Fact]
    public void Each_reminder_has_its_own_stable_key_so_it_is_sent_once()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ComplianceRules.DedupeKey(id, 30), ComplianceRules.DedupeKey(id, 30));
        Assert.NotEqual(ComplianceRules.DedupeKey(id, 30), ComplianceRules.DedupeKey(id, 7));
        Assert.NotEqual(ComplianceRules.DedupeKey(id, 30), ComplianceRules.DedupeKey(Guid.NewGuid(), 30));
    }

    // ---- fakes ----

    private sealed class FakeStore : IComplianceStore
    {
        public List<ComplianceDocument> Docs { get; } = [];
        public bool FailOnSave { get; set; }
        public Task Add(ComplianceDocument d, CancellationToken ct) { Docs.Add(d); return Task.CompletedTask; }
        public Task<List<ComplianceDocument>> List(Guid shopId, CancellationToken ct) => Task.FromResult(Docs.Where(d => d.ShopId == shopId).ToList());
        public Task<ComplianceDocument?> Get(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(Docs.FirstOrDefault(d => d.Id == id && d.ShopId == shopId));
        public void Remove(ComplianceDocument d) => Docs.Remove(d);
        public Task<List<ComplianceDocument>> ExpiringBy(DateOnly date, CancellationToken ct) => Task.FromResult(Docs.Where(d => d.ExpiresOn <= date).ToList());
        public Task Save(CancellationToken ct) => FailOnSave ? throw new InvalidOperationException("database down") : Task.CompletedTask;
    }

    private sealed class MemoryFiles : IFileStore
    {
        public Dictionary<string, StoredFile> Files { get; } = [];
        public Task<string> Save(string container, string fileName, string contentType, byte[] content, CancellationToken ct)
        {
            var url = $"tkfile://{container}/{Guid.NewGuid():N}";
            Files[url] = new StoredFile(content, contentType, fileName);
            return Task.FromResult(url);
        }
        public Task<StoredFile?> Open(string url, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(url));
        public Task Delete(string url, CancellationToken ct) { Files.Remove(url); return Task.CompletedTask; }
    }

    private sealed class FakeAccess : ITillAccessChecker
    {
        public Result Answer { get; set; } = Result.Ok();
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) => Task.FromResult(Answer);
    }

    private sealed class FakeLookups : ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) => Task.FromResult(type == "document_types" && code is "trading_licence" or "health_certificate");
    }

    private readonly FakeStore _store = new();
    private readonly MemoryFiles _files = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _shop = Guid.NewGuid();

    private UploadComplianceDocumentHandler Upload() => new(_store, _files, _access, new FakeLookups(), new FakeTimeProvider(Now));

    private UploadComplianceDocument Request(string type = "trading_licence", DateOnly? expiry = null, string name = "licence.pdf", string contentType = "application/pdf", byte[]? content = null) =>
        new(_shop, Guid.NewGuid(), Guid.NewGuid(), type, expiry ?? Today.AddDays(200), name, contentType, content ?? Pdf);

    // ---- upload ----

    [Fact]
    public async Task Uploading_stores_the_file_encrypted_and_the_details_for_reminders()
    {
        var result = await Upload().Handle(Request(expiry: Today.AddDays(90)), default);

        Assert.True(result.IsSuccess);
        var doc = Assert.Single(_store.Docs);
        Assert.Equal(result.Value, doc.Id);
        Assert.Equal(Today.AddDays(90), doc.ExpiresOn);
        Assert.Equal("licence.pdf", doc.OriginalName);
        Assert.StartsWith("tkfile://compliance/", doc.FileUrl);
        Assert.Single(_files.Files);
    }

    [Fact]
    public async Task Refuses_without_permission_with_a_bad_file_or_an_unknown_type_and_stores_nothing()
    {
        _access.Answer = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await Upload().Handle(Request(), default)).Error!.Code);
        _access.Answer = Result.Ok();

        Assert.Equal("file.content_mismatch", (await Upload().Handle(Request(content: "MZ-malware"u8.ToArray()), default)).Error!.Code);
        Assert.Equal("file.type_invalid", (await Upload().Handle(Request(contentType: "text/html", name: "a.html"), default)).Error!.Code);
        Assert.Equal("compliance.type_invalid", (await Upload().Handle(Request(type: "nonsense"), default)).Error!.Code);

        Assert.Empty(_store.Docs);
        Assert.Empty(_files.Files);
    }

    [Fact]
    public async Task An_expiry_date_years_in_the_past_is_refused_and_the_stored_file_is_removed()
    {
        var result = await Upload().Handle(Request(expiry: Today.AddYears(-11)), default);

        Assert.Equal("compliance.expiry_invalid", result.Error!.Code);
        Assert.Empty(_files.Files);
    }

    [Fact]
    public async Task If_saving_the_record_fails_the_file_is_not_left_behind()
    {
        _store.FailOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload().Handle(Request(), default));

        Assert.Empty(_files.Files);
    }

    // ---- list, open, delete ----

    [Fact]
    public async Task The_list_shows_status_and_days_left_soonest_expiry_first()
    {
        await Upload().Handle(Request(type: "health_certificate", expiry: Today.AddDays(20), name: "health.pdf"), default);
        await Upload().Handle(Request(type: "trading_licence", expiry: Today.AddDays(-5), name: "old.pdf"), default);
        await Upload().Handle(Request(type: "trading_licence", expiry: Today.AddDays(400), name: "new.pdf"), default);

        var list = (await new ListComplianceDocumentsHandler(_store, new FakeTimeProvider(Now)).Handle(new ListComplianceDocuments(_shop), default)).Value!;

        Assert.Equal(["old.pdf", "health.pdf", "new.pdf"], list.Select(d => d.FileName));
        Assert.Equal(["expired", "expiring_soon", "valid"], list.Select(d => d.Status));
        Assert.Equal([-5, 20, 400], list.Select(d => d.DaysLeft!.Value));
    }

    [Fact]
    public async Task A_shop_can_open_its_own_document_but_never_another_shops()
    {
        var id = (await Upload().Handle(Request(), default)).Value;
        var open = new GetComplianceFileHandler(_store, _files);

        var own = await open.Handle(new GetComplianceFile(_shop, id), default);
        var other = await open.Handle(new GetComplianceFile(Guid.NewGuid(), id), default);

        Assert.Equal(Pdf, own.Value!.Content);
        Assert.Equal("compliance.not_found", other.Error!.Code);
    }

    [Fact]
    public async Task If_the_stored_file_has_gone_the_owner_is_told_to_upload_it_again()
    {
        var id = (await Upload().Handle(Request(), default)).Value;
        _files.Files.Clear();

        var result = await new GetComplianceFileHandler(_store, _files).Handle(new GetComplianceFile(_shop, id), default);

        Assert.Equal("compliance.file_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task Deleting_needs_permission_and_removes_the_record_and_the_file()
    {
        var id = (await Upload().Handle(Request(), default)).Value;
        var delete = new DeleteComplianceDocumentHandler(_store, _files, _access);

        _access.Answer = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await delete.Handle(new DeleteComplianceDocument(_shop, Guid.NewGuid(), Guid.NewGuid(), id), default)).Error!.Code);
        Assert.Single(_store.Docs);

        _access.Answer = Result.Ok();
        Assert.True((await delete.Handle(new DeleteComplianceDocument(_shop, Guid.NewGuid(), Guid.NewGuid(), id), default)).IsSuccess);
        Assert.Empty(_store.Docs);
        Assert.Empty(_files.Files);
    }

    // ---- reminders for the worker ----

    [Fact]
    public async Task The_worker_gets_one_due_reminder_per_document_with_a_stable_key()
    {
        foreach (var days in new[] { 90, 30, 12, 7, 2, -3 })
            await Upload().Handle(Request(expiry: Today.AddDays(days), name: $"d{days}.pdf"), default);

        var due = (await new FindDueComplianceRemindersHandler(_store, new FakeTimeProvider(Now)).Handle(new FindDueComplianceReminders(), default)).Value!;

        Assert.Equal(5, due.Count);                                    // the 90-day one is not due yet
        Assert.Equal([30, 30, 7, 7, 0], due.OrderBy(d => d.ExpiresOn!.Value.DayNumber).Reverse().Select(d => d.Reminder));
        Assert.All(due, d => Assert.Equal(ComplianceRules.DedupeKey(d.DocumentId, d.Reminder), d.DedupeKey));
    }
}
