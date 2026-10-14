using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Engagement> Engagements => Set<Engagement>();
    public DbSet<EngagementFramework> EngagementFrameworks => Set<EngagementFramework>();
    public DbSet<EngagementControl> EngagementControls => Set<EngagementControl>();
    public DbSet<ScopeItem> ScopeItems => Set<ScopeItem>();
    public DbSet<Framework> Frameworks => Set<Framework>();
    public DbSet<Control> Controls => Set<Control>();
    public DbSet<AssessmentCriterion> AssessmentCriteria => Set<AssessmentCriterion>();
    public DbSet<ControlMapping> ControlMappings => Set<ControlMapping>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ChunkScopeMention> ChunkScopeMentions => Set<ChunkScopeMention>();
    public DbSet<AssessmentRun> AssessmentRuns => Set<AssessmentRun>();
    public DbSet<AssessmentRunControl> AssessmentRunControls => Set<AssessmentRunControl>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<EvidenceCitation> EvidenceCitations => Set<EvidenceCitation>();
    public DbSet<ReviewFlag> ReviewFlags => Set<ReviewFlag>();
    public DbSet<ConsultantDecision> ConsultantDecisions => Set<ConsultantDecision>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>
    /// Provider settings shared by the application, the migrate task and the tests.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder options, string? connectionString)
    {
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.MapEnum<UserRole>("user_role");
                npgsql.MapEnum<EngagementStatus>("engagement_status");
                npgsql.MapEnum<ScopeItemType>("scope_item_type");
                npgsql.MapEnum<DocumentStatus>("document_status");
                npgsql.MapEnum<MappingRelationship>("mapping_relationship");
                npgsql.MapEnum<MentionMethod>("mention_method");
                npgsql.MapEnum<RunStatus>("run_status");
                npgsql.MapEnum<PreliminaryVerdict>("preliminary_verdict");
                npgsql.MapEnum<FinalVerdict>("final_verdict");
                npgsql.MapEnum<ReviewFlagReason>("review_flag_reason");
            })
            .UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Append-only history must never disappear through a cascade. The one cascade in the
        // schema, scope item to its mentions, is set in ChunkScopeMentionConfiguration.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            var isScopeMention = fk.DeclaringEntityType.ClrType == typeof(ChunkScopeMention)
                && fk.PrincipalEntityType.ClrType == typeof(ScopeItem);
            if (!isScopeMention)
            {
                fk.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
