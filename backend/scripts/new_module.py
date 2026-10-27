#!/usr/bin/env python3
"""
Scaffold a new module: python scripts/new_module.py <Name> <schema>

Creates <Name>.Domain / .Application / .Infrastructure / .Contracts with the references the architecture tests
expect, a DbContext with its own schema and outbox, the module registration, a test project, and wires the module
into the API, the Worker, the architecture tests and scripts/check-migrations.sh.
"""
import os
import re
import subprocess
import sys

if len(sys.argv) != 3:
    sys.exit("usage: new_module.py <Name> <schema>")
NAME, SCHEMA = sys.argv[1], sys.argv[2]

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MOD = os.path.join(ROOT, "src", "Modules", NAME)
if os.path.exists(MOD):
    sys.exit(f"{NAME} already exists")


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def csproj(refs, packages=(), framework=False, visible_to=None):
    parts = ['<Project Sdk="Microsoft.NET.Sdk">', ""]
    if framework:
        parts += ["  <ItemGroup>", '    <FrameworkReference Include="Microsoft.AspNetCore.App" />', "  </ItemGroup>", ""]
    if refs:
        parts += ["  <ItemGroup>"] + [f'    <ProjectReference Include="{r}" />' for r in refs] + ["  </ItemGroup>", ""]
    if packages:
        parts += ["  <ItemGroup>"] + [f'    <PackageReference Include="{n}" Version="{v}" />' for n, v in packages] + ["  </ItemGroup>", ""]
    if visible_to:
        parts += ["  <ItemGroup>", f'    <InternalsVisibleTo Include="{visible_to}" />', "  </ItemGroup>", ""]
    parts.append("</Project>")
    return "\n".join(parts) + "\n"


BB = r"..\..\..\BuildingBlocks"
kernel = rf"{BB}\TillKnowIT.SharedKernel\TillKnowIT.SharedKernel.csproj"
dispatching = rf"{BB}\TillKnowIT.Dispatching\TillKnowIT.Dispatching.csproj"
outbox = rf"{BB}\TillKnowIT.Outbox\TillKnowIT.Outbox.csproj"

write(f"{MOD}/{NAME}.Contracts/{NAME}.Contracts.csproj", csproj([kernel]))
write(f"{MOD}/{NAME}.Domain/{NAME}.Domain.csproj", csproj([kernel, rf"..\{NAME}.Contracts\{NAME}.Contracts.csproj"]))
write(f"{MOD}/{NAME}.Application/{NAME}.Application.csproj", csproj(
    [rf"..\{NAME}.Domain\{NAME}.Domain.csproj", rf"..\{NAME}.Contracts\{NAME}.Contracts.csproj", dispatching],
    visible_to=f"{NAME}.Application.Tests"))
write(f"{MOD}/{NAME}.Infrastructure/{NAME}.Infrastructure.csproj", csproj(
    [rf"..\{NAME}.Application\{NAME}.Application.csproj", outbox],
    packages=[("EFCore.NamingConventions", "10.0.1"), ("Microsoft.EntityFrameworkCore.SqlServer", "10.0.14"),
              ("System.Security.Cryptography.Pkcs", "10.0.14")],
    framework=True, visible_to=f"{NAME}.Application.Tests"))

write(f"{MOD}/{NAME}.Application/{NAME}ApplicationMarker.cs",
      f"namespace {NAME}.Application;\n\n/// <summary>Lets the module registration find this assembly's handlers.</summary>\npublic sealed class {NAME}ApplicationMarker;\n")

write(f"{MOD}/{NAME}.Infrastructure/Persistence/{NAME}DbContext.cs", f"""using Microsoft.EntityFrameworkCore;
using TillKnowIT.Outbox;

namespace {NAME}.Infrastructure.Persistence;

public sealed class {NAME}DbContext(DbContextOptions<{NAME}DbContext> options) : DbContext(options)
{{
    public const string Schema = "{SCHEMA}";

    protected override void OnModelCreating(ModelBuilder b)
    {{
        b.HasDefaultSchema(Schema);
        b.ApplyConfigurationsFromAssembly(typeof({NAME}DbContext).Assembly);
        b.AddOutbox();
    }}
}}
""")

write(f"{MOD}/{NAME}.Infrastructure/{NAME}Module.cs", f"""using {NAME}.Application;
using {NAME}.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace {NAME}.Infrastructure;

public static partial class {NAME}Module
{{
    public static IServiceCollection Add{NAME}Module(this IServiceCollection services, string connectionString)
    {{
        services.AddDbContext<{NAME}DbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", {NAME}DbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, {NAME}Migrator>();
        services.AddDispatching(typeof({NAME}ApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }}

    // Stores and other services of this module are registered in {NAME}Services.cs.
    static partial void AddServices(IServiceCollection services);
}}

internal sealed class {NAME}Migrator({NAME}DbContext db) : IModuleMigrator
{{
    public string Module => "{NAME}";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class {NAME}DesignTimeFactory : IDesignTimeDbContextFactory<{NAME}DbContext>
{{
    public {NAME}DbContext CreateDbContext(string[] args)
    {{
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<{NAME}DbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", {NAME}DbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new {NAME}DbContext(options);
    }}
}}
""")

