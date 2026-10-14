namespace App.Tests;

public class DocumentTests
{
    [Fact(Skip = "Pending")]
    [Requirement("DO-1", "A user uploads a file directly to storage through a time-limited upload link. The backend does not receive the file's contents in the request.")]
    public void Upload_GoesDirectlyToStorage()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-2", "The backend accepts PDF, DOCX, XLSX, CSV and PPTX files, and rejects all other types and any file over 50 MB. The size limit is configurable.")]
    public void Upload_RejectsWrongTypeOrOversize()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-3", "A file identical to one in the engagement that is not deleted is not processed again. It ends as failed, with the reason duplicate.")]
    public void DuplicateFile_FailsAsDuplicate()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-4", "A stored file is never modified. Replacing a document stores a new file.")]
    public void StoredFile_IsNeverModified()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-5", "Each document has a status: pending, processing, ready or failed. A failed document shows the reason.")]
    public void Document_HasStatusAndFailureReason()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-6", "An authorized user can download an original file through a time-limited link.")]
    public void Download_UsesTimeLimitedLink()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-7", "A user who may upload documents may delete a document or replace it with a new version.")]
    public void Uploader_CanDeleteOrReplace()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-8", "A deleted or replaced document is excluded from search and from later runs.")]
    public void DeletedDocument_IsExcludedFromSearchAndRuns()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("DO-9", "A file cannot be changed after its upload is confirmed. Processing and downloads always use the confirmed version.")]
    public void ConfirmedVersion_IsUsedForProcessingAndDownload()
    {
    }
}
