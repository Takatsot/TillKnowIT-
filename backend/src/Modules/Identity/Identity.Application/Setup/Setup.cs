using Identity.Contracts;
using Identity.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Setup;

public interface ISetupStore
{
    Task<Organisation?> GetShop(Guid shopId, CancellationToken ct);
    Task<List<SetupStep>> Steps(Guid shopId, CancellationToken ct);
    Task Add(SetupStep step, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public interface IMembershipReadStore
{
    /// <summary>Active memberships of a person, with the organisation and role names.</summary>
    Task<List<MembershipResponse>> ForPerson(Guid personId, CancellationToken ct);
}

/// <summary>E1-5: the places a person can open. The till shows a choice only when there is more than one.</summary>
public sealed record ListMemberships(Guid PersonId) : IQuery<List<MembershipResponse>>;

internal sealed class ListMembershipsHandler(IMembershipReadStore store) : IQueryHandler<ListMemberships, List<MembershipResponse>>
{
    public async Task<Result<List<MembershipResponse>>> Handle(ListMemberships query, CancellationToken ct) =>
        Result<List<MembershipResponse>>.Ok((await store.ForPerson(query.PersonId, ct))
            .OrderBy(m => m.OrganisationName, StringComparer.CurrentCultureIgnoreCase).ToList());
}

/// <summary>A field agent ticks off a step at the shop. Doing it again just updates the note.</summary>
public sealed record CompleteSetupStep(Guid ShopId, Guid AgentPersonId, string Item, string? Note) : ICommand<Unit>;

internal sealed class CompleteSetupStepHandler(ISetupStore store, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<CompleteSetupStep, Unit>
{
    public async Task<Result<Unit>> Handle(CompleteSetupStep c, CancellationToken ct)
    {
        if (!SetupChecklist.IsItem(c.Item))
            return Result<Unit>.Fail("setup.item_invalid", $"Steps are: {string.Join(", ", SetupChecklist.Items)}.");
        if (!await permissions.Has(c.AgentPersonId, "shop.setup", ct))
            return Result<Unit>.Fail("permission.denied", "Only a field agent can complete setup steps.");

        var shop = await store.GetShop(c.ShopId, ct);
        if (shop is null || shop.Type != OrganisationType.Shop) return Result<Unit>.Fail("shop.not_found", "Shop not found.");

        var done = await store.Steps(c.ShopId, ct);
        if (done.All(s => s.Item != c.Item))
        {
            await store.Add(new SetupStep(c.ShopId, c.Item, c.AgentPersonId, c.Note, clock.GetUtcNow()), ct);
            await store.Save(ct);
        }
        return Result<Unit>.Ok(default);
    }
}

public sealed record GetSetupChecklist(Guid ShopId) : IQuery<SetupChecklistResponse>;

internal sealed class GetSetupChecklistHandler(ISetupStore store) : IQueryHandler<GetSetupChecklist, SetupChecklistResponse>
{
    public async Task<Result<SetupChecklistResponse>> Handle(GetSetupChecklist q, CancellationToken ct)
    {
        var shop = await store.GetShop(q.ShopId, ct);
        if (shop is null || shop.Type != OrganisationType.Shop) return Result<SetupChecklistResponse>.Fail("shop.not_found", "Shop not found.");

        var done = (await store.Steps(q.ShopId, ct)).ToDictionary(s => s.Item);
        var steps = SetupChecklist.Items
            .Select(i => done.TryGetValue(i, out var s) ? new SetupStepResponse(i, true, s.CompletedAt, s.Note) : new SetupStepResponse(i, false, null, null))
            .ToList();
        return Result<SetupChecklistResponse>.Ok(new SetupChecklistResponse(steps, steps.All(s => s.Done), shop.Status.ToString().ToLowerInvariant()));
    }
}

/// <summary>E1-1 and E13-1: the shop stays "pending verification" until every step is done and a field agent verifies it.</summary>
public sealed record VerifyShop(Guid ShopId, Guid AgentPersonId) : ICommand<Unit>;

internal sealed class VerifyShopHandler(ISetupStore store, IPermissionChecker permissions) : ICommandHandler<VerifyShop, Unit>
{
    public async Task<Result<Unit>> Handle(VerifyShop c, CancellationToken ct)
    {
        if (!await permissions.Has(c.AgentPersonId, "shop.verify", ct))
            return Result<Unit>.Fail("permission.denied", "Only a field agent or platform admin can verify a shop.");

        var shop = await store.GetShop(c.ShopId, ct);
        if (shop is null || shop.Type != OrganisationType.Shop) return Result<Unit>.Fail("shop.not_found", "Shop not found.");

        var done = await store.Steps(c.ShopId, ct);
        if (!SetupChecklist.IsComplete(done.Select(s => s.Item)))
        {
            var missing = SetupChecklist.Items.Except(done.Select(s => s.Item));
            return Result<Unit>.Fail("setup.incomplete", $"Still to do: {string.Join(", ", missing)}.");
        }

        var verified = shop.Verify();
        if (!verified.IsSuccess) return Result<Unit>.Fail(verified.Error!.Code, verified.Error.Message);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
