<#
.SYNOPSIS
    Deploys the dev environment: tests, Platform stack, image, migrate, Services stack.

.DESCRIPTION
    Needs the AWS CLI, Node.js and the .NET SDK. The AWS account must be bootstrapped for CDK
    (see "Deploying to dev" in the README). Every step stops the script if it fails.

.EXAMPLE
    powershell -File scripts/deploy-dev.ps1 -Profile dev -ModelId us.anthropic.<model> -AlarmEmail me@example.com -ImportCatalog
#>
param(
    [Parameter(Mandatory)] [string] $Profile,
    [Parameter(Mandatory)] [string] $ModelId,
    [string] $AlarmEmail = "",
    [string] $FrontendOrigin = "",
    [switch] $ImportCatalog,
    [switch] $SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$region = "us-east-1"
$env:AWS_PROFILE = $Profile
$env:AWS_REGION = $region
$env:AWS_DEFAULT_REGION = $region

# Runs a native command and stops if it exits with an error.
function Invoke-Checked {
    param([string] $Description, [scriptblock] $Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Failed: $Description (exit code $LASTEXITCODE)" }
}

foreach ($tool in "aws", "node", "dotnet") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not installed or not on the PATH." }
}

Push-Location $root
try {
    if (-not $SkipTests) {
        Invoke-Checked "Run the tests" { dotnet test }
    }

    $tag = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
    $context = @("-c", "env=dev", "-c", "imageTag=$tag", "-c", "assessmentModelId=$ModelId")
    if ($AlarmEmail) { $context += @("-c", "alarmEmail=$AlarmEmail") }
    if ($FrontendOrigin) { $context += @("-c", "frontendOrigin=$FrontendOrigin") }

    # 1. Platform: network, database, queues, buckets, Cognito, image repository.
    $platformOutputs = Join-Path $root "infra/cdk.out/platform-outputs.json"
    Push-Location infra
    try {
        Invoke-Checked "Deploy the Platform stack" {
            npx --yes aws-cdk@2 deploy Platform --profile $Profile --require-approval broadening --outputs-file $platformOutputs @context
        }
    } finally { Pop-Location }
    $out = (Get-Content $platformOutputs -Raw | ConvertFrom-Json).PSObject.Properties | Select-Object -First 1 -ExpandProperty Value

    # 2. Build the image and push it to ECR.
    $registry = $out.RepositoryUri.Split("/")[0]
    $repository = $out.RepositoryUri.Substring($registry.Length + 1)
    $env:SDK_CONTAINER_REGISTRY_UNAME = "AWS"
    $env:SDK_CONTAINER_REGISTRY_PWORD = (aws ecr get-login-password --region $region)
    if ($LASTEXITCODE -ne 0) { throw "Could not get an ECR login." }
    Invoke-Checked "Build and push the image ($tag)" {
        dotnet publish src/App -c Release -t:PublishContainer `
            -p:ContainerRegistry=$registry -p:ContainerRepository=$repository `
            -p:ContainerImageTag=$tag -p:ContainerRuntimeIdentifier=linux-x64
    }

    # Runs one command in the one-off task and waits for it. Fails on a non-zero exit code.
    function Invoke-OneOff {
        param([string] $Command)
        $overrides = Join-Path ([IO.Path]::GetTempPath()) "overrides-$Command.json"
        @{ containerOverrides = @(@{ name = "app"; command = @($Command) }) } | ConvertTo-Json -Depth 5 | Set-Content $overrides -Encoding ascii
        Write-Host "==> Run the one-off task: $Command" -ForegroundColor Cyan
        $network = "awsvpcConfiguration={subnets=[$($out.TaskSubnetIds)],securityGroups=[$($out.TaskSecurityGroupId)],assignPublicIp=DISABLED}"
        $arn = aws ecs run-task --cluster $out.ClusterName --task-definition $out.OneOffTaskDefinition --launch-type FARGATE `
            --network-configuration $network --overrides "file://$overrides" --query "tasks[0].taskArn" --output text
        if ($LASTEXITCODE -ne 0 -or -not $arn -or $arn -eq "None") { throw "The $Command task did not start." }
        aws ecs wait tasks-stopped --cluster $out.ClusterName --tasks $arn
        $exit = aws ecs describe-tasks --cluster $out.ClusterName --tasks $arn --query "tasks[0].containers[0].exitCode" --output text
        if ($exit -ne "0") { throw "The $Command task failed (exit code $exit). Check the log group in CloudWatch. Task: $arn" }
    }

    # 3. Migrate while the old version still runs, then roll the services.
    Invoke-OneOff "migrate"

    if ($ImportCatalog) {
        $csv = Join-Path $root "res/iso_iec_27001_annexA_2022_controls.csv"
        if (-not (Test-Path $csv)) { throw "Catalog file not found: $csv" }
        Invoke-Checked "Upload the catalog" {
            aws s3 cp $csv "s3://$($out.CatalogBucketName)/iso_iec_27001_annexA_2022_controls.csv"
        }
        Invoke-OneOff "import-catalog"
    }

    $servicesOutputs = Join-Path $root "infra/cdk.out/services-outputs.json"
    Push-Location infra
    try {
        Invoke-Checked "Deploy the Services stack" {
            npx --yes aws-cdk@2 deploy Services --profile $Profile --require-approval broadening --outputs-file $servicesOutputs @context
        }
    } finally { Pop-Location }
    $services = (Get-Content $servicesOutputs -Raw | ConvertFrom-Json).PSObject.Properties | Select-Object -First 1 -ExpandProperty Value

    Write-Host ""
    Write-Host "Deployed image tag $tag" -ForegroundColor Green
    Write-Host "Site URL:           $($services.SiteUrl)"
    Write-Host "Frontend bucket:    $($services.FrontendBucketName)"
    Write-Host "User pool ID:       $($out.UserPoolId)"
    Write-Host "User pool client:   $($out.UserPoolClientId)"
} finally {
    Pop-Location
}
