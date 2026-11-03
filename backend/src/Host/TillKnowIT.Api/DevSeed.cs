using System.Text;
using Identity.Application.Devices;
using Identity.Application.Setup;
using Identity.Application.Shops;
using Identity.Application.Staff;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Microsoft.EntityFrameworkCore;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Wallet.Application;
using Wallet.Contracts;
using ShopCore.Application.Sync;
using ShopCore.Contracts;

/// <summary>
/// Development only: one person for every role, a verified shop with a till, a verified supplier, and a school with a child linked to a parent, so every app can be signed in to.
/// It goes through the same commands the apps use. It runs once: if the demo owner already exists it does nothing. Writes what it made to dev-seed.md beside the API.
/// </summary>
internal static class DevSeed
{
    public const string OwnerPhone = "+27825550001";

    private sealed record Who(string Name, string Phone, string Role, string Apps);

    private static readonly Who Owner = new("Nomsa Dlamini", OwnerPhone, "Shop owner", "Back office");
    private static readonly Who Agent = new("Pieter Botha", "+27825550004", "Field agent", "(sets up and verifies shops)");
    private static readonly Who Admin = new("Alice Admin", "+27825550005", "Platform admin", "Admin");
    private static readonly Who Supplier = new("Sello Mahlangu", "+27825550006", "Supplier admin", "Supplier portal, Insights portal");
    private static readonly Who Parent = new("Lerato Khumalo", "+27825550007", "Parent", "Parent app");

