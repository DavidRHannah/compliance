using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.KMS;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Amazon.CDK.AWS.SQS;
using Constructs;
using Secret = Amazon.CDK.AWS.ECS.Secret;

namespace Infra;

/// <summary>
/// The long-lived resources. Deployed first, because the migrate task needs the database and the
/// image repository before the services start.
/// </summary>
public class PlatformStack : Stack
{
    public const int ApiPort = 8080;

    public InfraSettings Settings { get; }
    public Vpc Vpc { get; }
    public SecurityGroup TaskSecurityGroup { get; }
    public SecurityGroup LoadBalancerSecurityGroup { get; }
    public SubnetSelection TaskSubnets { get; }
    public Key Key { get; }
    public DatabaseCluster Database { get; }
    public Bucket DocumentsBucket { get; }
    public Bucket CatalogBucket { get; }
    public Queue AssessmentQueue { get; }
    public Queue DocumentQueue { get; }
    public Queue AssessmentDeadLetterQueue { get; }
    public Queue DocumentDeadLetterQueue { get; }
    public UserPool UserPool { get; }
    public UserPoolClient UserPoolClient { get; }
    public Repository Repository { get; }
    public Cluster Cluster { get; }
    public LogGroup LogGroup { get; }
    public Topic AlarmTopic { get; }
    public FargateTaskDefinition OneOffTaskDefinition { get; }

