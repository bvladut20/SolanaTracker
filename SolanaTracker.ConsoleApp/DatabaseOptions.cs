namespace SolanaTracker.ConsoleApp;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "";
    public bool CheckConnection { get; set; }
    public bool ApplyMigrationsOnStartup { get; set; }
}
