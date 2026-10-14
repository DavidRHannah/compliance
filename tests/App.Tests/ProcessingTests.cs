namespace App.Tests;

public class ProcessingTests
{
    [Fact(Skip = "Pending")]
    [Requirement("PR-1", "Each uploaded document is processed in the background. The upload request does not wait for it.")]
    public void Processing_RunsInBackground()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-2", "Processing extracts the text, splits it into chunks, indexes the chunks for search, and links chunks to the scope items they mention.")]
    public void Processing_ExtractsChunksIndexesAndLinks()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-3", "Each chunk records its location in the source: page for PDF, heading and paragraph for DOCX, sheet and cell range for XLSX and CSV, slide for PPTX.")]
    public void Chunk_RecordsSourceLocation()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-4", "Any processing step can be repeated without creating duplicate data.")]
    public void ProcessingStep_IsRepeatableWithoutDuplicates()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-5", "A document that fails a step is retried from that step. After three failed attempts it is marked failed.")]
    public void FailedStep_IsRetriedThenFailsAfterThree()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-6", "A scanned PDF is read with text recognition, using Amazon Textract. A document that still yields no text is marked failed with that reason.")]
    public void ScannedPdf_UsesTextRecognition()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-7", "Every uploaded document ends as either ready or failed. None is left in an intermediate state.")]
    public void Document_EndsReadyOrFailed()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("PR-8", "An upload that is never confirmed ends as failed after 30 minutes.")]
    public void UnconfirmedUpload_FailsAfter30Minutes()
    {
    }
}
