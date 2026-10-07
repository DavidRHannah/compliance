# Compliance Backend

Backend for AI-assisted ISO 27001 evidence review. It stores client evidence, assesses each control against it, and records the consultant's final decision.

## Scope (phase one)

- ISO 27001:2022, the 93 Annex A controls only.
- Evidence files: PDF, DOCX, XLSX, CSV, PPTX.
- Roles: admin, consultant, client user.

## Prerequisites

- Visual Studio 2026 with the "ASP.NET and web development" workload (.NET 10 SDK)
- Docker Desktop (for integration tests, from the data milestone onward)

## Run

1. Open `ComplianceBackend.sln`.
2. Set `App` as the startup project and press F5.
3. Check `https://localhost:<port>/health` and `https://localhost:<port>/scalar`.

## Test

Run all tests from Test Explorer, or `dotnet test` from the repository root.

## Layout

```
src/App/        the application (API and worker in one project)
tests/App.Tests/ unit and integration tests
docs/           generated files. Do not edit by hand.
DECISIONS.md    hard-to-reverse choices, three lines each
```

Inside `src/App/`, code is grouped as `Features/`, `Pipeline/`, `Integrations/`, `Data/` and `Common/`. Folders are added when their first file is.

## Documentation rules

1. Behavior change: add or change the `[Requirement]` on its test first. A requirement that is not built yet is a skipped test.
2. Hard-to-reverse choice: add three lines to `DECISIONS.md` in the same pull request.
3. Never edit a file in `docs/`. The build overwrites it.
4. Never reuse a requirement ID. Mark a removed requirement withdrawn.
