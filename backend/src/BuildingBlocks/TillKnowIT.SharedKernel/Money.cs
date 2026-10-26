namespace TillKnowIT.SharedKernel;

/// <summary>Money is whole cents, never decimals.</summary>
public readonly record struct Money(long Cents)
{
    public static Money Zero => new(0);
    public static Money operator +(Money a, Money b) => new(a.Cents + b.Cents);
    public static Money operator -(Money a, Money b) => new(a.Cents - b.Cents);
    public static Money operator *(Money a, int qty) => new(a.Cents * qty);
    public override string ToString() => $"R{Cents / 100m:N2}";
}
