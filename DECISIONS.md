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
