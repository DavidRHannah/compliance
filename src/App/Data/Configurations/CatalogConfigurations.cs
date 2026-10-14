using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Data.Configurations;

public class FrameworkConfiguration : IEntityTypeConfiguration<Framework>
{
    public void Configure(EntityTypeBuilder<Framework> e)
    {
        e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
    }
}

public class ControlConfiguration : IEntityTypeConfiguration<Control>
{
    public void Configure(EntityTypeBuilder<Control> e)
    {
        e.HasAlternateKey(x => new { x.Id, x.FrameworkId });
        e.HasIndex(x => new { x.FrameworkId, x.RefCode }).IsUnique();
        e.HasOne<Framework>().WithMany().HasForeignKey(x => x.FrameworkId);
        e.ToTable(t => t.HasCheckConstraint("ck_controls_theme",
            "theme IN ('organizational', 'people', 'physical', 'technological')"));
    }
}

public class AssessmentCriterionConfiguration : IEntityTypeConfiguration<AssessmentCriterion>
{
    public void Configure(EntityTypeBuilder<AssessmentCriterion> e)
    {
        e.ToTable("assessment_criteria");
        e.HasAlternateKey(x => new { x.ControlId, x.Id });
        e.HasIndex(x => new { x.ControlId, x.Version }).IsUnique();

        // One active version per control.
        e.HasIndex(x => x.ControlId).IsUnique().HasFilter("is_active")
            .HasDatabaseName("ix_assessment_criteria_one_active");

        e.Property(x => x.RequiresOperationEvidence).HasDefaultValue(false);
        e.Property(x => x.IsActive).HasDefaultValue(false);
        e.HasOne<Control>().WithMany().HasForeignKey(x => x.ControlId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById);
    }
}

public class ControlMappingConfiguration : IEntityTypeConfiguration<ControlMapping>
{
    public void Configure(EntityTypeBuilder<ControlMapping> e)
    {
        e.HasIndex(x => new { x.SourceControlId, x.TargetControlId }).IsUnique();
        e.HasOne<Control>().WithMany().HasForeignKey(x => x.SourceControlId);
        e.HasOne<Control>().WithMany().HasForeignKey(x => x.TargetControlId);
        e.ToTable(t => t.HasCheckConstraint("ck_control_mappings_not_self",
            "source_control_id <> target_control_id"));
    }
}
