namespace TillKnowIT.SharedKernel;

/// <summary>Each module registers one so the host can apply that module's EF migrations.</summary>
public interface IModuleMigrator
{
    string Module { get; }
    Task MigrateAsync(CancellationToken ct = default);
}
