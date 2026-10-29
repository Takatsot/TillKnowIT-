using Accounting.Contracts;
using Identity.Contracts;
using Billing.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Accounting.Application;

/// <summary>What the package gave us when the owner signed in. Kept only in the token vault, never in the connection row or a log.</summary>
public sealed record OAuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string TenantId);

/// <summary>
/// Where the packages' tokens are kept. The connection row holds only the reference this returns. The packages rotate refresh tokens (each use gives a new one and
/// cancels the old), so the new set must be written back every time. The real implementation encrypts them; production should put the key in the secrets store.
/// </summary>
public interface IProviderTokenVault
{
    Task<string> Store(string provider, OAuthTokens tokens, CancellationToken ct);
    Task<OAuthTokens?> Load(string reference, CancellationToken ct);
    Task Replace(string reference, OAuthTokens tokens, CancellationToken ct);
}

/// <summary>A package that signs the owner in with a browser redirect (OAuth 2.0, authorisation code) instead of a code pasted by hand.</summary>
public interface IOAuthExporter : IAccountingExporter
{
    /// <summary>The package's sign-in page, which sends the browser back to us with a code and this state.</summary>
    string AuthoriseUrl(string state);
}

/// <summary>Checks the owner may connect this package (permission, plan, known package) before the browser is sent to its sign-in page.</summary>
public sealed record CheckAccountingConnect(Guid ShopId, Guid PersonId, string Provider) : IQuery<AuthoriseInfo>;

public sealed record AuthoriseInfo(string Provider, bool RedirectSignIn);

internal sealed class CheckAccountingConnectHandler(IEnumerable<IAccountingExporter> exporters, IOrganisationAccess access, IEntitlements entitlements, IBranchDirectory branches)
    : IQueryHandler<CheckAccountingConnect, AuthoriseInfo>
{
    public async Task<Result<AuthoriseInfo>> Handle(CheckAccountingConnect q, CancellationToken ct)
    {
        if (!await access.Has(q.PersonId, q.ShopId, "shop.settings", ct)) return Result<AuthoriseInfo>.Fail("permission.denied", "Only the owner can connect an accounting package.");
        if (!await entitlements.Has(await branches.HeadOf(q.ShopId, ct), "shop", PlanFeatures.AccountingExport, ct)) return Result<AuthoriseInfo>.Fail("plan.feature_not_included", "Accounting export is not part of your plan.");
        var exporter = exporters.FirstOrDefault(e => e.Provider.Equals(q.Provider?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exporter is null) return Result<AuthoriseInfo>.Fail("accounting.provider_unknown", "Choose Xero or Sage.");
        return Result<AuthoriseInfo>.Ok(new(exporter.Provider, exporter is IOAuthExporter));
    }
}
