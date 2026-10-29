using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.Importing;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

internal static class HolderMapping
{
    public static HolderResponse ToResponse(Holder h, SchoolWallet wallet) => new(
        h.Id, h.SchoolId, h.Type.ToString().ToLowerInvariant(), h.FirstName, h.LastName, h.Grade, h.ClassName, h.Status.ToString().ToLowerInvariant(), wallet.Id, h.PhotoUrl is not null);

    public static bool TryParseType(string? text, out HolderType type)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "" or "learner" or "student" or "pupil": type = HolderType.Learner; return true;
            case "teacher" or "staff": type = HolderType.Teacher; return true;
            default: type = default; return false;
        }
    }

    /// <summary>A new holder with its wallet and ledger accounts, and the first link code. All added to the store, none saved.</summary>
    public static async Task<(Holder Holder, SchoolWallet Wallet, string Code)> Open(IWalletStore store, School school, Holder holder, DateTimeOffset now, CancellationToken ct)
    {
        var wallet = SchoolWallet.Open(holder.Id, school.Id, now);
        await store.Add(holder, ct);
        await store.Add(wallet, ct);
        await store.Add(new LedgerAccount(wallet.AccountId, LedgerAccountType.Wallet, wallet.Id, now), ct);
        if (holder.Type == HolderType.Teacher) await store.Add(new LedgerAccount(AccountIds.StaffAccount(holder.Id), LedgerAccountType.StaffAccount, holder.Id, now), ct);
        return (holder, wallet, holder.IssueLinkCode());
    }
}

// ---------------------------------------------------------------- W1-2: learners and teachers

public sealed record AddHolder(Guid SchoolId, AddHolderRequest Request) : ICommand<HolderWithCode>;
public sealed record UpdateHolder(Guid HolderId, UpdateHolderRequest Request) : ICommand<HolderResponse>;
public sealed record MarkHolderLeft(Guid HolderId, Guid CallerPersonId) : ICommand<Unit>;
public sealed record NewLinkCode(Guid HolderId, Guid CallerPersonId) : ICommand<HolderWithCode>;
public sealed record ListHolders(Guid SchoolId, Guid CallerPersonId) : IQuery<List<HolderResponse>>;

