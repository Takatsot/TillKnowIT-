using System.Text;
using Wallet.Application;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application.Tests;

public class SchoolFlowTests
{
    private readonly WalletWorld _w = new();

    // ---- W1-1: schools ----

    [Fact]
    public async Task Only_a_platform_admin_registers_a_school_for_a_real_tuckshop_that_is_not_already_taken()
    {
        Assert.Equal("permission.denied", (await _w.Schools.Handle(new RegisterSchool(new(Guid.NewGuid(), "Sunrise", null, _w.ShopId)), default)).Error!.Code);
        Assert.Equal("school.shop_not_found", (await _w.Schools.Handle(new RegisterSchool(new(_w.PlatformAdmin, "Sunrise", null, Guid.NewGuid())), default)).Error!.Code);

        var school = await _w.Schools.Handle(new RegisterSchool(new(_w.PlatformAdmin, "Sunrise", "200301234", _w.ShopId)), default);

        Assert.Equal(("pending", _w.ShopId), (school.Value!.Status, school.Value.ShopId));
        Assert.Equal("school.shop_taken", (await _w.Schools.Handle(new RegisterSchool(new(_w.PlatformAdmin, "Another", null, _w.ShopId)), default)).Error!.Code);
        Assert.True(await _w.Store.AccountExists(AccountIds.ShopPayable(_w.ShopId), default));
    }

