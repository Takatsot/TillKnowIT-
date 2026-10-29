using Identity.Contracts;
using Identity.Domain;
using Messaging.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.Security;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Auth;

public sealed record AuthSettings(string TokenKey, TimeSpan TokenLife, bool ExposeDevCode);

public sealed record PersonRef(Guid Id, string DisplayName, string Phone, string Language);

public interface IAuthStore
{
    Task<PersonRef?> PersonByPhone(string phoneKey, CancellationToken ct);
    void Add(OtpChallenge challenge);
    Task<OtpChallenge?> LatestOpen(string phoneKey, DateTimeOffset now, CancellationToken ct);
    Task<int> RequestedSince(string phoneKey, DateTimeOffset since, CancellationToken ct);
    Task Save(CancellationToken ct);
}

public sealed record RequestOtp(RequestOtpRequest Request) : ICommand<RequestOtpResponse>;
public sealed record VerifyOtp(VerifyOtpRequest Request) : ICommand<SessionResponse>;

/// <summary>Sign in with a code sent by SMS. The session is a signed token that names the person and stops working after a while.</summary>
internal sealed class OtpHandlers(IAuthStore store, IMessageQueue queue, IDestinationChecker destinations, AuthSettings settings, TimeProvider clock)
    : ICommandHandler<RequestOtp, RequestOtpResponse>, ICommandHandler<VerifyOtp, SessionResponse>
{
    public const int MaxRequestsPerHour = 5;
    private const string Message = "If that number is registered, a code has been sent to it.";

    /// <summary>The last nine digits, so +27 82 123 4567, 0821234567 and 27821234567 are one number.</summary>
    public static string PhoneKey(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return digits.Length >= 9 ? digits[^9..] : digits;
    }

    public async Task<Result<RequestOtpResponse>> Handle(RequestOtp c, CancellationToken ct)
    {
        var checkedPhone = destinations.Check("sms", c.Request.Phone);
        if (!checkedPhone.IsSuccess) return Result<RequestOtpResponse>.Fail("otp.phone_invalid", "Enter a South African cell number.");
        var key = PhoneKey(checkedPhone.Value);
        var now = clock.GetUtcNow();

        if (await store.RequestedSince(key, now.AddHours(-1), ct) >= MaxRequestsPerHour)
            return Result<RequestOtpResponse>.Fail("otp.too_many", "Too many codes asked for. Try again in an hour.");

        var person = await store.PersonByPhone(key, ct);
        if (person is null) return Result<RequestOtpResponse>.Ok(new(Message, null));   // no hint that there is no such account

        var (challenge, code) = OtpChallenge.Issue(person.Id, key, now);
        store.Add(challenge);
        await store.Save(ct);
        await queue.Queue(new QueueMessageRequest("otp.code", person.Language, "sms", checkedPhone.Value!, new() { ["code"] = code }, null, $"otp:{challenge.Id:N}"), ct);
        return Result<RequestOtpResponse>.Ok(new(Message, settings.ExposeDevCode ? code : null));
    }

    public async Task<Result<SessionResponse>> Handle(VerifyOtp c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var checkedPhone = destinations.Check("sms", c.Request.Phone);
        var invalid = Result<SessionResponse>.Fail("otp.invalid", "That code is not right or has expired. Ask for a new one.");
        if (!checkedPhone.IsSuccess) return invalid;

        var challenge = await store.LatestOpen(PhoneKey(checkedPhone.Value), now, ct);
        if (challenge is null) return invalid;
        var result = challenge.Check(c.Request.Code, now);
        await store.Save(ct);   // a wrong try is counted even though it fails
        if (!result.IsSuccess) return invalid;

        var person = await store.PersonByPhone(challenge.PhoneKey, ct);
        if (person is null || person.Id != challenge.PersonId) return invalid;
        var (token, expires) = ExpiringToken.Issue(settings.TokenKey, person.Id.ToString(), now, settings.TokenLife);
        return Result<SessionResponse>.Ok(new(token, expires, person.Id, person.DisplayName, person.Language));
    }
}
