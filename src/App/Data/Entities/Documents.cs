using System.Text.Json;
using NpgsqlTypes;
using Pgvector;

namespace App.Data.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid EngagementId { get; set; }
    public Guid UploadedById { get; set; }
    public required string FileName { get; set; }
    public required string MimeType { get; set; }
    public long SizeBytes { get; set; }
    public int? PageCount { get; set; }

    /// <summary>SHA-256 of the original file; set by the check step.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Chosen by the server from the document ID.</summary>
    public required string S3Key { get; set; }

    /// <summary>Recorded at confirm; the worker and downloads use only this version.</summary>
    public string? S3VersionId { get; set; }

    /// <summary>Extracted text and locations as JSON.</summary>
    public string? ExtractedS3Key { get; set; }

    /// <summary>embedded or text_recognition.</summary>
    public string? TextSource { get; set; }

    public Guid? ReplacesDocumentId { get; set; }

    /// <summary>Soft delete. A trigger copies it to the document's chunks.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public DocumentStatus Status { get; set; }

    /// <summary>Last completed step, for retries.</summary>
    public string? ProcessingStep { get; set; }

    /// <summary>Set only when failed.</summary>
    public string? Error { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>Denormalized for filtered search.</summary>
    public Guid EngagementId { get; set; }

    public int ChunkIndex { get; set; }

    /// <summary>Page, sheet and range, or slide.</summary>
    public required JsonDocument Locator { get; set; }

    public string? SectionTitle { get; set; }
    public required string Content { get; set; }
    public int TokenCount { get; set; }
    public Vector? Embedding { get; set; }
    public string? EmbeddingModel { get; set; }

    /// <summary>For example xlsx-rows-v2.</summary>
    public required string Chunker { get; set; }

    /// <summary>Set by re-chunking; search excludes.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>Copied from the document by triggers; search excludes.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Generated from Content by the database.</summary>
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}

public class ChunkScopeMention
{
    public Guid ChunkId { get; set; }
    public Guid ScopeItemId { get; set; }
    public Guid EngagementId { get; set; }
    public required string MatchedText { get; set; }
    public MentionMethod Method { get; set; }
}
