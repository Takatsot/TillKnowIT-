using Catalogue.Contracts;
using Identity.Contracts;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;

namespace ShopCore.Application.Compliance;

public interface IComplianceStore
{
    Task Add(ComplianceDocument document, CancellationToken ct);
    Task<List<ComplianceDocument>> List(Guid shopId, CancellationToken ct);
    Task<ComplianceDocument?> Get(Guid shopId, Guid documentId, CancellationToken ct);
    void Remove(ComplianceDocument document);

    /// <summary>Documents of every shop that expire on or before the date, for the reminder job.</summary>
    Task<List<ComplianceDocument>> ExpiringBy(DateOnly date, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record UploadComplianceDocument(
    Guid ShopId, Guid DeviceId, Guid StaffId, string TypeCode, DateOnly? ExpiresOn, string FileName, string ContentType, byte[] Content)
    : ICommand<Guid>;

internal sealed class UploadComplianceDocumentHandler(
    IComplianceStore store, IFileStore files, ITillAccessChecker access, ILookupChecker lookups, TimeProvider clock)
    : ICommandHandler<UploadComplianceDocument, Guid>
{
    public async Task<Result<Guid>> Handle(UploadComplianceDocument c, CancellationToken ct)
    {
        var allowed = await access.Check(c.ShopId, c.DeviceId, c.StaffId, "compliance.manage", ct);
        if (!allowed.IsSuccess) return Result<Guid>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var file = FileRules.Validate(c.FileName, c.ContentType, c.Content);
        if (!file.IsSuccess) return Result<Guid>.Fail(file.Error!.Code, file.Error.Message);
        if (!await lookups.IsActive("document_types", c.TypeCode, ct))
            return Result<Guid>.Fail("compliance.type_invalid", "Unknown document type.");

        var now = clock.GetUtcNow();
        var id = Guid.CreateVersion7();
        var url = await files.Save("compliance", c.FileName.Trim(), c.ContentType, c.Content, ct);

        var recorded = ComplianceDocument.Record(id, c.ShopId, c.StaffId, c.DeviceId, c.TypeCode, url, c.FileName.Trim(), c.ExpiresOn, now);
        if (!recorded.IsSuccess)
        {
            await files.Delete(url, ct);
            return Result<Guid>.Fail(recorded.Error!.Code, recorded.Error.Message);
        }

        try
        {
            await store.Add(recorded.Value!, ct);
            await store.Save(ct);
        }
        catch
        {
            // Never leave a stored file that no record points at.
            await files.Delete(url, ct);
            throw;
        }
        return Result<Guid>.Ok(id);
    }
}

public sealed record ListComplianceDocuments(Guid ShopId) : IQuery<List<ComplianceDocumentResponse>>;

internal sealed class ListComplianceDocumentsHandler(IComplianceStore store, TimeProvider clock)
    : IQueryHandler<ListComplianceDocuments, List<ComplianceDocumentResponse>>
{
    public async Task<Result<List<ComplianceDocumentResponse>>> Handle(ListComplianceDocuments query, CancellationToken ct)
    {
        var today = BusinessDay.Of(clock.GetUtcNow());
        var docs = await store.List(query.ShopId, ct);
        return Result<List<ComplianceDocumentResponse>>.Ok(docs
            .OrderBy(d => d.ExpiresOn ?? DateOnly.MaxValue).ThenBy(d => d.TypeCode)
            .Select(d => new ComplianceDocumentResponse(d.Id, d.TypeCode, d.OriginalName, d.ExpiresOn,
                ComplianceRules.DaysLeft(d.ExpiresOn, today), ToText(ComplianceRules.StatusOf(d.ExpiresOn, today))))
            .ToList());
    }

    private static string ToText(ComplianceStatus s) => s switch
    {
        ComplianceStatus.ExpiringSoon => "expiring_soon",
        ComplianceStatus.Expired => "expired",
        _ => "valid",
    };
}

public sealed record GetComplianceFile(Guid ShopId, Guid DocumentId) : IQuery<StoredFile>;

internal sealed class GetComplianceFileHandler(IComplianceStore store, IFileStore files) : IQueryHandler<GetComplianceFile, StoredFile>
{
    public async Task<Result<StoredFile>> Handle(GetComplianceFile query, CancellationToken ct)
    {
        // The shop id is part of the lookup, so one shop can never open another shop's document.
        var doc = await store.Get(query.ShopId, query.DocumentId, ct);
        if (doc is null) return Result<StoredFile>.Fail("compliance.not_found", "Document not found.");
        var file = await files.Open(doc.FileUrl, ct);
        return file is null
            ? Result<StoredFile>.Fail("compliance.file_unavailable", "The file could not be read. Upload it again.")
            : Result<StoredFile>.Ok(file);
    }
}

public sealed record DeleteComplianceDocument(Guid ShopId, Guid DeviceId, Guid StaffId, Guid DocumentId) : ICommand<Unit>;

internal sealed class DeleteComplianceDocumentHandler(IComplianceStore store, IFileStore files, ITillAccessChecker access)
    : ICommandHandler<DeleteComplianceDocument, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteComplianceDocument c, CancellationToken ct)
    {
        var allowed = await access.Check(c.ShopId, c.DeviceId, c.StaffId, "compliance.manage", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var doc = await store.Get(c.ShopId, c.DocumentId, ct);
        if (doc is null) return Result<Unit>.Fail("compliance.not_found", "Document not found.");

        store.Remove(doc);
        await store.Save(ct);
        await files.Delete(doc.FileUrl, ct);
        return Result<Unit>.Ok(default);
    }
}

/// <summary>Used by the worker: which documents need a 30-day, 7-day or expired reminder right now.</summary>
public sealed record FindDueComplianceReminders : IQuery<List<DueComplianceReminder>>;

internal sealed class FindDueComplianceRemindersHandler(IComplianceStore store, TimeProvider clock)
    : IQueryHandler<FindDueComplianceReminders, List<DueComplianceReminder>>
{
    public async Task<Result<List<DueComplianceReminder>>> Handle(FindDueComplianceReminders query, CancellationToken ct)
    {
        var today = BusinessDay.Of(clock.GetUtcNow());
        var docs = await store.ExpiringBy(today.AddDays(ComplianceRules.FirstReminderDays), ct);
        return Result<List<DueComplianceReminder>>.Ok(docs
            .Select(d => (Doc: d, Reminder: ComplianceRules.ReminderDue(d.ExpiresOn, today)))
            .Where(x => x.Reminder is not null)
            .Select(x => new DueComplianceReminder(x.Doc.ShopId, x.Doc.Id, x.Doc.TypeCode, x.Doc.ExpiresOn, x.Reminder!.Value,
                ComplianceRules.DedupeKey(x.Doc.Id, x.Reminder.Value)))
            .ToList());
    }
}