internal sealed class HolderHandlers(IWalletStore store, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<AddHolder, HolderWithCode>, ICommandHandler<UpdateHolder, HolderResponse>, ICommandHandler<MarkHolderLeft, Unit>,
      ICommandHandler<NewLinkCode, HolderWithCode>, IQueryHandler<ListHolders, List<HolderResponse>>
{
    public async Task<Result<HolderWithCode>> Handle(AddHolder c, CancellationToken ct)
    {
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<HolderWithCode>.Fail("school.not_found", "School not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, school.Id, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<HolderWithCode>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!HolderMapping.TryParseType(c.Request.Type, out var type)) return Result<HolderWithCode>.Fail("holder.type_invalid", "Type is learner or teacher.");

        var now = clock.GetUtcNow();
        var created = Holder.Create(school.Id, new HolderInput(c.Request.FirstName, c.Request.LastName, c.Request.Grade, c.Request.ClassName, type), now);
        if (!created.IsSuccess) return Result<HolderWithCode>.Fail(created.Error!.Code, created.Error.Message);

        var existing = (await store.HoldersOf(school.Id, ct)).Where(h => h.Status != HolderStatus.Erased).Select(h => h.Key).ToHashSet();
        if (existing.Contains(created.Value!.Key)) return Result<HolderWithCode>.Fail("holder.duplicate", "Someone with the same name, grade and class is already on the list.");

        var (holder, wallet, code) = await HolderMapping.Open(store, school, created.Value, now, ct);
        await store.Save(ct);
        return Result<HolderWithCode>.Ok(new HolderWithCode(HolderMapping.ToResponse(holder, wallet), code));
    }

    public async Task<Result<HolderResponse>> Handle(UpdateHolder c, CancellationToken ct)
    {
        var holder = await store.GetHolder(c.HolderId, ct);
        if (holder is null) return Result<HolderResponse>.Fail("holder.not_found", "Person not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, holder.SchoolId, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<HolderResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var updated = holder.Update(c.Request.FirstName, c.Request.LastName, c.Request.Grade, c.Request.ClassName);
        if (!updated.IsSuccess) return Result<HolderResponse>.Fail(updated.Error!.Code, updated.Error.Message);
        await store.Save(ct);
        return Result<HolderResponse>.Ok(HolderMapping.ToResponse(holder, (await store.WalletOfHolder(holder.Id, ct))!));
    }

    /// <summary>A learner leaves: their cards stop working at once and the wallet is frozen so only a refund can move what is left.</summary>
    public async Task<Result<Unit>> Handle(MarkHolderLeft c, CancellationToken ct)
    {
        var holder = await store.GetHolder(c.HolderId, ct);
        if (holder is null) return Result<Unit>.Fail("holder.not_found", "Person not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, holder.SchoolId, c.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var left = holder.MarkLeft();
        if (!left.IsSuccess) return Result<Unit>.Fail(left.Error!.Code, left.Error.Message);
        var now = clock.GetUtcNow();
        foreach (var token in await store.TokensOf(holder.Id, ct)) token.Block(now);
        (await store.WalletOfHolder(holder.Id, ct))?.Freeze();
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<HolderWithCode>> Handle(NewLinkCode c, CancellationToken ct)
    {
        var holder = await store.GetHolder(c.HolderId, ct);
        if (holder is null) return Result<HolderWithCode>.Fail("holder.not_found", "Person not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, holder.SchoolId, c.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<HolderWithCode>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (holder.Status != HolderStatus.Active) return Result<HolderWithCode>.Fail("holder.not_active", "This person is not active.");

        var code = holder.IssueLinkCode();
        await store.Save(ct);
        return Result<HolderWithCode>.Ok(new HolderWithCode(HolderMapping.ToResponse(holder, (await store.WalletOfHolder(holder.Id, ct))!), code));
    }

    public async Task<Result<List<HolderResponse>>> Handle(ListHolders q, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Admin(store, permissions, q.SchoolId, q.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<List<HolderResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var wallets = (await store.WalletsOf(q.SchoolId, ct)).ToDictionary(w => w.HolderId);
        return Result<List<HolderResponse>>.Ok((await store.HoldersOf(q.SchoolId, ct)).Where(h => h.Status != HolderStatus.Erased)
            .OrderBy(h => h.LastName).ThenBy(h => h.FirstName).Select(h => HolderMapping.ToResponse(h, wallets[h.Id])).ToList());
    }
}

// ---------------------------------------------------------------- import

/// <summary>An Excel or CSV list of learners and teachers. Rows already on the list are skipped, so the same file can be sent again.</summary>
public sealed record ImportHolders(Guid SchoolId, Guid CallerPersonId, string FileName, byte[] Content) : ICommand<ImportHoldersResult>;

public static class HolderColumns
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["first"] = ["firstname", "first", "givenname", "name", "learnername", "learnerfirstname", "firstnames"],
        ["last"] = ["lastname", "surname", "last", "familyname", "learnersurname"],
        ["grade"] = ["grade", "gr", "year"],
        ["class"] = ["class", "classname", "classroom", "register", "registerclass"],
        ["type"] = ["type", "role", "kind"],
    };

    public static string Key(string header) => new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>The column index of each field, found by header name.</summary>
    public static Dictionary<string, int> Find(IReadOnlyList<string> headers)
    {
        var found = new Dictionary<string, int>();
        for (var i = 0; i < headers.Count; i++)
        {
            var key = Key(headers[i]);
            foreach (var (field, names) in Aliases)
                if (!found.ContainsKey(field) && names.Contains(key)) { found[field] = i; break; }
        }
        return found;
    }
}

internal sealed class ImportHoldersHandler(IWalletStore store, IPermissionChecker permissions, TimeProvider clock) : ICommandHandler<ImportHolders, ImportHoldersResult>
{
    public async Task<Result<ImportHoldersResult>> Handle(ImportHolders c, CancellationToken ct)
    {
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<ImportHoldersResult>.Fail("school.not_found", "School not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, school.Id, c.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<ImportHoldersResult>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var sheet = SheetReader.Read(c.FileName, c.Content);
        if (!sheet.IsSuccess) return Result<ImportHoldersResult>.Fail(sheet.Error!.Code, sheet.Error.Message);
        var columns = HolderColumns.Find(sheet.Value!.Headers);
        if (!columns.ContainsKey("first") || !columns.ContainsKey("last"))
            return Result<ImportHoldersResult>.Fail("import.columns_missing", "The file needs a first name column and a surname column.");

        string Cell(IReadOnlyList<string> row, string field) => columns.TryGetValue(field, out var i) && i < row.Count ? row[i].Trim() : "";

        var now = clock.GetUtcNow();
        var seen = (await store.HoldersOf(school.Id, ct)).Where(h => h.Status != HolderStatus.Erased).Select(h => h.Key).ToHashSet();
        var results = new List<ImportRowResult>();
        var codes = new List<LinkCodeRow>();
        int created = 0, duplicates = 0, invalid = 0;

        for (var n = 0; n < sheet.Value.Rows.Count; n++)
        {
            var row = sheet.Value.Rows[n];
            var rowNumber = n + 2;   // the header is row 1
            var first = Cell(row, "first");
            var last = Cell(row, "last");
            var label = $"{first} {last}".Trim();
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            if (!HolderMapping.TryParseType(Cell(row, "type"), out var type))
            {
                results.Add(new(rowNumber, label, "invalid", "Type is learner or teacher."));
                invalid++;
                continue;
            }
            var made = Holder.Create(school.Id, new HolderInput(first, last, Cell(row, "grade"), Cell(row, "class"), type), now);
            if (!made.IsSuccess)
            {
                results.Add(new(rowNumber, label, "invalid", made.Error!.Message));
                invalid++;
                continue;
            }
            if (!seen.Add(made.Value!.Key))
            {
                results.Add(new(rowNumber, label, "duplicate", "Already on the list."));
                duplicates++;
                continue;
            }

            var (holder, _, code) = await HolderMapping.Open(store, school, made.Value, now, ct);
            codes.Add(new LinkCodeRow(holder.Id, holder.DisplayName, holder.Grade, holder.ClassName, code));
            results.Add(new(rowNumber, label, "created", null));
            created++;
        }

        await store.Save(ct);
        return Result<ImportHoldersResult>.Ok(new ImportHoldersResult(created, duplicates, invalid, results, codes));
    }
}

// ---------------------------------------------------------------- photos: shown only on that school's tills

public sealed record SetHolderPhoto(Guid HolderId, Guid CallerPersonId, string ContentType, byte[] Content) : ICommand<Unit>;
public sealed record GetHolderPhoto(Guid HolderId, Guid DeviceId, Guid StaffId) : IQuery<StoredFile>;

internal sealed class PhotoHandlers(IWalletStore store, IFileStore files, ITillAccessChecker tills, IPermissionChecker permissions)
    : ICommandHandler<SetHolderPhoto, Unit>, IQueryHandler<GetHolderPhoto, StoredFile>
{
    public async Task<Result<Unit>> Handle(SetHolderPhoto c, CancellationToken ct)
    {
        var holder = await store.GetHolder(c.HolderId, ct);
        if (holder is null) return Result<Unit>.Fail("holder.not_found", "Person not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, holder.SchoolId, c.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (holder.Status == HolderStatus.Erased) return Result<Unit>.Fail("holder.erased", "This person's data has been removed.");

        var valid = FileRules.Validate($"{holder.Id:N}", c.ContentType, c.Content);
        if (!valid.IsSuccess) return Result<Unit>.Fail(valid.Error!.Code, valid.Error.Message);
        if (c.ContentType == "application/pdf") return Result<Unit>.Fail("file.not_an_image", "The photo must be a JPEG or PNG image.");

        var old = holder.PhotoUrl;
        holder.SetPhoto(await files.Save("holder-photos", $"{holder.Id:N}", c.ContentType, c.Content, ct));
        await store.Save(ct);
        if (old is not null) await files.Delete(old, ct);
        return Result<Unit>.Ok(default);
    }

    /// <summary>Only a till of the learner's own school can see the photo, and only with a signed-in cashier.</summary>
    public async Task<Result<StoredFile>> Handle(GetHolderPhoto q, CancellationToken ct)
    {
        var holder = await store.GetHolder(q.HolderId, ct);
        if (holder?.PhotoUrl is null) return Result<StoredFile>.Fail("photo.not_found", "No photo.");
        var school = await store.GetSchool(holder.SchoolId, ct);
        var access = await tills.Check(school!.ShopId, q.DeviceId, q.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<StoredFile>.Fail(access.Error!.Code, access.Error.Message);

        var file = await files.Open(holder.PhotoUrl, ct);
        return file is null ? Result<StoredFile>.Fail("photo.not_found", "No photo.") : Result<StoredFile>.Ok(file);
    }
}

// ---------------------------------------------------------------- W1-3 and W3-3: cards and wristbands

public sealed record IssueToken(Guid HolderId, IssueTokenRequest Request) : ICommand<TokenResponse>;
public sealed record BlockToken(Guid TokenId, Guid CallerPersonId) : ICommand<Unit>;

internal sealed class TokenHandlers(IWalletStore store, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<IssueToken, TokenResponse>, ICommandHandler<BlockToken, Unit>
{
    public async Task<Result<TokenResponse>> Handle(IssueToken c, CancellationToken ct)
    {
        var holder = await store.GetHolder(c.HolderId, ct);
        if (holder is null) return Result<TokenResponse>.Fail("holder.not_found", "Person not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, holder.SchoolId, c.Request.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<TokenResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (holder.Status != HolderStatus.Active) return Result<TokenResponse>.Fail("holder.not_active", "This person is not active.");
        if (!Enum.TryParse<TokenType>(c.Request.Type?.Replace("_", ""), ignoreCase: true, out var type)) return Result<TokenResponse>.Fail("token.type_invalid", "Type is nfc.");
        if (type != TokenType.Nfc) return Result<TokenResponse>.Fail("token.type_invalid", "Only card and wristband tokens are issued here; phones use rotating QR codes.");

        if (c.Request.Label is { } label && label.Trim().Length > HolderToken.MaxLabel) return Result<TokenResponse>.Fail("token.label_invalid", $"A card name can be up to {HolderToken.MaxLabel} characters.");
        var raw = new string((c.Request.RawToken ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (raw.Length < 4 || raw.Length > 32) return Result<TokenResponse>.Fail("token.uid_invalid", "Tap the card again: its number could not be read.");

        // A card belongs to one person. A blocked card stays blocked and cannot be linked to someone else.
        var existing = await store.TokenByHash(HolderToken.Hash(raw), ct);
        if (existing is not null) return Result<TokenResponse>.Fail("token.already_linked", "This card is already linked to someone.");

        var token = new HolderToken(holder.Id, type, raw, clock.GetUtcNow(), c.Request.Label);
        await store.Add(token, ct);
        await store.Save(ct);
        return Result<TokenResponse>.Ok(new TokenResponse(token.Id, holder.Id, "nfc", "active", token.Label));
    }

    /// <summary>The school admin blocks a lost card; so does a parent of that child.</summary>
    public async Task<Result<Unit>> Handle(BlockToken c, CancellationToken ct)
    {
        var token = await store.GetToken(c.TokenId, ct);
        if (token is null) return Result<Unit>.Fail("token.not_found", "Card not found.");
        var holder = (await store.GetHolder(token.HolderId, ct))!;
        var allowed = await store.GuardianshipOf(c.CallerPersonId, holder.Id, ct) is not null
            ? Result.Ok()
            : await SchoolAccess.Admin(store, permissions, holder.SchoolId, c.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        token.Block(clock.GetUtcNow());
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
