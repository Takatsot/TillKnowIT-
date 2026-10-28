using System.Reflection;
using NetArchTest.Rules;

namespace TillKnowIT.Architecture.Tests;

public class ModuleRulesTests
{
    private static readonly string[] Modules = ["ShopCore", "Catalogue", "Marketplace", "Messaging", "Identity", "Payments", "Wallet", "Platform", "Customers", "Bookings", "Billing", "Finance"];

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

    // ---- Phase 4: the core sale flow knows nothing about business packs ----

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    public void The_selling_core_does_not_depend_on_the_platform_module(string layer)
    {
        // Features are asked for through ShopCore's own IFeatureGate port; only ShopCore.Infrastructure connects that port to the Platform module.
        var result = Types.InAssembly(Load("ShopCore", layer)).ShouldNot().HaveDependencyOnAny("Platform.Domain", "Platform.Application", "Platform.Infrastructure", "Platform.Contracts").GetResult();

        Assert.True(result.IsSuccessful, $"ShopCore.{layer} depends on the Platform module");
    }

    /// <summary>
    /// Adding a business type must never need a change in the selling code, so the selling code cannot mention one. Pack codes live in the Platform
    /// module's data; if one turns up as a string in ShopCore's domain or application code, a pack-specific special case has crept in.
    /// </summary>
    [Fact]
    public void No_pack_code_appears_in_the_core_selling_code()
    {
        var root = FindBackendRoot();
        var packCodes = new[] { "spaza", "cafe", "butchery", "salon", "clothing_hardware" };
        var offenders = new List<string>();
        foreach (var layer in new[] { "ShopCore.Domain", "ShopCore.Application" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Modules", "ShopCore", layer), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            {
                var text = File.ReadAllText(file);
                foreach (var code in packCodes)
                    if (text.Contains($"\"{code}\"", StringComparison.OrdinalIgnoreCase)) offenders.Add($"{Path.GetFileName(file)} mentions \"{code}\"");
            }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TillKnowIT.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the backend folder.");
    }
}
