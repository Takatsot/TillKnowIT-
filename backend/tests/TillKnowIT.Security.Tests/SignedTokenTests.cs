using TillKnowIT.Security;

namespace TillKnowIT.Security.Tests;

public class SignedTokenTests
{
    private const string Key = "a-long-enough-test-key";
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_signed_token_gives_its_payload_back_only_under_the_same_key()
    {
        var token = SignedToken.Sign(Key, "wallet=abc|day=2026-10-26");

        Assert.Equal("wallet=abc|day=2026-10-26", SignedToken.Verify(Key, token));
        Assert.Null(SignedToken.Verify("another-key", token));
    }

    [Fact]
    public void A_changed_payload_or_mac_or_a_garbled_or_empty_token_is_refused()
    {
        var token = SignedToken.Sign(Key, "amount=100");
        var parts = token.Split('.');
        var forgedPayload = SignedToken.Sign(Key, "amount=999").Split('.')[0] + "." + parts[1];
        var flippedMac = parts[0] + "." + (parts[1][0] == 'A' ? 'B' : 'A') + parts[1][1..];

        Assert.Null(SignedToken.Verify(Key, forgedPayload));
        Assert.Null(SignedToken.Verify(Key, flippedMac));
        Assert.Null(SignedToken.Verify(Key, "not a token"));
        Assert.Null(SignedToken.Verify(Key, "a.b.c"));
        Assert.Null(SignedToken.Verify(Key, "!!!.???"));
        Assert.Null(SignedToken.Verify(Key, ""));
        Assert.Null(SignedToken.Verify(Key, null));
        Assert.Null(SignedToken.Verify("", token));
    }

    [Fact]
    public void Unicode_payloads_survive()
    {
        Assert.Equal("Thandi – Gr 5ä", SignedToken.Verify(Key, SignedToken.Sign(Key, "Thandi – Gr 5ä")));
    }

    [Fact]
    public void An_expiring_token_works_for_its_lifetime_and_a_few_seconds_of_clock_difference_then_stops()
    {
        var (token, expires) = ExpiringToken.Issue(Key, "wallet:abc", Now, TimeSpan.FromSeconds(60));

        Assert.Equal(Now.AddSeconds(60), expires);
        Assert.Equal("wallet:abc", ExpiringToken.Verify(Key, token, Now.AddSeconds(59)));
        Assert.Equal("wallet:abc", ExpiringToken.Verify(Key, token, Now.AddSeconds(64)));
        Assert.Null(ExpiringToken.Verify(Key, token, Now.AddSeconds(66)));
    }

    [Fact]
    public void An_expiring_token_cannot_have_its_expiry_extended()
    {
        var (token, _) = ExpiringToken.Issue(Key, "wallet:abc", Now, TimeSpan.FromSeconds(60));
        var (later, _) = ExpiringToken.Issue("someone-elses-key", "wallet:abc", Now.AddHours(1), TimeSpan.FromSeconds(60));

        Assert.Null(ExpiringToken.Verify(Key, later, Now.AddHours(1)));
        Assert.Null(ExpiringToken.Verify(Key, token, Now.AddHours(1)));
    }
}
