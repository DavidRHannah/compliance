using Amazon.CDK.Assertions;
using App.Tests;

namespace Infra.Tests;

[Collection(InfraCollection.Name)]
public class InfraTests(InfraFixture infra)
{
    private static Dictionary<string, object> Obj(params (string Key, object Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static int Count(Template template, string type, Dictionary<string, object> properties) =>
        template.FindResources(type, Obj(("Properties", Match.ObjectLike(properties)))).Count;

    [Fact]
    [Requirement("QR-6", "A message that cannot be processed is moved to a dead-letter queue and raises an alert.")]
    public void DeadLetterQueues_RaiseAnAlert()
    {
        // One alarm per dead-letter queue (assessments and documents), sent to the alarm topic.
        var alarms = Count(infra.Platform, "AWS::CloudWatch::Alarm", Obj(
            ("Namespace", "AWS/SQS"),
            ("MetricName", "ApproximateNumberOfMessagesVisible"),
            ("ComparisonOperator", "GreaterThanThreshold"),
            ("Threshold", 0),
            ("AlarmActions", Match.AnyValue())));
        Assert.Equal(2, alarms);

        // Each live queue gives up after three receives, the limit the consumer uses.
        Assert.Equal(2, Count(infra.Platform, "AWS::SQS::Queue", Obj(
            ("RedrivePolicy", Match.ObjectLike(Obj(("maxReceiveCount", 3)))))));
    }

    [Fact]
    [Requirement("AC-2", "Every user must sign in with multi-factor authentication.")]
    public void UserPool_RequiresMfa()
    {
        infra.Platform.HasResourceProperties("AWS::Cognito::UserPool", Obj(
            ("MfaConfiguration", "ON"),
            ("EnabledMfas", new[] { "SOFTWARE_TOKEN_MFA" })));
    }

    [Fact]
    [Requirement("AC-3", "Users are created by invitation. There is no self-registration.")]
    public void UserPool_HasNoSelfRegistration()
    {
        infra.Platform.HasResourceProperties("AWS::Cognito::UserPool", Obj(
            ("AdminCreateUserConfig", Obj(("AllowAdminCreateUserOnly", true)))));
    }

    [Fact]
    [Requirement("QR-2", "All data is encrypted in transit and at rest.")]
    public void Data_IsEncryptedInTransitAndAtRest()
    {
        infra.Platform.HasResourceProperties("AWS::RDS::DBCluster", Obj(("StorageEncrypted", true)));

        foreach (var template in new[] { infra.Platform, infra.Services })
        {
            // Every bucket is encrypted and its policy refuses plain HTTP.
            var buckets = template.FindResources("AWS::S3::Bucket").Count;
            Assert.Equal(buckets, Count(template, "AWS::S3::Bucket", Obj(("BucketEncryption", Match.AnyValue()))));
            Assert.Equal(buckets, template.FindResources("AWS::S3::BucketPolicy", Obj(("Properties", Match.ObjectLike(Obj(
                ("PolicyDocument", Obj(("Statement", Match.ArrayWith(
                [
                    Match.ObjectLike(Obj(
                        ("Effect", "Deny"),
                        ("Condition", Obj(("Bool", Obj(("aws:SecureTransport", "false")))))))
                ]))))))))).Count);
        }

        // Every queue uses the customer-managed key.
        Assert.Equal(4, Count(infra.Platform, "AWS::SQS::Queue", Obj(("KmsMasterKeyId", Match.AnyValue()))));
    }

    [Fact]
    [Requirement("QR-3", "The backend holds no long-lived credentials. It reaches AWS services through its assigned role.")]
    public void Backend_HoldsNoLongLivedCredentials()
    {
        foreach (var template in new[] { infra.Platform, infra.Services })
        {
            template.ResourceCountIs("AWS::IAM::User", 0);
            template.ResourceCountIs("AWS::IAM::AccessKey", 0);
        }

        // The API and the worker each run under a task role.
        var taskDefinitions = infra.Services.FindResources("AWS::ECS::TaskDefinition").Count;
        Assert.Equal(2, taskDefinitions);
        Assert.Equal(taskDefinitions, Count(infra.Services, "AWS::ECS::TaskDefinition", Obj(("TaskRoleArn", Match.AnyValue()))));
    }

    [Fact]
    public void OnlyTheWorkerCanCallBedrock()
    {
        var policies = Count(infra.Services, "AWS::IAM::Policy", Obj(
            ("PolicyDocument", Obj(("Statement", Match.ArrayWith(
            [
                Match.ObjectLike(Obj(("Action", "bedrock:InvokeModel")))
            ]))))));
        Assert.Equal(1, policies);
        Assert.Equal(0, Count(infra.Platform, "AWS::IAM::Policy", Obj(
            ("PolicyDocument", Obj(("Statement", Match.ArrayWith(
            [
                Match.ObjectLike(Obj(("Action", "bedrock:InvokeModel")))
            ])))))));
    }

    [Fact]
    public void LoadBalancerIsInternal_AndThereIsNoNatGateway()
    {
        infra.Services.HasResourceProperties("AWS::ElasticLoadBalancingV2::LoadBalancer", Obj(("Scheme", "internal")));
        infra.Platform.ResourceCountIs("AWS::EC2::NatGateway", 0);
    }
}
