namespace App.Data.Entities;

/// <summary>The isolation boundary. Every engagement-scoped table carries its ID.</summary>
public class Engagement
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid LeadConsultantId { get; set; }
    public required string Name { get; set; }
    public EngagementStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class EngagementFramework
{
    public Guid EngagementId { get; set; }
    public Guid FrameworkId { get; set; }
}

/// <summary>One row per control of each framework in the engagement.</summary>
public class EngagementControl
{
    public Guid EngagementId { get; set; }
    public Guid ControlId { get; set; }
    public Guid FrameworkId { get; set; }
    public bool IsApplicable { get; set; } = true;

    /// <summary>Required when not applicable.</summary>
    public string? Justification { get; set; }

    public Guid? UpdatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>A system, asset or vendor the engagement covers. Hard-deleted.</summary>
public class ScopeItem
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public ScopeItemType Type { get; set; }
    public required string Name { get; set; }

    /// <summary>Alternate names used for entity matching.</summary>
    public List<string> Aliases { get; set; } = [];

    public string? Description { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
