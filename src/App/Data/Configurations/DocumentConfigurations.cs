using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    // A live document is one that is not deleted and has not failed.
    private const string Live = "deleted_at IS NULL AND status <> 'failed'";

    public void Configure(EntityTypeBuilder<Document> e)
    {
        e.HasAlternateKey(x => new { x.Id, x.EngagementId });

        // The naming convention would write s3key.
        e.Property(x => x.S3Key).HasColumnName("s3_key");
        e.Property(x => x.S3VersionId).HasColumnName("s3_version_id");
        e.Property(x => x.ExtractedS3Key).HasColumnName("extracted_s3_key");

        // One live copy of a file per engagement (DO-3).
        e.HasIndex(x => new { x.EngagementId, x.ContentHash }).IsUnique().HasFilter(Live)
            .HasDatabaseName("ix_documents_one_live_copy");

        // A document is replaced by at most one live document.
        e.HasIndex(x => x.ReplacesDocumentId).IsUnique().HasFilter(Live)
            .HasDatabaseName("ix_documents_one_live_replacement");

        e.HasIndex(x => new { x.EngagementId, x.Status });
        e.Property(x => x.Status).HasDefaultValue(DocumentStatus.Pending);

        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedById);
        e.HasOne<Document>().WithMany()
            .HasForeignKey(x => new { x.ReplacesDocumentId, x.EngagementId })
            .HasPrincipalKey(d => new { d.Id, d.EngagementId });

        e.ToTable(t =>
        {
            t.HasCheckConstraint("ck_documents_not_self_replacing", "replaces_document_id <> id");
            t.HasCheckConstraint("ck_documents_text_source", "text_source IN ('embedded', 'text_recognition')");
            t.HasCheckConstraint("ck_documents_failed_has_error", "(status = 'failed') = (error IS NOT NULL)");
        });
    }
}

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> e)
    {
        e.HasAlternateKey(x => new { x.Id, x.EngagementId });

        // One live chunk per position; re-chunking supersedes the old ones.
        e.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique().HasFilter("superseded_at IS NULL")
            .HasDatabaseName("ix_document_chunks_one_live_position");

        e.Property(x => x.Embedding).HasColumnType("vector(1024)");

        // Search queries must repeat this condition exactly, or the planner skips the index.
        e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops")
            .HasFilter("deleted_at IS NULL AND superseded_at IS NULL");

        e.HasGeneratedTsVectorColumn(x => x.SearchVector, "english", x => new { x.Content })
            .HasIndex(x => x.SearchVector).HasMethod("gin");

        // The index on (document_id, engagement_id) for the foreign key also serves lookups by document.
        e.HasIndex(x => x.EngagementId);

        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<Document>().WithMany()
            .HasForeignKey(x => new { x.DocumentId, x.EngagementId })
            .HasPrincipalKey(d => new { d.Id, d.EngagementId });
    }
}

public class ChunkScopeMentionConfiguration : IEntityTypeConfiguration<ChunkScopeMention>
{
    public void Configure(EntityTypeBuilder<ChunkScopeMention> e)
    {
        // The index on (scope_item_id, engagement_id) for the foreign key also serves lookups by scope item.
        e.HasKey(x => new { x.ChunkId, x.ScopeItemId });

        e.HasOne<Engagement>().WithMany().HasForeignKey(x => x.EngagementId);
        e.HasOne<DocumentChunk>().WithMany()
            .HasForeignKey(x => new { x.ChunkId, x.EngagementId })
            .HasPrincipalKey(c => new { c.Id, c.EngagementId });

        // The only cascade in the schema: deleting a scope item deletes its mentions.
        e.HasOne<ScopeItem>().WithMany()
            .HasForeignKey(x => new { x.ScopeItemId, x.EngagementId })
            .HasPrincipalKey(s => new { s.Id, s.EngagementId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
