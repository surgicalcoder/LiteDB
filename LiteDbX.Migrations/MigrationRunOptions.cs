namespace LiteDbX.Migrations;

public sealed class MigrationRunOptions
{
    public bool DryRun { get; set; }

    public BackupRetentionPolicy BackupRetentionPolicy { get; set; } = BackupRetentionPolicy.KeepAll;

    internal MigrationRunOptions Clone()
    {
        return new MigrationRunOptions
        {
            DryRun = DryRun,
            BackupRetentionPolicy = BackupRetentionPolicy
        };
    }
}

public enum BackupRetentionPolicy
{
    KeepAll,
    DeleteOnSuccess
}

public enum BackupDisposition
{
    None,
    Planned,
    Kept,
    Deleted
}