    public static async Task Run(IServiceProvider services, string outputFolder, ILogger log, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        if (await db.People.AnyAsync(p => p.Phone == OwnerPhone, ct))
        {
            log.LogInformation("Dev seed: already done (see dev-seed.md). Delete the database to start again.");
            return;
        }

        static T Ok<T>(Result<T> r, string what) => r.IsSuccess ? r.Value! : throw new InvalidOperationException($"Dev seed: {what} failed: {r.Error!.Code} {r.Error.Message}");
        Guid RoleId(string code) => db.Roles.AsNoTracking().Single(r => r.Code == code).Id;

        // People who are not created by a command of their own.
        var people = new Dictionary<string, Person>();
        foreach (var w in new[] { Owner, Agent, Admin, Supplier, Parent })
        {
            var person = new Person(w.Name, w.Phone);
            people[w.Phone] = person;
            db.People.Add(person);
        }
        await db.SaveChangesAsync(ct);

        // The demo shop and the school's tuckshop: set up and verified the way a field agent does it.
        var shop = Ok(await dispatcher.Send(new CreateShop(people[Owner.Phone].Id, "Mama Nomsa's Spaza", "jhb", "en"), ct), "create shop");
        var tuckshop = Ok(await dispatcher.Send(new CreateShop(people[Owner.Phone].Id, "Sunrise Primary Tuckshop", "jhb", "en"), ct), "create tuckshop");
        db.Memberships.Add(new Membership(people[Agent.Phone].Id, shop, RoleId("field_agent")));
        db.Memberships.Add(new Membership(people[Admin.Phone].Id, shop, RoleId("platform_admin")));
        await db.SaveChangesAsync(ct);
        foreach (var id in new[] { shop, tuckshop })
        {
            foreach (var item in SetupChecklist.Items) Ok(await dispatcher.Send(new CompleteSetupStep(id, people[Agent.Phone].Id, item, "Dev seed"), ct), $"setup step {item}");
            Ok(await dispatcher.Send(new VerifyShop(id, people[Agent.Phone].Id), ct), "verify shop");
        }

        // Staff who use the till with a PIN.
        Ok(await dispatcher.Send(new AddStaff(shop, "Thandi Mokoena", "0825550002", "cashier", "1234"), ct), "add cashier");
        var manager = Ok(await dispatcher.Send(new AddStaff(shop, "Sipho Zulu", "0825550003", "manager", "4321"), ct), "add manager");

        // A till, approved, with its one-time setup code.
        var till = Ok(await dispatcher.Send(new RegisterDevice(shop, "Counter till", people[Owner.Phone].Id), ct), "register till");
        Ok(await dispatcher.Send(new ApproveDevice(till.DeviceId, people[Owner.Phone].Id), ct), "approve till");

        // Some stock to sell, so the till and the reports have something to show.
        var now = DateTimeOffset.UtcNow;
        (string Name, string Barcode, string Category, long Cost, long Price, int Low, int Qty)[] stock =
        [
            ("White bread 700g", "6001234500011", "bread_bakery", 1450, 1899, 10, 40),
            ("Full cream milk 1L", "6001234500028", "dairy_eggs", 1590, 1999, 12, 30),
            ("Eggs tray of 18", "6001234500035", "dairy_eggs", 4200, 5499, 5, 12),
            ("Maize meal 5kg", "6001234500042", "groceries", 5800, 7499, 6, 20),
            ("Sunflower oil 750ml", "6001234500059", "groceries", 4100, 5299, 6, 18),
            ("Coca-Cola 330ml can", "6001234500066", "drinks", 950, 1399, 24, 72),
            ("Simba chips 120g", "6001234500073", "snacks_sweets", 1150, 1599, 12, 36),
            ("Lux soap bar", "6001234500080", "toiletries", 650, 999, 8, 25),
            ("Sunlight dish liquid 750ml", "6001234500097", "household", 2300, 3199, 6, 3),
            ("Rama margarine 500g", "6001234500103", "dairy_eggs", 2900, 3899, 6, 14),
        ];
        var products = new List<ProductUpload>();
        var movements = new List<StockMovementUpload>();
        foreach (var p in stock)
        {
            var id = Guid.CreateVersion7();
            products.Add(new ProductUpload(id, manager, null, p.Name, p.Barcode, p.Category, p.Cost, p.Price, p.Low, null, true, now));
            movements.Add(new StockMovementUpload(Guid.CreateVersion7(), manager, id, "receive", p.Qty, now, null, p.Cost, null, "Mahlangu Wholesale", "DEV-001"));
        }
        var uploaded = Ok(await dispatcher.Send(new UploadBatch(new SyncUploadRequest(shop, till.DeviceId, null, null, null, products)), ct), "upload products");
        if (uploaded.Results.Any(r => r.Status == UploadStatus.Rejected)) throw new InvalidOperationException($"Dev seed: products rejected: {string.Join("; ", uploaded.Results.Where(r => r.Status == UploadStatus.Rejected).Select(r => r.Message))}");
        var received = Ok(await dispatcher.Send(new UploadBatch(new SyncUploadRequest(shop, till.DeviceId, null, movements, null)), ct), "receive stock");
        if (received.Results.Any(r => r.Status == UploadStatus.Rejected)) throw new InvalidOperationException($"Dev seed: stock rejected: {string.Join("; ", received.Results.Where(r => r.Status == UploadStatus.Rejected).Select(r => r.Message))}");

        // A verified supplier that delivers to Johannesburg.
        var supplier = Ok(await dispatcher.Send(new ApplyAsSupplier(new ApplyAsSupplierRequest(people[Supplier.Phone].Id, "Mahlangu Wholesale", null, "Sello Mahlangu", "0825550006", null, null,
            [new AreaRequest("jhb", ["Mon", "Wed", "Fri"])])), ct), "supplier application");
        Ok(await dispatcher.Send(new VerifySupplier(supplier.SupplierId, new VerifySupplierRequest(people[Admin.Phone].Id, [.. SupplierVerification.Items])), ct), "verify supplier");

        // A school with a child, and the child's parent linked to it.
        var school = Ok(await dispatcher.Send(new RegisterSchool(new RegisterSchoolRequest(people[Admin.Phone].Id, "Sunrise Primary", null, tuckshop)), ct), "register school");
        Ok(await dispatcher.Send(new VerifySchool(school.Id, people[Admin.Phone].Id), ct), "verify school");
        var child = Ok(await dispatcher.Send(new AddHolder(school.Id, new AddHolderRequest(people[Admin.Phone].Id, "Themba", "Khumalo", "4", "4B", "learner")), ct), "add child");
        Ok(await dispatcher.Send(new LinkChild(new LinkChildRequest(people[Parent.Phone].Id, child.LinkCode, true, "dev-seed")), ct), "link child");

        var text = new StringBuilder();
        text.AppendLine("# Dev seed");
        text.AppendLine();
        text.AppendLine("Made by the API the first time it started in Development. Delete the database to make it again.");
        text.AppendLine();
        text.AppendLine("## Signing in (back office, supplier portal, admin, parent app, insights portal)");
        text.AppendLine();
        text.AppendLine("Enter the phone number. In Development the sign-in screen shows the code on screen.");
        text.AppendLine();
        text.AppendLine("| Who | Phone | Role | Apps |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var w in new[] { Owner, Agent, Admin, Supplier, Parent }) text.AppendLine($"| {w.Name} | {w.Phone[3..].Insert(0, "0")} | {w.Role} | {w.Apps} |");
        text.AppendLine();
        text.AppendLine("## The till");
        text.AppendLine();
        text.AppendLine("Staff PINs: Thandi Mokoena (cashier) 1234, Sipho Zulu (manager) 4321.");
        text.AppendLine();
        text.AppendLine($"Shop: Mama Nomsa's Spaza, id `{shop}`.");
        text.AppendLine($"Till: Counter till, id `{till.DeviceId}`, one-time setup code `{till.EnrolmentCode}` (used once, on the till's first check-in).");
        text.AppendLine();
        text.AppendLine("On the till (http://localhost:5173) use the set-up screen with the shop id, till id and setup code above. Or, in the browser console, then reload:");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine($"localStorage.setItem('tillknowit.device', JSON.stringify({{shopId: '{shop}', deviceId: '{till.DeviceId}'}}))");
        text.AppendLine($"localStorage.setItem('tillknowit.enrolmentCode', '{till.EnrolmentCode}')");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("## School");
        text.AppendLine();
        text.AppendLine($"Sunrise Primary (tuckshop id `{tuckshop}`), child Themba Khumalo, grade 4B, already linked to Lerato Khumalo. Link code used: `{child.LinkCode}`.");
        Directory.CreateDirectory(outputFolder);
        var path = Path.Combine(outputFolder, "dev-seed.md");
        await File.WriteAllTextAsync(path, text.ToString(), ct);
        log.LogInformation("Dev seed: done. Sign-in phones, PINs and the till's setup code are in {Path}", path);
    }
}
