namespace App.Tests;

public class QualityTests
{
    [Fact(Skip = "Pending")]
    [Requirement("QR-1", "Stored files, search and assessments are separated by engagement. A test must show that a search in one engagement cannot return another engagement's content.")]
    public void Search_NeverReturnsAnotherEngagementsContent()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-2", "All data is encrypted in transit and at rest.")]
    public void Data_IsEncryptedInTransitAndAtRest()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-3", "The backend holds no long-lived credentials. It reaches AWS services through its assigned role.")]
    public void Backend_HoldsNoLongLivedCredentials()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-4", "Every model call goes through Amazon Bedrock in the firm's own AWS account.")]
    public void ModelCalls_GoThroughBedrock()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-5", "A message may be delivered more than once without causing duplicate or incorrect data.")]
    public void RepeatedMessage_CausesNoDuplicates()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-6", "A message that cannot be processed is moved to a dead-letter queue and raises an alert.")]
    public void UnprocessableMessage_GoesToDeadLetterQueue()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-7", "Every verdict can be traced to its run, model, prompt version, assessment criteria version, and cited passages.")]
    public void Verdict_IsTraceable()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-8", "Each request and background job writes logs that share one correlation ID.")]
    public void Logs_ShareCorrelationId()
    {
    }

    [Fact(Skip = "Pending")]
    [Requirement("QR-9", "Targets will be set after the first working version is measured.")]
    public void Performance_TargetsPending()
    {
    }
}
