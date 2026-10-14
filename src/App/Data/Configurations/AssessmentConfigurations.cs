using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Data.Configurations;

public class AssessmentRunConfiguration : IEntityTypeConfiguration<AssessmentRun>
{
    public void Configure(EntityTypeBuilder<AssessmentRun> e)
    {
        e.HasAlternateKey(x => new { x.Id, x.EngagementId });
        e.HasIndex(x => new { x.EngagementId, x.CreatedAt });

        // One active run per engagement (AS-14).
        e.HasIndex(x => x.EngagementId).IsUnique().HasFilter("status IN ('queued', 'running')")
            .HasDatabaseName("ix_assessment_runs_one_active");

        e.Property(x => x.Status).HasDefaultValue(RunStatus.Queued);
        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.TriggeredById);
    }
}

public class AssessmentRunControlConfiguration : IEntityTypeConfiguration<AssessmentRunControl>
{
    public void Configure(EntityTypeBuilder<AssessmentRunControl> e)
    {
        e.HasKey(x => new { x.RunId, x.ControlId });
        e.HasOne<AssessmentRun>().WithMany().HasForeignKey(x => x.RunId);
        e.HasOne<Control>().WithMany().HasForeignKey(x => x.ControlId);
        e.HasOne<AssessmentCriterion>().WithMany()
            .HasForeignKey(x => new { x.ControlId, x.AssessmentCriteriaId })
            .HasPrincipalKey(c => new { c.ControlId, c.Id });
        e.ToTable(t => t.HasCheckConstraint("ck_assessment_run_controls_failed_has_error",
            "(failed_at IS NULL) = (error IS NULL)"));
    }
}

public class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> e)
    {
        e.HasAlternateKey(x => new { x.Id, x.EngagementId });
        e.HasIndex(x => new { x.RunId, x.ControlId }).IsUnique();

        // One current assessment per engagement and control. An empty scope item counts as equal.
        e.HasIndex(x => new { x.EngagementId, x.ControlId, x.ScopeItemId }).IsUnique()
            .AreNullsDistinct(false).HasFilter("is_current")
            .HasDatabaseName("ix_assessments_one_current");

        // Sentinel true: EF sends false, and leaves true to the database default.
        e.Property(x => x.IsCurrent).HasDefaultValue(true).HasSentinel(true);

        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<Control>().WithMany().HasForeignKey(x => x.ControlId);
        e.HasOne<AssessmentRunControl>().WithMany()
            .HasForeignKey(x => new { x.RunId, x.ControlId });
        e.HasOne<AssessmentRun>().WithMany()
            .HasForeignKey(x => new { x.RunId, x.EngagementId })
            .HasPrincipalKey(r => new { r.Id, r.EngagementId });
        e.HasOne<ScopeItem>().WithMany()
            .HasForeignKey(x => new { x.ScopeItemId, x.EngagementId })
            .HasPrincipalKey(s => new { s.Id, s.EngagementId });
    }
}

public class EvidenceCitationConfiguration : IEntityTypeConfiguration<EvidenceCitation>
{
    public void Configure(EntityTypeBuilder<EvidenceCitation> e)
    {
        // The indexes on (assessment_id, engagement_id) and (chunk_id, engagement_id) for the
        // foreign keys also serve lookups by assessment and by chunk.
        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<Assessment>().WithMany()
            .HasForeignKey(x => new { x.AssessmentId, x.EngagementId })
            .HasPrincipalKey(a => new { a.Id, a.EngagementId });
        e.HasOne<DocumentChunk>().WithMany()
            .HasForeignKey(x => new { x.ChunkId, x.EngagementId })
            .HasPrincipalKey(c => new { c.Id, c.EngagementId });

        e.ToTable(t => t.HasCheckConstraint("ck_evidence_citations_verified_range",
            "(is_verified AND char_start IS NOT NULL AND char_end > char_start) " +
            "OR (NOT is_verified AND char_start IS NULL AND char_end IS NULL)"));
    }
}

public class ReviewFlagConfiguration : IEntityTypeConfiguration<ReviewFlag>
{
    public void Configure(EntityTypeBuilder<ReviewFlag> e)
    {
        // One open flag per assessment, reason and document.
        e.HasIndex(x => new { x.AssessmentId, x.Reason, x.DocumentId }).IsUnique()
            .AreNullsDistinct(false).HasFilter("cleared_at IS NULL")
            .HasDatabaseName("ix_review_flags_one_open");
        e.HasIndex(x => x.AssessmentId).HasFilter("cleared_at IS NULL")
            .HasDatabaseName("ix_review_flags_open_by_assessment");
        e.HasIndex(x => x.EngagementId).HasFilter("cleared_at IS NULL")
            .HasDatabaseName("ix_review_flags_open_by_engagement");

        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<Assessment>().WithMany()
            .HasForeignKey(x => new { x.AssessmentId, x.EngagementId })
            .HasPrincipalKey(a => new { a.Id, a.EngagementId });
        e.HasOne<Document>().WithMany()
            .HasForeignKey(x => new { x.DocumentId, x.EngagementId })
            .HasPrincipalKey(d => new { d.Id, d.EngagementId });
        e.HasOne<Assessment>().WithMany()
            .HasForeignKey(x => new { x.SupersededAssessmentId, x.EngagementId })
            .HasPrincipalKey(a => new { a.Id, a.EngagementId });
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ClearedById);

        e.ToTable(t =>
        {
            t.HasCheckConstraint("ck_review_flags_document_reason",
                "(reason IN ('document_deleted', 'document_replaced')) = (document_id IS NOT NULL)");
            t.HasCheckConstraint("ck_review_flags_superseded_reason",
                "(reason = 'decision_superseded') = (superseded_assessment_id IS NOT NULL)");
            t.HasCheckConstraint("ck_review_flags_cleared_by",
                "(cleared_at IS NULL) = (cleared_by_id IS NULL)");
        });
    }
}

public class ConsultantDecisionConfiguration : IEntityTypeConfiguration<ConsultantDecision>
{
    public void Configure(EntityTypeBuilder<ConsultantDecision> e)
    {
        // Finds the decision of record: the latest by (decided_at, id).
        e.HasIndex(x => new { x.AssessmentId, x.DecidedAt, x.Id }).IsDescending(false, true, true);

        e.Property(x => x.DecidedAt).HasDefaultValueSql("now()");
        e.HasOne<Assessment>().WithMany().HasForeignKey(x => x.AssessmentId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedById);
    }
}

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> e)
    {
        e.HasIndex(x => new { x.EngagementId, x.OccurredAt });
        e.HasIndex(x => new { x.EntityType, x.EntityId });

        e.Property(x => x.OccurredAt).HasDefaultValueSql("now()");
        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId);
    }
}
