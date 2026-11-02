<#
.SYNOPSIS
    Deploys the dev environment: tests, Platform stack, image, migrate, Services stack.

.DESCRIPTION
    Needs the AWS CLI, Node.js, and the .NET SDK. The AWS account must be bootstrapped for CDK
    (see "Deploying to dev" in the README). Every step stops the script if it fails.

.EXAMPLE
    powershell -File scripts/deploy-dev.ps1 -Profile dev -ModelId us.anthropic.<model> -AlarmEmail me@example.com -ImportCatalog iso_iec_27001_annexA_2022_controls.csv
#>
param(
    [Parameter(Mandatory)] [string] $Profile,
    [Parameter(Mandatory)] [string] $ModelId,
    [string] $AlarmEmail = "",
    [string] $FrontendOrigin = "",
    [string] $ImportCatalog = "",
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
    if ($LASTEXITCODE -ne 0) { 
        throw "Failed: $Description (exit code $LASTEXITCODE)" 
    }
}

# Check that the required tools are installed.
foreach ($tool in "aws", "node", "dotnet") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { 
        throw "$tool is not installed or not on the PATH." 
    }
}

Push-Location $root # Return to the root folder at the end of the script.
try {

    # Run the tests unless skipped.
    if (-not $SkipTests) {
        Invoke-Checked "Run the tests" { dotnet test }
    }

    # Generate a unique tag for the image based on the current UTC timestamp.
    $tag = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")

    # Prepare the context for CDK deploy commands.
    $context = @("-c", "env=dev", "-c", "imageTag=$tag", "-c", "assessmentModelId=$ModelId")
    
    # Set alarm email if provided, otherwise the stack will not create alarms.
    if ($AlarmEmail) { 
        $context += @("-c", "alarmEmail=$AlarmEmail") 
    }
    
    # Set frontend origin if provided, otherwise the stack won't create the frontend bucket.
    if ($FrontendOrigin) { 
        $context += @("-c", "frontendOrigin=$FrontendOrigin") 
    }

    # 1. Platform: network, database, queues, buckets, Cognito, image repository.
    $platformOutputs = Join-Path $root "infra/cdk.out/platform-outputs.json"

    # The CDK deploy command will fail if the stack is already deployed and the context has changed. 
    # To avoid that, we delete the context file before deploying.
    Push-Location infra
    try {
        Invoke-Checked "Deploy the Platform stack" {
            npx --yes aws-cdk@2 deploy Platform \
            --profile $Profile \
            --require-approval broadening \
            --outputs-file $platformOutputs \
            @context
        }
    } finally { 
        Pop-Location 
    }

    # The outputs file contains a single object with the stack outputs. We extract that object.
    $platform = (Get-Content $platformOutputs -Raw | ConvertFrom-Json).PSObject.Properties | Select-Object -First 1 -ExpandProperty Value

    # 2. Build the image and push it to ECR.
    $registry = $platform.RepositoryUri.Split("/")[0] # The registry is the first part of the URI, before the slash.
    $repository = $platform.RepositoryUri.Substring($registry.Length + 1) # The repository is the rest of the URI, after the slash.
    
    $env:SDK_CONTAINER_REGISTRY_UNAME = "AWS" # The username is always "AWS" for ECR.
    $env:SDK_CONTAINER_REGISTRY_PWORD = (aws ecr get-login-password --region $region)
    if ($LASTEXITCODE -ne 0) { 
        throw "Could not get an ECR login." 
    }

    Invoke-Checked "Build and push the image ($tag)" {
        dotnet publish src/App -c Release -t:PublishContainer ` # PublishContainer is the custom target that builds+pushes the image to ECR.
            -p:ContainerRegistry=$registry -p:ContainerRepository=$repository ` # The registry and repository are used to tag the image.
            -p:ContainerImageTag=$tag -p:ContainerRuntimeIdentifier=linux-x64 # The runtime identifier is used to build a Linux image.
    }

    # Runs one command in the one-off task and waits for it. Fails on a non-zero exit code.
    function Invoke-OneOff {
        # The command to run in the one-off task, e.g. "migrate" or "import-catalog".
        param([string] $Command)

        # The ECS run-task command requires a JSON file with the container overrides. 
        # We create a temporary file for that.
        $overrides = Join-Path ([IO.Path]::GetTempPath()) "overrides-$Command.json"
        
        @{ containerOverrides = @(@{ name = "app"; command = @($Command) }) } \
            | ConvertTo-Json -Depth 5 \
            | Set-Content $overrides -Encoding ascii
        
        Write-Host "==> Run the one-off task: $Command" -ForegroundColor Cyan
        
        # The network configuration for the Fargate task. 
        # Uses the subnets and security group from the Platform stack outputs.
        $network = "awsvpcConfiguration={subnets=[$($platform.TaskSubnetIds)],securityGroups=[$($platform.TaskSecurityGroupId)],assignPublicIp=DISABLED}"
        
        # Run the task and get its ARN.
        $arn = aws ecs run-task \
            --cluster $platform.ClusterName \
            --task-definition $platform.OneOffTaskDefinition \
            --launch-type FARGATE \
            --network-configuration $network \
            --overrides "file://$overrides" \
            --query "tasks[0].taskArn" \
            --output text
        
        # Check if the task started successfully. If not, throw an error.
        if ($LASTEXITCODE -ne 0 -or -not $arn -or $arn -eq "None") { 
            throw "The $Command task did not start." 
        }
        
        # Wait for the task to stop and get its exit code.
        aws ecs wait tasks-stopped \
            --cluster $platform.ClusterName \
            --tasks $arn
        
        # Get the exit code of the container in the task.
        $exit = aws ecs describe-tasks \
            --cluster $platform.ClusterName \
            --tasks $arn \
            --query "tasks[0].containers[0].exitCode" \
            --output text
        # If task exit code is not zero, throw an error.
        if ($exit -ne "0") { 
            throw "The $Command task failed (exit code $exit). Check the log group in CloudWatch. Task: $arn" 
        }
    }

    # 3. Migrate while the old version still runs, then roll the services.
    Invoke-OneOff "migrate"

    # 4. Import the catalog if specified. The catalog file must be in the res folder.
    if ($ImportCatalog) {
        $catalogFileName = $ImportCatalog
        $csv = Join-Path $root "res/$catalogFileName"
        if (-not (Test-Path $csv)) { 
            throw "Catalog file not found: $csv" 
        }
        Invoke-Checked "Upload the catalog" {
            aws s3 cp $csv "s3://$($platform.CatalogBucketName)/$catalogFileName"
        }
        Invoke-OneOff "import-catalog"
    }

    # 5. Deploy the Services stack, which rolls the services to the new image.
    $servicesOutputs = Join-Path $root "infra/cdk.out/services-outputs.json"
    Push-Location infra
    try {
        Invoke-Checked "Deploy the Services stack" {
            npx --yes aws-cdk@2 deploy Services \
            --profile $Profile \
            --require-approval broadening \
            --outputs-file $servicesOutputs \
            @context
        }
    } finally { 
        Pop-Location 
    }

    $services = (Get-Content $servicesOutputs -Raw | ConvertFrom-Json).PSObject.Properties | Select-Object -First 1 -ExpandProperty Value

    Write-Host ""
    Write-Host "Deployed image tag $tag" -ForegroundColor Green
    Write-Host "Site URL:           $($services.SiteUrl)"
    Write-Host "Frontend bucket:    $($services.FrontendBucketName)"
    Write-Host "User pool ID:       $($platform.UserPoolId)"
    Write-Host "User pool client:   $($platform.UserPoolClientId)"
} finally {
    Pop-Location
}
