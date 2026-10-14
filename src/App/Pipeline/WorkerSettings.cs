namespace App.Pipeline;

/// <summary>The Worker section of the configuration.</summary>
public sealed class WorkerSettings
{
    /// <summary>Turns the queue consumers and the sweeper on. The same image runs as API or worker.</summary>
    public bool Enabled { get; set; }

    /// <summary>How many controls are assessed at once.</summary>
    public int Concurrency { get; set; } = 4;

    public int SweepIntervalMinutes { get; set; } = 5;

    /// <summary>A run with no progress for this long is failed, so it can be resumed.</summary>
    public int StalledRunMinutes { get; set; } = 30;
}