write(f"{MOD}/{NAME}.Infrastructure/{NAME}Services.cs", f"""using Microsoft.Extensions.DependencyInjection;

namespace {NAME}.Infrastructure;

public static partial class {NAME}Module
{{
    static partial void AddServices(IServiceCollection services)
    {{
        // Register this module's stores here.
    }}
}}
""")

write(f"{MOD}/{NAME}.Infrastructure/{NAME}Endpoints.cs", f"""using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace {NAME}.Infrastructure;

public static class {NAME}Endpoints
{{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder Map{NAME}Endpoints(this IEndpointRouteBuilder api)
    {{
        return api;
    }}
}}
""")

# test project
TEST = os.path.join(ROOT, "tests", f"{NAME}.Application.Tests")
write(f"{TEST}/{NAME}.Application.Tests.csproj", f"""<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.11.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Modules\{NAME}\{NAME}.Application\{NAME}.Application.csproj" />
    <ProjectReference Include="..\..\src\Modules\{NAME}\{NAME}.Domain\{NAME}.Domain.csproj" />
    <ProjectReference Include="..\..\src\Modules\{NAME}\{NAME}.Contracts\{NAME}.Contracts.csproj" />
  </ItemGroup>

</Project>
""")

# solution
def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)


for layer in ("Contracts", "Domain", "Application", "Infrastructure"):
    run("dotnet", "sln", "add", f"src/Modules/{NAME}/{NAME}.{layer}/{NAME}.{layer}.csproj", "--solution-folder", NAME)
run("dotnet", "sln", "add", f"tests/{NAME}.Application.Tests/{NAME}.Application.Tests.csproj", "--solution-folder", "tests")
run("dotnet", "add", "src/Host/TillKnowIT.Api/TillKnowIT.Api.csproj", "reference", f"src/Modules/{NAME}/{NAME}.Infrastructure/{NAME}.Infrastructure.csproj")
run("dotnet", "add", "src/Host/TillKnowIT.Worker/TillKnowIT.Worker.csproj", "reference", f"src/Modules/{NAME}/{NAME}.Infrastructure/{NAME}.Infrastructure.csproj")
run("dotnet", "add", "tests/TillKnowIT.Architecture.Tests/TillKnowIT.Architecture.Tests.csproj", "reference", f"src/Modules/{NAME}/{NAME}.Infrastructure/{NAME}.Infrastructure.csproj")


def edit(path, fn):
    p = os.path.join(ROOT, path)
    s = open(p, encoding="utf-8").read()
    t = fn(s)
    if t == s:
        sys.exit(f"could not wire {NAME} into {path}")
    open(p, "w", encoding="utf-8", newline="\n").write(t)


# API host
def api(s):
    s = f"using {NAME}.Infrastructure;\n" + s
    s = re.sub(r"(\.AddMessagingModule\(db\))(;?)", lambda m: f"{m.group(1)}\n    .Add{NAME}Module(db)" + m.group(2), s, count=1) \
        if ".AddMessagingModule(db)" in s else s
    s = s.replace("api.MapCatalogueEndpoints();", f"api.MapCatalogueEndpoints();\napi.Map{NAME}Endpoints();", 1)
    return s


edit("src/Host/TillKnowIT.Api/Program.cs", api)


# Worker host: after the last .AddXModule(db) line and the last .AddOutboxProcessor line
def worker(s):
    s = f"using {NAME}.Infrastructure;\nusing {NAME}.Infrastructure.Persistence;\n" + s
    s = s.replace(".AddMessagingModule(db);", f".AddMessagingModule(db)\n    .Add{NAME}Module(db);", 1) if ".AddMessagingModule(db);" in s \
        else re.sub(r"(\.Add\w+Module\(db\))(;)\s*\n", lambda m: f"{m.group(1)}\n    .Add{NAME}Module(db);\n", s, count=1)
    s = s.replace(".AddOutboxProcessor<MessagingDbContext>();", f".AddOutboxProcessor<MessagingDbContext>()\n    .AddOutboxProcessor<{NAME}DbContext>();", 1) \
        if ".AddOutboxProcessor<MessagingDbContext>();" in s \
        else re.sub(r"(\.AddOutboxProcessor<\w+>\(\))(;)", lambda m: f"{m.group(1)}\n    .AddOutboxProcessor<{NAME}DbContext>();", s, count=1)
    return s


edit("src/Host/TillKnowIT.Worker/Program.cs", worker)
edit("tests/TillKnowIT.Architecture.Tests/ModuleRulesTests.cs", lambda s: s.replace('"Identity"];', f'"Identity", "{NAME}"];', 1))
edit("scripts/check-migrations.sh", lambda s: s.replace("Messaging Identity; do", f"Messaging Identity {NAME}; do", 1))
print(f"Module {NAME} scaffolded. Next: add entities, configurations, handlers, then run scripts/new_migration.sh {NAME}")
