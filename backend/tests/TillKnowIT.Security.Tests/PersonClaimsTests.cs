using TillKnowIT.Security;

namespace TillKnowIT.Security.Tests;

public class PersonClaimsTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static KeyValuePair<string, string?> F(string key, Guid id) => new(key, id.ToString());

    [Fact]
    public void A_caller_field_names_the_actor_and_a_plain_person_id_next_to_it_is_only_the_person_acted_on()
    {
        Assert.Equal([A], PersonClaims.From([F("callerPersonId", A), F("personId", B)]));
        Assert.Equal([A], PersonClaims.From([F("personId", A)]));
        Assert.Equal([A], PersonClaims.From([F("CallerPersonId", A)]));
        Assert.Empty(PersonClaims.From([F("shopId", A), new("callerPersonId", "not-a-guid")]));
    }

    [Fact]
    public void A_json_body_is_read_at_the_top_and_one_level_down()
    {
        Assert.Equal([A], PersonClaims.FromJson($"{{\"callerPersonId\":\"{A}\",\"personId\":\"{B}\",\"name\":\"x\"}}"));
        Assert.Equal([A], PersonClaims.FromJson($"{{\"request\":{{\"personId\":\"{A}\"}},\"shopId\":\"{B}\"}}"));
        Assert.Empty(PersonClaims.FromJson($"{{\"a\":{{\"b\":{{\"personId\":\"{A}\"}}}}}}"));   // deeper than the requests really are
        Assert.Empty(PersonClaims.FromJson("not json"));
        Assert.Empty(PersonClaims.FromJson(null));
    }
}
