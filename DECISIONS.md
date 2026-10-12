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