    [Fact]
    public async Task A_school_is_verified_only_when_its_tuckshop_is_and_only_by_the_platform()
    {
        var school = (await _w.Schools.Handle(new RegisterSchool(new(_w.PlatformAdmin, "Sunrise", null, _w.ShopId)), default)).Value!;
        _w.Identity.VerifiedShops[_w.ShopId] = "pending";

        Assert.Equal("school.shop_not_verified", (await _w.Schools.Handle(new VerifySchool(school.Id, _w.PlatformAdmin), default)).Error!.Code);
        _w.Identity.VerifiedShops[_w.ShopId] = "verified";
        Assert.Equal("permission.denied", (await _w.Schools.Handle(new VerifySchool(school.Id, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("verified", (await _w.Schools.Handle(new VerifySchool(school.Id, _w.PlatformAdmin), default)).Value!.Status);
    }

    [Fact]
    public async Task A_school_admin_can_change_settings_but_never_the_fee_the_platform_keeps()
    {
        var school = await _w.OpenSchool(feeBasisPoints: 150);

        var withSameFee = await _w.Schools.Handle(new ConfigureSchool(school.Id, new(_w.SchoolAdmin, 150, "weekly", true, 20_000, 30)), default);
        var withNewFee = await _w.Schools.Handle(new ConfigureSchool(school.Id, new(_w.SchoolAdmin, 0, "weekly", true, 20_000, 30)), default);
        var stranger = await _w.Schools.Handle(new ConfigureSchool(school.Id, new(Guid.NewGuid(), 150, "weekly", true, null, 30)), default);

        Assert.Equal(("weekly", true, 20_000L), (withSameFee.Value!.PayoutSchedule, withSameFee.Value.StaffAccountsEnabled, withSameFee.Value.DailyCeilingCents!.Value));
        Assert.Equal("permission.denied", withNewFee.Error!.Code);
        Assert.Equal("permission.denied", stranger.Error!.Code);
        Assert.Equal("school.schedule_invalid", (await _w.Schools.Handle(new ConfigureSchool(school.Id, new(_w.PlatformAdmin, 150, "yearly", true, null, 30)), default)).Error!.Code);
        Assert.Equal(150, _w.Store.Schools.Single().FeeBasisPoints);
    }

    [Fact]
    public async Task A_school_admin_must_be_someone_who_has_signed_up_and_only_admins_see_the_school()
    {
        var school = await _w.OpenSchool();

        Assert.Equal("school.person_not_found", (await _w.Schools.Handle(new AddSchoolAdmin(school.Id, new(_w.PlatformAdmin, Guid.NewGuid())), default)).Error!.Code);
        Assert.True((await _w.Schools.Handle(new AddSchoolAdmin(school.Id, new(_w.PlatformAdmin, _w.SchoolAdmin)), default)).IsSuccess);   // adding again is harmless
        Assert.Single(_w.Store.Admins);
        Assert.True((await _w.Schools.Handle(new GetSchool(school.Id, _w.SchoolAdmin), default)).IsSuccess);
        Assert.Equal("permission.denied", (await _w.Schools.Handle(new GetSchool(school.Id, Guid.NewGuid()), default)).Error!.Code);
    }

    // ---- W1-2: learners and teachers ----

    [Fact]
    public async Task Adding_a_learner_opens_a_wallet_and_gives_a_one_time_link_code_and_duplicates_are_refused()
    {
        await _w.OpenSchool();

        var added = await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(_w.SchoolAdmin, "Thandi", "Nkosi", "5", "5B", "learner")), default);

        Assert.Equal(8, added.Value!.LinkCode.Length);
        Assert.True(await _w.Store.AccountExists(AccountIds.Wallet(added.Value.Holder.WalletId), default));
        Assert.Equal("holder.duplicate", (await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(_w.SchoolAdmin, " thandi", "NKOSI ", "5", "5b", "learner")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(Guid.NewGuid(), "A", "B", null, null, "learner")), default)).Error!.Code);
        Assert.Equal("holder.type_invalid", (await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(_w.SchoolAdmin, "A", "B", null, null, "alien")), default)).Error!.Code);
    }

    [Fact]
    public async Task A_teacher_also_gets_a_staff_account_in_the_ledger()
    {
        await _w.OpenSchool();

        var teacher = (await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(_w.SchoolAdmin, "Mr", "Dlamini", null, null, "teacher")), default)).Value!;

        Assert.True(await _w.Store.AccountExists(AccountIds.StaffAccount(teacher.Holder.Id), default));
        Assert.Equal("teacher", teacher.Holder.Type);
    }

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task An_import_creates_learners_flags_duplicates_and_bad_rows_and_hands_back_codes_to_print()
    {
        await _w.OpenSchool();
        await _w.EnrolChild("Thandi", "Nkosi", "5", "5B");
        var file = Csv("First Name,Surname,Grade,Class\nSipho,Dube,4,4A\nthandi,NKOSI,5,5b\n,Missing,3,3C\nSipho,Dube,4,4A\nLerato,Mokoena,4,4A\n");

        var result = (await _w.Importer.Handle(new ImportHolders(_w.School.Id, _w.SchoolAdmin, "learners.csv", file), default)).Value!;

        Assert.Equal((2, 2, 1), (result.Created, result.Duplicates, result.Invalid));
        Assert.Equal(["created", "duplicate", "invalid", "duplicate", "created"], result.Rows.Select(r => r.Outcome));
        Assert.Equal(["Sipho Dube", "Lerato Mokoena"], result.LinkCodes.Select(l => l.Name));
        Assert.All(result.LinkCodes, l => Assert.Equal(8, l.LinkCode.Length));
        Assert.Equal(3, _w.Store.Holders.Count);
        Assert.Equal(3, _w.Store.Wallets.Count);
    }

    [Fact]
    public async Task Sending_the_same_import_again_creates_nobody_twice()
    {
        await _w.OpenSchool();
        var file = Csv("first_name,last_name,grade,class\nSipho,Dube,4,4A\nLerato,Mokoena,4,4A\n");

        await _w.Importer.Handle(new ImportHolders(_w.School.Id, _w.SchoolAdmin, "learners.csv", file), default);
        var again = (await _w.Importer.Handle(new ImportHolders(_w.School.Id, _w.SchoolAdmin, "learners.csv", file), default)).Value!;

        Assert.Equal((0, 2), (again.Created, again.Duplicates));
        Assert.Equal(2, _w.Store.Holders.Count);
    }

    [Fact]
    public async Task An_import_needs_name_columns_a_school_admin_and_a_readable_file()
    {
        await _w.OpenSchool();

        Assert.Equal("import.columns_missing", (await _w.Importer.Handle(new ImportHolders(_w.School.Id, _w.SchoolAdmin, "x.csv", Csv("Grade,Class\n4,4A\n")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Importer.Handle(new ImportHolders(_w.School.Id, Guid.NewGuid(), "x.csv", Csv("First Name,Surname\nA,B\n")), default)).Error!.Code);
        Assert.Equal("school.not_found", (await _w.Importer.Handle(new ImportHolders(Guid.NewGuid(), _w.SchoolAdmin, "x.csv", Csv("First Name,Surname\nA,B\n")), default)).Error!.Code);
        Assert.False((await _w.Importer.Handle(new ImportHolders(_w.School.Id, _w.SchoolAdmin, "x.csv", []), default)).IsSuccess);
    }

    [Fact]
    public async Task A_teacher_row_in_an_import_becomes_a_teacher_and_an_unknown_type_is_a_bad_row()
    {
        await _w.OpenSchool();

        var result = (await _w.Importer.Handle(new ImportHolders(_w.School.Id, _w.SchoolAdmin, "x.csv", Csv("first name,surname,type\nMr,Dlamini,teacher\nMs,Jones,wizard\n")), default)).Value!;

        Assert.Equal((1, 1), (result.Created, result.Invalid));
        Assert.Equal(HolderType.Teacher, _w.Store.Holders.Single().Type);
    }

    // ---- W1-3 and W3-3: cards ----

    [Fact]
    public async Task A_card_links_to_one_person_and_cannot_be_linked_twice_even_after_it_is_blocked()
    {
        await _w.OpenSchool();
        var a = await _w.EnrolChild("Thandi", "Nkosi", withCard: false);
        var b = await _w.EnrolChild("Sipho", "Dube", "4", "4A", withCard: false);

        var first = await _w.TokenOps.Handle(new IssueToken(a.HolderId, new(_w.SchoolAdmin, "nfc", "04:A2:2B:1C")), default);
        var again = await _w.TokenOps.Handle(new IssueToken(b.HolderId, new(_w.SchoolAdmin, "nfc", "04a22b1c")), default);
        await _w.TokenOps.Handle(new BlockToken(first.Value!.Id, _w.SchoolAdmin), default);
        var afterBlock = await _w.TokenOps.Handle(new IssueToken(b.HolderId, new(_w.SchoolAdmin, "nfc", "04A22B1C")), default);

        Assert.Equal(("nfc", "active"), (first.Value.Type, first.Value.Status));
        Assert.Equal("token.already_linked", again.Error!.Code);
        Assert.Equal("token.already_linked", afterBlock.Error!.Code);
    }

    [Fact]
    public async Task Issuing_a_card_needs_a_school_admin_a_readable_uid_and_an_active_person()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild(withCard: false);

        Assert.Equal("permission.denied", (await _w.TokenOps.Handle(new IssueToken(child.HolderId, new(Guid.NewGuid(), "nfc", "04A22B1C")), default)).Error!.Code);
        Assert.Equal("token.uid_invalid", (await _w.TokenOps.Handle(new IssueToken(child.HolderId, new(_w.SchoolAdmin, "nfc", "04")), default)).Error!.Code);
        Assert.Equal("token.type_invalid", (await _w.TokenOps.Handle(new IssueToken(child.HolderId, new(_w.SchoolAdmin, "qr", "04A22B1C")), default)).Error!.Code);
        await _w.HolderOps.Handle(new MarkHolderLeft(child.HolderId, _w.SchoolAdmin), default);
        Assert.Equal("holder.not_active", (await _w.TokenOps.Handle(new IssueToken(child.HolderId, new(_w.SchoolAdmin, "nfc", "04A22B1C")), default)).Error!.Code);
    }

    [Fact]
    public async Task A_parent_blocks_all_their_childs_cards_and_a_replacement_works_on_the_same_wallet()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Fund(child, 5_000);

        var blocked = await _w.Wallets.Handle(new BlockMyChildsCards(child.HolderId, child.Parent), default);
        var withLostCard = await _w.Pay(child, 500);
        await _w.TokenOps.Handle(new IssueToken(child.HolderId, new(_w.SchoolAdmin, "nfc", "04FFEE0011")), default);
        var withReplacement = await _w.Payments.Handle(new ProcessWalletPayment(_w.ShopId, new(_w.DeviceId, _w.StaffId, Guid.NewGuid(), "04FFEE0011", null, 500, [new(null, null, "Crisps", 1, 500)])), default);

        Assert.Equal(1, blocked.Value);
        Assert.Equal("token.blocked", withLostCard.Error!.Code);
        Assert.Equal(4_500, withReplacement.Value!.BalanceCents);
        Assert.Equal("permission.denied", (await _w.Wallets.Handle(new BlockMyChildsCards(child.HolderId, Guid.NewGuid()), default)).Error!.Code);
    }

    [Fact]
    public async Task Marking_a_learner_as_left_blocks_their_cards_and_freezes_the_wallet()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Fund(child, 5_000);

        await _w.HolderOps.Handle(new MarkHolderLeft(child.HolderId, _w.SchoolAdmin), default);

        Assert.Equal("token.blocked", (await _w.Pay(child, 500)).Error!.Code);
        Assert.Equal(WalletStatus.Frozen, _w.Store.Wallets.Single().Status);
        Assert.Equal("holder.not_active", (await _w.HolderOps.Handle(new MarkHolderLeft(child.HolderId, _w.SchoolAdmin), default)).Error!.Code);
    }

    // ---- photos ----

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0];

    [Fact]
    public async Task A_photo_is_stored_and_only_a_till_of_that_school_can_fetch_it()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();

        Assert.True((await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "image/jpeg", Jpeg()), default)).IsSuccess);

        Assert.NotEmpty((await _w.Photos.Handle(new GetHolderPhoto(child.HolderId, _w.DeviceId, _w.StaffId), default)).Value!.Content);
        Assert.Equal("device.not_approved", (await _w.Photos.Handle(new GetHolderPhoto(child.HolderId, Guid.NewGuid(), _w.StaffId), default)).Error!.Code);
        Assert.Equal("permission.denied", (await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, Guid.NewGuid(), "image/jpeg", Jpeg()), default)).Error!.Code);
        Assert.Equal("file.not_an_image", (await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 test")), default)).Error!.Code);
        Assert.Equal("file.content_mismatch", (await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "image/jpeg", Encoding.ASCII.GetBytes("not a picture at all")), default)).Error!.Code);
    }

    [Fact]
    public async Task A_new_photo_replaces_the_old_one_which_is_deleted()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "image/jpeg", Jpeg()), default);
        var first = _w.Files.Files.Keys.Single();

        await _w.Photos.Handle(new SetHolderPhoto(child.HolderId, _w.SchoolAdmin, "image/jpeg", Jpeg()), default);

        Assert.DoesNotContain(first, _w.Files.Files.Keys);
        Assert.Single(_w.Files.Files);
    }

    // ---- W2-1: a parent links a child ----

    [Fact]
    public async Task A_parent_links_with_the_code_after_consent_and_sees_the_child_and_the_balance()
    {
        await _w.OpenSchool();
        var added = (await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(_w.SchoolAdmin, "Thandi", "Nkosi", "5", "5B", "learner")), default)).Value!;
        var parent = Guid.NewGuid();
        _w.Identity.People.Add(parent);

        var linked = await _w.Guardians.Handle(new LinkChild(new(parent, added.LinkCode.ToLowerInvariant().Insert(4, "-"), true, "popia-v1")), default);

        Assert.Equal(("Thandi Nkosi", "Sunrise Primary", 0L), (linked.Value!.Name, linked.Value.School, linked.Value.BalanceCents));
        var consent = Assert.Single(_w.Store.Guardianships);
        Assert.Equal("popia-v1", consent.ConsentVersion);
        Assert.Single((await _w.Guardians.Handle(new GetMyChildren(parent), default)).Value!);
    }

    [Fact]
    public async Task Linking_needs_consent_first_a_valid_code_and_has_a_limit_of_parents()
    {
        await _w.OpenSchool();
        var added = (await _w.HolderOps.Handle(new AddHolder(_w.School.Id, new(_w.SchoolAdmin, "Thandi", "Nkosi", "5", "5B", "learner")), default)).Value!;
        var parent = Guid.NewGuid();
        _w.Identity.People.Add(parent);

        Assert.Equal("consent.required", (await _w.Guardians.Handle(new LinkChild(new(parent, added.LinkCode, false, "v1")), default)).Error!.Code);
        Assert.Equal("consent.required", (await _w.Guardians.Handle(new LinkChild(new(parent, added.LinkCode, true, " ")), default)).Error!.Code);
        Assert.Equal("link.code_invalid", (await _w.Guardians.Handle(new LinkChild(new(parent, "AAAAAAAA", true, "v1")), default)).Error!.Code);
        Assert.Equal("link.code_invalid", (await _w.Guardians.Handle(new LinkChild(new(parent, "short", true, "v1")), default)).Error!.Code);
        Assert.Equal("person.not_found", (await _w.Guardians.Handle(new LinkChild(new(Guid.NewGuid(), added.LinkCode, true, "v1")), default)).Error!.Code);
        Assert.Empty(_w.Store.Guardianships);

        Assert.True((await _w.Guardians.Handle(new LinkChild(new(parent, added.LinkCode, true, "v1")), default)).IsSuccess);
        Assert.Equal("link.already_linked", (await _w.Guardians.Handle(new LinkChild(new(parent, added.LinkCode, true, "v1")), default)).Error!.Code);
        for (var i = 0; i < Holder.MaxGuardians - 1; i++)
        {
            var other = Guid.NewGuid();
            _w.Identity.People.Add(other);
            Assert.True((await _w.Guardians.Handle(new LinkChild(new(other, added.LinkCode, true, "v1")), default)).IsSuccess);
        }
        var extra = Guid.NewGuid();
        _w.Identity.People.Add(extra);
        Assert.Equal("link.too_many_guardians", (await _w.Guardians.Handle(new LinkChild(new(extra, added.LinkCode, true, "v1")), default)).Error!.Code);
    }

    [Fact]
    public async Task A_code_stops_working_when_the_school_issues_a_new_one_and_nothing_links_before_the_school_is_verified()
    {
        await _w.OpenSchool();
        var child = await _w.EnrolChild();
        var fresh = (await _w.HolderOps.Handle(new NewLinkCode(child.HolderId, _w.SchoolAdmin), default)).Value!.LinkCode;
        var parent = Guid.NewGuid();
        _w.Identity.People.Add(parent);

        Assert.Equal("link.code_invalid", (await _w.Guardians.Handle(new LinkChild(new(parent, child.LinkCode, true, "v1")), default)).Error!.Code);
        Assert.True((await _w.Guardians.Handle(new LinkChild(new(parent, fresh, true, "v1")), default)).IsSuccess);

        _w.Store.Schools.Single().Suspend();
        var another = Guid.NewGuid();
        _w.Identity.People.Add(another);
        var third = (await _w.HolderOps.Handle(new NewLinkCode(child.HolderId, _w.SchoolAdmin), default)).Value!.LinkCode;
        Assert.Equal("link.school_not_active", (await _w.Guardians.Handle(new LinkChild(new(another, third, true, "v1")), default)).Error!.Code);
    }

    [Fact]
    public async Task Listing_holders_is_for_school_admins_and_hides_erased_people()
    {
        await _w.OpenSchool();
        await _w.EnrolChild("Thandi", "Nkosi");
        var other = await _w.EnrolChild("Sipho", "Dube", "4", "4A");
        _w.Store.Holders.Single(h => h.Id == other.HolderId).Erase();

        var list = (await _w.HolderOps.Handle(new ListHolders(_w.School.Id, _w.SchoolAdmin), default)).Value!;

        Assert.Equal(["Thandi"], list.Select(h => h.FirstName));
        Assert.Equal("permission.denied", (await _w.HolderOps.Handle(new ListHolders(_w.School.Id, Guid.NewGuid()), default)).Error!.Code);
    }
}
