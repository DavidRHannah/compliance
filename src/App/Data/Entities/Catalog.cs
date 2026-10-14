namespace App.Data.Entities;

public class Framework
{
    public Guid Id { get; set; }

    /// <summary>For example iso_27001.</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    /// <summary>For example 2022.</summary>
    public required string Version { get; set; }
}

/// <summary>Loaded by the catalog import. Never deleted.</summary>
public class Control
{
    public Guid Id { get; set; }
    public Guid FrameworkId { get; set; }

    /// <summary>For example 5.1 or 8.24.</summary>
    public required string RefCode { get; set; }

    public required string Theme { get; set; }
    public required string Title { get; set; }

    /// <summary>The standard's own text. Licensed; never in the repository.</summary>
    public required string Statement { get; set; }
}

/// <summary>One version of the firm's criteria for a control. Insert-only apart from IsActive.</summary>
public class AssessmentCriterion
{
    public Guid Id { get; set; }
    public Guid ControlId { get; set; }
    public int Version { get; set; }

    /// <summary>One entry per kind of evidence; each is a search query.</summary>
    public List<string> ExpectedEvidence { get; set; } = [];

    public required string PassConditions { get; set; }
    public required string FailConditions { get; set; }
    public bool RequiresOperationEvidence { get; set; }
    public bool IsActive { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Empty in phase one.</summary>
public class ControlMapping
{
    public Guid Id { get; set; }
    public Guid SourceControlId { get; set; }
    public Guid TargetControlId { get; set; }
    public MappingRelationship Relationship { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? FetchedAt { get; set; }
}
