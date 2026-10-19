# Decisions

One entry per hard-to-reverse choice: the date, the choice, and the reason. Newest last. Never edit an old entry; add a new one that names the entry it replaces.

## 1. One ASP.NET project for API and worker
- Date: 2026-10-06
- Choice: One project and one image. The setting `Worker:Enabled` turns the queue consumers on.
- Reason: One codebase to build and deploy for a small team. The two services still scale separately.

## 2. Vectors in PostgreSQL (pgvector)
- Date: 2026-10-06
- Choice: Store embeddings in Aurora PostgreSQL with pgvector, not in a separate vector database.
- Reason: Search is always scoped to one engagement, and chunks, citations and verdicts must join in one transactional store.

## 3. ECS Fargate for hosting
- Date: 2026-10-06
- Choice: Run the API and worker as Fargate services, not as Lambda functions.
- Reason: The worker is a long-running queue consumer. Lambda would add a 15-minute limit, cold starts and a database proxy.

## 4. AWS CDK in C# for infrastructure
- Date: 2026-10-06
- Choice: Define infrastructure with CDK in C#, not SAM.
- Reason: SAM targets serverless applications. CDK handles containers and uses the application's language.

## 5. Models through Amazon Bedrock
- Date: 2026-10-06
- Choice: Call Claude and the Titan embedding model through Bedrock, starting with Claude Haiku.
- Reason: Client documents stay inside the firm's AWS account. The model is a setting, chosen by evaluation.

## 6. Prompt in three layers
- Date: 2026-10-06
- Choice: Build each prompt from a template (versioned file), the control and its control test (database), and the engagement context (database).
- Reason: The standard's text does not say what proves a control, and that differs by client. Each layer has a different owner and rate of change.

## 7. Prompt templates as files, never edited
- Date: 2026-10-06
- Choice: A template is a versioned file in the code. A change is a new file.
- Reason: The template and the code that reads the model's answer must change together, and past verdicts must stay traceable to the exact prompt.

## 8. Six verdicts, with pass and fail set only by a consultant
- Date: 2026-10-06
- Choice: The system sets preliminary pass, preliminary fail, interpretation required, or insufficient evidence. Only a consultant sets pass or fail.
- Reason: The consultant has the final say, and "not enough evidence" must stay distinct from "evidence shows a problem".

## 9. Requirements live on tests
- Date: 2026-10-06
- Choice: Each requirement's ID and text is written once, in a `[Requirement]` attribute on the test that proves it. The requirements page is generated.
- Reason: One home for each requirement, and the page cannot claim what the code does not do.

## 10. Reverse part of Decision #6, Prompt in Three Layers
- Date: 2026-10-11
- Choice: Engagement context in prompts is scope items only. 
- Reason: No organization profile or implementation statements, because they describe the company or the client’s claims, not evidence for the assessed scope

## 11. Database rules live in the database, mapped by EF Core
- Date: 2026-10-14
- Choice: Table and column names are snake_case, set by EFCore.NamingConventions. Enums are PostgreSQL enum types. Entities have foreign key properties but no navigation properties. Every foreign key restricts deletes, except scope item to mentions, which cascades. Keys, CHECKs and partial unique indexes are in the EF model. Triggers and the case-insensitive email index are hand-written SQL in the migrations.
- Reason: The database keeps engagements apart and keeps history even if application code has a bug. Without navigations, every join names engagement_id. Renaming tables, columns or enum types later needs a migration that stays compatible with the running version.

## 12. docs/schema.dbml is generated from the migrations
- Date: 2026-10-14
- Choice: scripts/generate-schema-dbml.ps1 applies the migrations to a throwaway database and reads the schema back with the DBML CLI. CI fails if the committed file differs.
- Reason: The schema reference cannot drift from the migrations. DBML cannot show partial index conditions, expression indexes or triggers, so those are read from the EF configurations and the migrations.

## 13. Access check in one endpoint filter, not ASP.NET role policies
- Date: 2026-10-14
- Choice: The authorization middleware only requires a valid Cognito access token. One filter on every route under /api then checks, in order: user known (401 if not), user disabled (403), engagement visible (404), role allowed (403). A route that declares no roles is refused.
- Reason: Role policies run before the engagement is loaded, so a client user asking for another client's engagement would get 403 and learn that it exists. The order lives in one pure function that unit tests cover.

