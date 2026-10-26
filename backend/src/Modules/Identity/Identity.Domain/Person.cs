using TillKnowIT.SharedKernel;

namespace Identity.Domain;

public sealed class Person : VersionedEntity
{
    private Person() { }

    public Person(string displayName, string phone, string preferredLanguage = "en")
    {
        DisplayName = displayName;
        Phone = phone;
        PreferredLanguage = preferredLanguage;
    }

    public string DisplayName { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public string? Email { get; private set; }
    public string PreferredLanguage { get; private set; } = "en";
}
