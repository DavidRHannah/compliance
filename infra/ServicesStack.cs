using Amazon.CDK;
using Amazon.CDK.AWS.ApplicationAutoScaling;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace Infra;

/// <summary>
/// The running application: API and worker services, the load balancer and CloudFront.
/// Deployed after the image is pushed and the migrate task has finished.
/// </summary>
public class ServicesStack : Stack
{
    public FargateService ApiService { get; }
    public FargateService WorkerService { get; }
    public ApplicationLoadBalancer LoadBalancer { get; }
    public Distribution Distribution { get; }
    public Bucket FrontendBucket { get; }

    public ServicesStack(Construct scope, string id, InfraSettings settings, PlatformStack platform, IStackProps props)
        : base(scope, id, props)
    {
        // API: behind an internal load balancer. Only CloudFront reaches it.
        var apiTask = platform.NewTaskDefinition(this, "Api", workerEnabled: false);
        GrantApi(platform, apiTask.TaskRole);

        ApiService = new FargateService(this, "ApiService", new FargateServiceProps
        {
            Cluster = platform.Cluster,
            TaskDefinition = apiTask,
            DesiredCount = 2,
            VpcSubnets = platform.TaskSubnets,
            SecurityGroups = [platform.TaskSecurityGroup],
            AssignPublicIp = false,
            MinHealthyPercent = 100,
            MaxHealthyPercent = 200,
            CircuitBreaker = new DeploymentCircuitBreaker { Rollback = true },
            HealthCheckGracePeriod = Duration.Seconds(60),
        });

        LoadBalancer = new ApplicationLoadBalancer(this, "LoadBalancer", new ApplicationLoadBalancerProps
        {
            Vpc = platform.Vpc,
            InternetFacing = false,
            VpcSubnets = platform.TaskSubnets,
            SecurityGroup = platform.LoadBalancerSecurityGroup,
        });
        // Internal only: the traffic stays inside the VPC, so the listener is plain HTTP.
        LoadBalancer.AddListener("Http", new BaseApplicationListenerProps { Port = 80, Open = false })
            .AddTargets("Api", new AddApplicationTargetsProps
            {
                Port = PlatformStack.ApiPort,
                Protocol = ApplicationProtocol.HTTP,
                Targets = [ApiService],
                DeregistrationDelay = Duration.Seconds(30),
                HealthCheck = new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
                {
                    Path = "/health",
                    HealthyHttpCodes = "200",
                    Interval = Duration.Seconds(30),
                },
            });

        ApiService.AutoScaleTaskCount(new EnableScalingProps { MinCapacity = 2, MaxCapacity = 4 })
            .ScaleOnCpuUtilization("Cpu", new CpuUtilizationScalingProps { TargetUtilizationPercent = 60 });

        // Worker: consumes the queues, calls Bedrock and Textract. Scales on queue depth.
        var workerTask = platform.NewTaskDefinition(this, "Worker", workerEnabled: true, cpu: 1024, memory: 2048);
        GrantWorker(platform, workerTask.TaskRole, settings);

        WorkerService = new FargateService(this, "WorkerService", new FargateServiceProps
        {
            Cluster = platform.Cluster,
            TaskDefinition = workerTask,
            DesiredCount = 1,
            VpcSubnets = platform.TaskSubnets,
            SecurityGroups = [platform.TaskSecurityGroup],
            AssignPublicIp = false,
            MinHealthyPercent = 100,
            MaxHealthyPercent = 200,
            CircuitBreaker = new DeploymentCircuitBreaker { Rollback = true },
        });
        WorkerService.AutoScaleTaskCount(new EnableScalingProps { MinCapacity = 1, MaxCapacity = 4 })
            .ScaleOnMetric("QueueDepth", new BasicStepScalingPolicyProps
            {
                Metric = platform.AssessmentQueue.MetricApproximateNumberOfMessagesVisible(
                    new MetricOptions { Period = Duration.Minutes(1) }),
                AdjustmentType = AdjustmentType.CHANGE_IN_CAPACITY,
                ScalingSteps =
                [
                    new ScalingInterval { Upper = 0, Change = -1 },
                    new ScalingInterval { Lower = 10, Change = 1 },
                    new ScalingInterval { Lower = 50, Change = 2 },
                ],
            });

        // CloudFront: the frontend files, and /api/* to the API.
        FrontendBucket = new Bucket(this, "FrontendBucket", new BucketProps
        {
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });
        Distribution = new Distribution(this, "Distribution", new DistributionProps
        {
            DefaultRootObject = "index.html",
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(FrontendBucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/api/*"] = new BehaviorOptions
                {
                    Origin = VpcOrigin_.WithApplicationLoadBalancer(
                        LoadBalancer,
                        new VpcOriginWithEndpointProps { ProtocolPolicy = OriginProtocolPolicy.HTTP_ONLY }),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                    AllowedMethods = AllowedMethods.ALLOW_ALL,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                },
            },
        });

        // ALB 5xx above 1% of requests.
        var errorRate = new MathExpression(new MathExpressionProps
        {
            Expression = "IF(requests > 0, 100 * errors / requests, 0)",
            UsingMetrics = new Dictionary<string, IMetric>
            {
                ["requests"] = LoadBalancer.Metrics.RequestCount(new MetricOptions { Statistic = "Sum" }),
                ["errors"] = LoadBalancer.Metrics.HttpCodeTarget(HttpCodeTarget.TARGET_5XX_COUNT, new MetricOptions { Statistic = "Sum" }),
            },
            Period = Duration.Minutes(5),
        });
        new Alarm(this, "Api5xxRate", new AlarmProps
        {
            AlarmDescription = "More than 1% of API requests failed with a 5xx.",
            Metric = errorRate,
            Threshold = 1,
            EvaluationPeriods = 2,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        }).AddAlarmAction(new SnsAction(platform.AlarmTopic));

        new CfnOutput(this, "SiteUrl", new CfnOutputProps { Value = $"https://{Distribution.DistributionDomainName}" });
        new CfnOutput(this, "FrontendBucketName", new CfnOutputProps { Value = FrontendBucket.BucketName });
    }

    private static void GrantApi(PlatformStack platform, IGrantable role)
    {
        platform.AssessmentQueue.GrantSendMessages(role);
        platform.DocumentQueue.GrantSendMessages(role);
        platform.DocumentsBucket.GrantPut(role);
        platform.DocumentsBucket.GrantRead(role);
        role.GrantPrincipal.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions =
            [
                "cognito-idp:AdminCreateUser", "cognito-idp:AdminGetUser", "cognito-idp:AdminDisableUser",
                "cognito-idp:AdminEnableUser", "cognito-idp:AdminUserGlobalSignOut", "cognito-idp:AdminDeleteUser",
            ],
            Resources = [platform.UserPool.UserPoolArn],
        }));
    }

    private void GrantWorker(PlatformStack platform, IGrantable role, InfraSettings settings)
    {
        platform.AssessmentQueue.GrantConsumeMessages(role);
        platform.AssessmentQueue.GrantSendMessages(role);
        platform.DocumentQueue.GrantConsumeMessages(role);
        platform.DocumentQueue.GrantSendMessages(role);
        platform.DocumentsBucket.GrantReadWrite(role);

        // A "us.xxx" model ID is an inference profile: the call needs the profile and the
        // foundation model it routes to, in whichever region it picks.
        var baseModelId = System.Text.RegularExpressions.Regex.Replace(settings.AssessmentModelId, @"^(us|eu|apac|global)\.", "");
        role.GrantPrincipal.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["bedrock:InvokeModel"],
            Resources =
            [
                $"arn:aws:bedrock:*::foundation-model/{baseModelId}",
                $"arn:aws:bedrock:{Region}:{Account}:inference-profile/{settings.AssessmentModelId}",
                $"arn:aws:bedrock:{Region}::foundation-model/{InfraSettings.EmbeddingModelId}",
            ],
        }));

        // Textract has no resource-level permissions.
        role.GrantPrincipal.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions =
            [
                "textract:StartDocumentTextDetection", "textract:GetDocumentTextDetection",
                "textract:StartDocumentAnalysis", "textract:GetDocumentAnalysis",
            ],
            Resources = ["*"],
        }));
    }
}