## 14. Enums in JSON are snake_case strings
- Date: 2026-10-14
- Choice: The API writes and reads enums as snake_case strings, such as "client" and "preliminary_pass", set once in Program.cs for all endpoints.
- Reason: Strings stay readable and do not change meaning if an enum's order changes. They are the same names the database uses. The frontend depends on them, so a change needs both repositories.

## 15. Keyset paging on UUIDv7 IDs
- Date: 2026-10-14
- Choice: Lists sort by ID and return items and nextCursor. The cursor is the last ID on the page, base64url-encoded, and clients treat it as opaque. The limit is 50 by default and 100 at most, so an engagement's 93 controls fit in one page.
- Reason: UUIDv7 IDs sort by creation time, so pages stay stable while rows are added, with no offset scans. The response shape is part of the API contract.

## 16. Queue messages are sent before the transaction commits
- Date: 2026-10-14
- Choice: A handler saves its changes, sends its queue message, then commits. This applies only to messages whose handlers can safely run again, such as scope re-linking.
- Reason: A commit followed by a failed send would lose the message, and nothing would retry it. A message sent for a change that then rolls back does no harm, because the handler repeats safely (PR-4, QR-5). No outbox table is needed.

## 17. Cognito account first, users row second
- Date: 2026-10-14
- Choice: Inviting creates the Cognito account, then the users row with its subject. If the row cannot be saved, the account is deleted. Disable and enable change the row, call Cognito, then commit. The user pool signs in with email as the username attribute, so the Cognito username is the subject.
- Reason: users.external_id is required, so the subject must exist first. This order keeps Cognito and the database in step without a background job.


## 18. A run is marked failed only after every control has finished
- Date: 2026-10-14
- Choice: A control that fails its last attempt records failed_at and the error on its row. The run changes to failed (or completed) only when every control has an assessment or a recorded failure. The worker ignores messages for runs that are not queued or running. The sweeper fails a run with no progress for 30 minutes, so a consultant can resume it.
- Reason: Failing the run at the first failure would let a new run start while messages of the old one are still in flight. Waiting keeps the one-active-run rule honest and lets the other controls finish, so a resume repeats only the failed ones.

## 19. Evidence search details
- Date: 2026-10-14
- Choice: Each query runs a vector search and a full-text search, 20 chunks each. Lists are merged by reciprocal rank fusion with k = 60, ties go to the smaller ID, and the top 12 are kept. The full-text query matches any of the query's words. Only chunks of ready documents are searched. The vector search sets hnsw.iterative_scan to relaxed_order and repeats the partial index condition exactly; the ready check is a scalar subquery so the plan keeps using the index.
- Reason: A query is a phrase, and a chunk rarely holds every word, so an all-words match would find nothing. Documents that are not ready have chunks being written, and their text is not yet evidence. A test reads the plan of the vector query.

## 20. Citation matching ignores case and whitespace
- Date: 2026-10-14
- Choice: A quote is verified when it appears in a chunk that was sent, ignoring case and all whitespace. The offsets stored are those of the original chunk text. A citation to a chunk that was not sent is stored as unverified if the chunk belongs to the engagement, and dropped otherwise. Citations to chunks deleted before the save are dropped.
- Reason: Models copy quotes with different line breaks and spacing. The table needs a real chunk for every citation, so an ID that names no chunk of the engagement cannot be stored. Unverified citations never leave the API.

## 21. Prompt layout
- Date: 2026-10-14
- Choice: The instructions are the Converse system prompt and the control, criteria, scope items and evidence are the user message. Each chunk sits in its own evidence block with its ID, document name, type and upload date, and a tag inside a chunk's text is made harmless. Placeholders are filled in one pass, so text inside a value is never read as a placeholder. The model answers through one tool, and throttled calls are retried by the SDK client's adaptive retry mode.
- Reason: Keeping document text out of the system prompt and inside delimited blocks supports the rule that document text is evidence, never an instruction (AS-13). Retries and backoff for throttling come from the SDK, so there is no retry code of our own to test.