    public PlatformStack(Construct scope, string id, InfraSettings settings, IStackProps props) : base(scope, id, props)
    {
        Settings = settings;

        // Networking: no NAT gateway by default. Tasks reach AWS through VPC endpoints.
        var subnetType = settings.NatGateway ? SubnetType.PRIVATE_WITH_EGRESS : SubnetType.PRIVATE_ISOLATED;
        Vpc = new Vpc(this, "Vpc", new VpcProps
        {
            MaxAzs = 2,
            NatGateways = settings.NatGateway ? 1 : 0,
            SubnetConfiguration =
            [
                // Public subnets exist for the internet gateway that CloudFront's VPC origin needs
                // (and for the NAT gateway if it is switched on). No task runs here.
                new SubnetConfiguration { Name = "public", SubnetType = SubnetType.PUBLIC, CidrMask = 24 },
                new SubnetConfiguration { Name = "private", SubnetType = subnetType, CidrMask = 24 },
                new SubnetConfiguration { Name = "isolated", SubnetType = SubnetType.PRIVATE_ISOLATED, CidrMask = 24 },
            ],
        });
        TaskSubnets = new SubnetSelection { SubnetGroupName = "private" };

        Vpc.AddGatewayEndpoint("S3Endpoint", new GatewayVpcEndpointOptions { Service = GatewayVpcEndpointAwsService.S3 });

        // Dev keeps one endpoint interface per service, in one AZ. Production should use both.
        var endpointSubnets = new SubnetSelection
        {
            SubnetGroupName = "private",
            AvailabilityZones = settings.EnvName == "prod" ? Vpc.AvailabilityZones : [Vpc.AvailabilityZones[0]],
        };
        var endpoints = new Dictionary<string, IInterfaceVpcEndpointService>
        {
            ["Sqs"] = InterfaceVpcEndpointAwsService.SQS,
            ["BedrockRuntime"] = InterfaceVpcEndpointAwsService.BEDROCK_RUNTIME,
            ["Textract"] = InterfaceVpcEndpointAwsService.TEXTRACT,
            ["SecretsManager"] = InterfaceVpcEndpointAwsService.SECRETS_MANAGER,
            ["EcrApi"] = InterfaceVpcEndpointAwsService.ECR,
            ["EcrDocker"] = InterfaceVpcEndpointAwsService.ECR_DOCKER,
            ["Logs"] = InterfaceVpcEndpointAwsService.CLOUDWATCH_LOGS,
            ["CognitoIdp"] = new InterfaceVpcEndpointService($"com.amazonaws.{Region}.cognito-idp"),
        };
        foreach (var (name, service) in endpoints)
        {
            Vpc.AddInterfaceEndpoint($"{name}Endpoint", new InterfaceVpcEndpointOptions
            {
                Service = service,
                Subnets = endpointSubnets,
                PrivateDnsEnabled = true,
            });
        }

        // One security group for every task. The database accepts connections from it only.
        TaskSecurityGroup = new SecurityGroup(this, "TaskSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "API, worker and one-off tasks",
            AllowAllOutbound = true,
        });
        // The internal load balancer takes traffic from the CloudFront VPC origin, which sits inside the VPC.
        // Both security groups live here so the services stack never changes a rule in this one.
        LoadBalancerSecurityGroup = new SecurityGroup(this, "LoadBalancerSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            Description = "Internal load balancer",
            AllowAllOutbound = true,
        });
        LoadBalancerSecurityGroup.AddIngressRule(Peer.Ipv4(Vpc.VpcCidrBlock), Port.Tcp(80), "CloudFront VPC origin");
        TaskSecurityGroup.AddIngressRule(LoadBalancerSecurityGroup, Port.Tcp(ApiPort), "Load balancer to API");

        Key = new Key(this, "Key", new KeyProps { EnableKeyRotation = true, Description = "Documents and queues" });

        Database = new DatabaseCluster(this, "Database", new DatabaseClusterProps
        {
            Engine = DatabaseClusterEngine.AuroraPostgres(new AuroraPostgresClusterEngineProps
            {
                Version = AuroraPostgresEngineVersion.Of("17.5", "17"),
            }),
            Writer = ClusterInstance.ServerlessV2("Writer"),
            ServerlessV2MinCapacity = 0.5,
            ServerlessV2MaxCapacity = InfraSettings.MaxAcu,
            Vpc = Vpc,
            VpcSubnets = new SubnetSelection { SubnetGroupName = "isolated" },
            DefaultDatabaseName = "compliance",
            Credentials = Credentials.FromGeneratedSecret("app_admin"),
            StorageEncrypted = true,
            Backup = new BackupProps { Retention = Duration.Days(7) },
            RemovalPolicy = RemovalPolicy.SNAPSHOT,
        });
        Database.Connections.AllowDefaultPortFrom(TaskSecurityGroup, "Tasks to Aurora");

        DocumentsBucket = new Bucket(this, "DocumentsBucket", new BucketProps
        {
            Encryption = BucketEncryption.KMS,
            EncryptionKey = Key,
            BucketKeyEnabled = true,
            EnforceSSL = true,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Versioned = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
            Cors =
            [
                new CorsRule
                {
                    AllowedOrigins = [settings.FrontendOrigin],
                    AllowedMethods = [HttpMethods.PUT, HttpMethods.GET],
                    AllowedHeaders = ["*"],
                    MaxAge = 3000,
                },
            ],
        });

        CatalogBucket = new Bucket(this, "CatalogBucket", new BucketProps
        {
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        // The consumer gives up after 3 receives (AssessmentConsumer.MaxReceives).
        (AssessmentQueue, AssessmentDeadLetterQueue) = CreateQueue("Assessment");
        (DocumentQueue, DocumentDeadLetterQueue) = CreateQueue("Document");

        UserPool = new UserPool(this, "UserPool", new UserPoolProps
        {
            SelfSignUpEnabled = false,
            SignInAliases = new SignInAliases { Email = true },
            StandardAttributes = new StandardAttributes { Email = new StandardAttribute { Required = true, Mutable = false } },
            Mfa = Mfa.REQUIRED,
            MfaSecondFactor = new MfaSecondFactor { Otp = true, Sms = false },
            PasswordPolicy = new PasswordPolicy { MinLength = 12, RequireDigits = true, RequireLowercase = true, RequireUppercase = true, RequireSymbols = true },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });
        UserPoolClient = UserPool.AddClient("WebClient", new UserPoolClientOptions
        {
            GenerateSecret = false,
            AuthFlows = new AuthFlow { UserSrp = true },
            AccessTokenValidity = Duration.Hours(1),
            PreventUserExistenceErrors = true,
        });

        Repository = new Repository(this, "Repository", new RepositoryProps
        {
            RepositoryName = $"compliance-backend-{settings.EnvName}",
            ImageScanOnPush = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
            LifecycleRules = [new Amazon.CDK.AWS.ECR.LifecycleRule { MaxImageCount = 20 }],
        });

        Cluster = new Cluster(this, "Cluster", new ClusterProps { Vpc = Vpc });
        LogGroup = new LogGroup(this, "Logs", new LogGroupProps
        {
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        // The one-off task runs migrate, import-catalog and the like. The command is set at run time.
        OneOffTaskDefinition = NewTaskDefinition(this, "OneOff", workerEnabled: false);
        CatalogBucket.GrantRead(OneOffTaskDefinition.TaskRole);

        AlarmTopic = new Topic(this, "Alarms");
        if (settings.AlarmEmail is not null)
        {
            AlarmTopic.AddSubscription(new EmailSubscription(settings.AlarmEmail));
        }

        AddAlarms();
        AddOutputs();
    }

    /// <summary>
    /// A Fargate task definition with the app container, the same settings for every role. It is created
    /// in the scope of the stack that runs it, so its role and policies stay in that stack.
    /// </summary>
    public FargateTaskDefinition NewTaskDefinition(Construct scope, string name, bool workerEnabled, int cpu = 512, int memory = 1024)
    {
        var definition = new FargateTaskDefinition(scope, $"{name}Task", new FargateTaskDefinitionProps
        {
            Cpu = cpu,
            MemoryLimitMiB = memory,
        });

        var dbSecret = Database.Secret!;
        definition.AddContainer("app", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromEcrRepository(Repository, Settings.ImageTag),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps { LogGroup = LogGroup, StreamPrefix = name.ToLowerInvariant() }),
            PortMappings = [new PortMapping { ContainerPort = ApiPort }],
            Environment = new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_HTTP_PORTS"] = ApiPort.ToString(),
                ["Worker__Enabled"] = workerEnabled ? "true" : "false",
                ["Database__Host"] = Database.ClusterEndpoint.Hostname,
                ["Database__Port"] = Database.ClusterEndpoint.Port.ToString(),
                ["Database__Name"] = "compliance",
                ["Auth__Authority"] = $"https://cognito-idp.{Region}.amazonaws.com/{UserPool.UserPoolId}",
                ["Auth__ClientId"] = UserPoolClient.UserPoolClientId,
                ["Auth__UserPoolId"] = UserPool.UserPoolId,
                ["Bedrock__AssessmentModelId"] = Settings.AssessmentModelId,
                ["Bedrock__EmbeddingModelId"] = InfraSettings.EmbeddingModelId,
                ["Queues__Assessments__Url"] = AssessmentQueue.QueueUrl,
                ["Queues__Documents__Url"] = DocumentQueue.QueueUrl,
                ["Catalog__Bucket"] = CatalogBucket.BucketName,
                ["Catalog__Key"] = "iso_iec_27001_annexA_2022_controls.csv",
            },
            Secrets = new Dictionary<string, Secret>
            {
                ["Database__Username"] = Secret.FromSecretsManager(dbSecret, "username"),
                ["Database__Password"] = Secret.FromSecretsManager(dbSecret, "password"),
            },
        });
        return definition;
    }

    private (Queue Queue, Queue DeadLetter) CreateQueue(string name)
    {
        var deadLetter = new Queue(this, $"{name}DeadLetterQueue", new QueueProps
        {
            Encryption = QueueEncryption.KMS,
            EncryptionMasterKey = Key,
            RetentionPeriod = Duration.Days(14),
        });
        var queue = new Queue(this, $"{name}Queue", new QueueProps
        {
            Encryption = QueueEncryption.KMS,
            EncryptionMasterKey = Key,
            VisibilityTimeout = Duration.Minutes(5),
            DeadLetterQueue = new DeadLetterQueue { Queue = deadLetter, MaxReceiveCount = 3 },
        });
        return (queue, deadLetter);
    }

    private void AddAlarms()
    {
        var action = new SnsAction(AlarmTopic);

        foreach (var (name, queue, deadLetter) in new[]
        {
            ("Assessment", AssessmentQueue, AssessmentDeadLetterQueue),
            ("Document", DocumentQueue, DocumentDeadLetterQueue),
        })
        {
            // A message in a dead-letter queue means a job failed three times.
            new Alarm(this, $"{name}DeadLetterNotEmpty", new AlarmProps
            {
                AlarmDescription = $"The {name.ToLowerInvariant()} dead-letter queue is not empty.",
                Metric = deadLetter.MetricApproximateNumberOfMessagesVisible(new MetricOptions { Period = Duration.Minutes(1) }),
                Threshold = 0,
                EvaluationPeriods = 1,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING,
            }).AddAlarmAction(action);

            new Alarm(this, $"{name}OldestMessage", new AlarmProps
            {
                AlarmDescription = $"The oldest {name.ToLowerInvariant()} message is over 30 minutes old.",
                Metric = queue.MetricApproximateAgeOfOldestMessage(new MetricOptions { Period = Duration.Minutes(5) }),
                Threshold = 30 * 60,
                EvaluationPeriods = 1,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING,
            }).AddAlarmAction(action);
        }

        new Alarm(this, "AuroraAtMaxCapacity", new AlarmProps
        {
            AlarmDescription = "Aurora Serverless capacity has reached its maximum.",
            Metric = new Metric(new MetricProps
            {
                Namespace = "AWS/RDS",
                MetricName = "ServerlessDatabaseCapacity",
                DimensionsMap = new Dictionary<string, string> { ["DBClusterIdentifier"] = Database.ClusterIdentifier },
                Statistic = "Maximum",
                Period = Duration.Minutes(5),
            }),
            Threshold = InfraSettings.MaxAcu,
            EvaluationPeriods = 3,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        }).AddAlarmAction(action);
    }

    private void AddOutputs()
    {
        // The deploy script reads these.
        void Output(string name, string value) => new CfnOutput(this, name, new CfnOutputProps { Value = value });

        Output("ClusterName", Cluster.ClusterName);
        Output("OneOffTaskDefinition", OneOffTaskDefinition.Family);
        Output("RepositoryUri", Repository.RepositoryUri);
        Output("CatalogBucketName", CatalogBucket.BucketName);
        Output("TaskSecurityGroupId", TaskSecurityGroup.SecurityGroupId);
        Output("TaskSubnetIds", Fn.Join(",", Vpc.SelectSubnets(TaskSubnets).SubnetIds));
        Output("UserPoolId", UserPool.UserPoolId);
        Output("UserPoolClientId", UserPoolClient.UserPoolClientId);
    }
}
