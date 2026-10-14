using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Data.Configurations;

public class EngagementConfiguration : IEntityTypeConfiguration<Engagement>
{
    public void Configure(EntityTypeBuilder<Engagement> e)
    {
        e.Property(x => x.Status).HasDefaultValue(EngagementStatus.Draft);
        e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.LeadConsultantId);
    }
}

public class EngagementFrameworkConfiguration : IEntityTypeConfiguration<EngagementFramework>
{
    public void Configure(EntityTypeBuilder<EngagementFramework> e)
    {
        e.HasKey(x => new { x.EngagementId, x.FrameworkId });
        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<Framework>().WithMany().HasForeignKey(x => x.FrameworkId);
    }
}

public class EngagementControlConfiguration : IEntityTypeConfiguration<EngagementControl>
{
    public void Configure(EntityTypeBuilder<EngagementControl> e)
    {
        e.HasKey(x => new { x.EngagementId, x.ControlId });

        // Sentinel true: EF sends false, and leaves true to the database default.
        e.Property(x => x.IsApplicable).HasDefaultValue(true).HasSentinel(true);

        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<EngagementFramework>().WithMany()
            .HasForeignKey(x => new { x.EngagementId, x.FrameworkId });
        e.HasOne<Control>().WithMany()
            .HasForeignKey(x => new { x.ControlId, x.FrameworkId })
            .HasPrincipalKey(c => new { c.Id, c.FrameworkId });
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedById);

        e.ToTable(t => t.HasCheckConstraint("ck_engagement_controls_justification",
            "is_applicable OR justification IS NOT NULL"));
    }
}

public class ScopeItemConfiguration : IEntityTypeConfiguration<ScopeItem>
{
    public void Configure(EntityTypeBuilder<ScopeItem> e)
    {
        e.HasAlternateKey(x => new { x.Id, x.EngagementId });
        e.HasIndex(x => new { x.EngagementId, x.Type, x.Name }).IsUnique();
        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById);
    }
}
