using System.Reflection;
using NetArchTest.Rules;

namespace TillKnowIT.Architecture.Tests;

public class ModuleRulesTests
{
    private static readonly string[] Modules = ["ShopCore", "Catalogue", "Marketplace", "Messaging", "Identity", "Payments"];

    private static Assembly Load(string module, string layer) => Assembly.Load($"{module}.{layer}");

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_does_not_depend_on_application_or_infrastructure(string module)
    {
        var result = Types.InAssembly(Load(module, "Domain")).ShouldNot()
            .HaveDependencyOnAny($"{module}.Application", $"{module}.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_does_not_depend_on_infrastructure(string module)
    {
        var result = Types.InAssembly(Load(module, "Application")).ShouldNot()
            .HaveDependencyOn($"{module}.Infrastructure").GetResult();
        Assert.True(result.IsSuccessful);
    }

    /// <summary>A module may reference another module's Contracts project only.</summary>
    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Modules_only_reference_other_modules_through_contracts(string module)
    {
        var others = Modules.Where(m => m != module).ToArray();
        foreach (var layer in new[] { "Domain", "Application", "Infrastructure" })
        {
            var forbidden = others.SelectMany(o => new[] { $"{o}.Domain", $"{o}.Application", $"{o}.Infrastructure" }).ToArray();
            var result = Types.InAssembly(Load(module, layer)).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            Assert.True(result.IsSuccessful, $"{module}.{layer} references another module's internals");
        }
    }

    public static IEnumerable<object[]> ModuleNames() => Modules.Select(m => new object[] { m });
}