## 22. What "under review" means in results
- Date: 2026-10-14
- Choice: A control is under review when its latest consultant decision is on an earlier assessment and its current assessment has no decision. Clearing a flag does not end it; only a decision on the current assessment does. Results are paged by control ID like other lists, and the totals always cover the whole engagement.
- Reason: RV-13 says the earlier verdict stays, marked under review, until a consultant decides on the new assessment. Client users see this value and the frontend depends on the response shape, so a change needs both repositories.

## 23. Region and accounts
- Date: 2026-10-14
- Choice: Everything runs in us-east-1. Development and production are separate AWS accounts, and the CDK app takes the environment as context. Development never holds client documents.
- Reason: Bedrock models and Textract must be available in the region, and us-east-1 has them. Separate accounts keep client documents out of the account where we experiment.

## 24. No NAT gateway; VPC endpoints instead
- Date: 2026-10-14
- Choice: Tasks run in subnets with no route to the internet. They reach S3 through a gateway endpoint and SQS, Bedrock, Textract, Secrets Manager, ECR, CloudWatch Logs and Cognito through interface endpoints. Development puts each interface endpoint in one AZ. The `natGateway` context value adds one NAT gateway if something turns out to need the internet (see decision 25 for the one known doubt).
- Reason: Client documents and prompts never leave the AWS network, and a NAT gateway costs about as much as four endpoints. One endpoint AZ halves the endpoint cost in dev; production should use both.

## 25. Sign-in keys are fetched through the Cognito endpoint (to verify)
- Date: 2026-10-14
- Choice: The API validates tokens with the keys from the pool's .well-known/jwks.json, and the task has no internet route. We expect the Cognito interface endpoint to serve that path. The first dev deploy checks it. If it does not, the options are a NAT gateway in dev or keys cached by the app.
- Reason: The AWS documentation does not say whether the path is served through the endpoint. Finding out costs one request on the first deploy.

## 26. Internal load balancer behind a CloudFront VPC origin
- Date: 2026-10-14
- Choice: CloudFront serves the frontend bucket and sends /api/* to an internal ALB through a VPC origin. The ALB listens on port 80 and accepts traffic from the VPC address range. Dev uses the CloudFront default domain, so there is no certificate.
- Reason: The ALB has no public address, so nothing reaches the API except through CloudFront. Traffic between CloudFront and the ALB stays inside AWS. The VPC range is wider than the diagram's "CloudFront only" rule, but only our own tasks and the origin's network interfaces live in the VPC. A custom domain and HTTPS to the ALB come with production.

## 27. Database connection assembled from the Aurora secret
- Date: 2026-10-14
- Choice: If ConnectionStrings:Database is empty, the app builds the string from Database:Host, Port, Name, Username and Password, with SSL required. ECS injects the username and password from the Aurora secret. The server certificate is not verified yet, because the RDS certificate authority is not in the minimal image.
- Reason: ECS cannot build a connection string from a secret, and a stored full string would go stale when the secret rotates. Verifying the certificate is a production task.

## 28. Two stacks, with migrate between them
- Date: 2026-10-14
- Choice: PlatformStack holds the network, database, queues, buckets, Cognito and the image repository. ServicesStack holds the API, the worker, the load balancer and CloudFront. A deploy runs Platform, pushes the image, runs the migrate task, then runs Services. Task definitions are created in the stack that runs them, and both security groups are in Platform.
- Reason: The spec says migrations run before the services roll. A migration needs the database and the image, which only the first stack provides. Putting the security groups in one stack avoids a dependency cycle between the stacks.

## 29. Container image from dotnet publish, not a Dockerfile
- Date: 2026-10-14
- Choice: The image is built with dotnet publish -t:PublishContainer on the chiseled Ubuntu base image, running as the app user on port 8080.
- Reason: One less file to keep in step with the project, and a smaller image with no shell. The cost is that Docker features such as multi-stage tweaks are not available; revisit if we need them.

## 30. Development sizing
- Date: 2026-10-14
- Choice: Aurora Serverless v2 with a single writer and 0.5 to 4 ACU. Two API tasks and one worker, scaling up to four of each. Logs are kept for one month.
- Reason: Enough to test the whole flow for little money. Production adds a reader for failover and sets the capacity from measured load.
