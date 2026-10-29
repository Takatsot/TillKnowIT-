using Identity.Application.Auth;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Identity.Contracts;

namespace Identity.Infrastructure;

internal sealed class AuthStore(IdentityDbContext db) : IAuthStore
{
    public async Task<PersonRef?> PersonByPhone(string phoneKey, CancellationToken ct)
    {
        if (phoneKey.Length < 9) return null;
        var rows = await db.People.AsNoTracking().Where(p => p.Phone.Replace(" ", "").Replace("-", "").EndsWith(phoneKey)).Take(2).ToListAsync(ct);
        return rows.Count == 1 ? new PersonRef(rows[0].Id, rows[0].DisplayName, rows[0].Phone, rows[0].PreferredLanguage) : null;   // two people on one number: nobody signs in until that is sorted out
    }

    public void Add(OtpChallenge challenge) => db.Set<OtpChallenge>().Add(challenge);

    public Task<OtpChallenge?> LatestOpen(string phoneKey, DateTimeOffset now, CancellationToken ct) =>
        db.Set<OtpChallenge>().Where(c => c.PhoneKey == phoneKey && c.UsedAt == null && c.Attempts < OtpChallenge.MaxAttempts && c.ExpiresAt > now).OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    public Task<int> RequestedSince(string phoneKey, DateTimeOffset since, CancellationToken ct) => db.Set<OtpChallenge>().CountAsync(c => c.PhoneKey == phoneKey && c.CreatedAt >= since, ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class OtpChallengeConfiguration : Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<OtpChallenge> b)
    {
        b.ToTable("otp_challenges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PhoneKey).HasMaxLength(20).IsRequired();
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.PhoneKey, x.CreatedAt });
    }
}

public static class AuthEndpoints
{
    public static IServiceCollection AddIdentityAuth(this IServiceCollection services, IConfiguration config, bool development)
    {
        var key = config["Auth:TokenKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            if (!development) throw new InvalidOperationException("Auth:TokenKey must be set to a secret of at least 32 characters.");
            key = "dev-only-auth-token-key-0123456789abcdef";
        }
        var hours = double.TryParse(config["Auth:TokenHours"], out var h) ? h : 12;
        services.AddSingleton(new AuthSettings(key, TimeSpan.FromHours(hours), ExposeDevCode: development && config.GetValue("Auth:ExposeDevCode", true)));
        services.AddScoped<IAuthStore, AuthStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/auth/otp/request", async (RequestOtpRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var r = await d.Send(new RequestOtp(req), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(title: r.Error!.Message, statusCode: r.Error.Code == "otp.too_many" ? 429 : 422, extensions: new Dictionary<string, object?> { ["code"] = r.Error.Code });
        });
        api.MapPost("/auth/otp/verify", async (VerifyOtpRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var r = await d.Send(new VerifyOtp(req), ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.Problem(title: r.Error!.Message, statusCode: 401, extensions: new Dictionary<string, object?> { ["code"] = r.Error.Code });
        });
        return api;
    }
}
