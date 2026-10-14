# Backend Functional Specification: Phase One

Oct 13, 2026 (v2.3) · @David

This specification defines what the backend must do in phase one: support ISO 27001 engagements, with evidence submitted as PDF, DOCX, XLSX, CSV or PPTX files.

## Scope

The backend stores client evidence, assesses each ISO 27001 control against it, and records the consultant's final decision.

**In scope**

- ISO 27001:2022 as the only framework, limited to the 93 Annex A controls.
- Evidence files in PDF, DOCX, XLSX, CSV and PPTX.
- Sign-in for consultants and client users.
- Engagement setup, scope definition, and control applicability.
- Document upload, processing, assessment, and consultant review.
- Export of results to a spreadsheet.

**Out of scope**

- Other frameworks and cross-framework control mapping.
- Email, images, screenshots, and archive files.
- Evidence request tracking.
- Regional data residency.
- The frontend.
- The ISO 27001 management system clauses 4 to 10.
- Single sign-on.

## Roles

There are three roles, and each user has exactly one.

| Role        | Can do                                                                                                                                                                                                                                                                                   |
| ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Admin       | Everything a consultant can do. Create clients, invite and disable users, and manage assessment criteria.                                                                                                                                                                                |
| ---         | ---                                                                                                                                                                                                                                                                                      |
| Consultant  | Create and manage engagements. Upload documents, define scope, set control applicability, start assessment runs, and record decisions.                                                                                                                                                   |
| ---         | ---                                                                                                                                                                                                                                                                                      |
| Client user | For engagements of their own client only: upload documents, define scope items, and see documents, scope, and the consultant's verdict for each control. A control shows its latest consultant verdict, marked under review while it is re-assessed, or pending if it was never decided. |
| ---         | ---                                                                                                                                                                                                                                                                                      |

A client user cannot see preliminary verdicts, rationales, citations or consultant notes.

## Terms

| Term                | Meaning                                                                                                     |
| ------------------- | ----------------------------------------------------------------------------------------------------------- |
| Engagement          | One assessment of one client against ISO 27001. All data belongs to exactly one engagement.                 |
| ---                 | ---                                                                                                         |
| Control             | One of the 93 ISO 27001 Annex A controls.                                                                   |
| ---                 | ---                                                                                                         |
| Control catalog     | The number, theme, title and text of each control. Developers load it and it is not edited through the API. |
| ---                 | ---                                                                                                         |
| Scope item          | A system, asset or vendor the engagement covers, with any alternate names.                                  |
| ---                 | ---                                                                                                         |
| Document            | A file submitted as evidence.                                                                               |
| ---                 | ---                                                                                                         |
| Chunk               | A passage of a document, with its location in the source file.                                              |
| ---                 | ---                                                                                                         |
| Assessment run      | One pass that assesses every applicable control in an engagement.                                           |
| ---                 | ---                                                                                                         |
| Assessment          | The system's preliminary verdict on one control, with a rationale and citations.                            |
| ---                 | ---                                                                                                         |
| Citation            | A quoted passage from a chunk that supports an assessment.                                                  |
| ---                 | ---                                                                                                         |
| Decision            | A consultant's final verdict on an assessment.                                                              |
| ---                 | ---                                                                                                         |
| Assessment criteria | The firm's own criteria for assessing one control: the evidence to expect, and what makes it pass or fail.  |
| ---                 | ---                                                                                                         |

## Functional requirements

Each requirement has an ID so that code, tests and reviews can refer to it.

### Access

| ID   | Requirement                                                                                                                                                                                      |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| AC-1 | Every request, except the health check, must carry a valid token from the identity provider.                                                                                                     |
| ---  | ---                                                                                                                                                                                              |
| AC-2 | Every user must sign in with multi-factor authentication.                                                                                                                                        |
| ---  | ---                                                                                                                                                                                              |
| AC-3 | Users are created by invitation. There is no self-registration.                                                                                                                                  |
| ---  | ---                                                                                                                                                                                              |
| AC-4 | A user's role and client are held by the backend, not by the identity provider.                                                                                                                  |
| ---  | ---                                                                                                                                                                                              |
| AC-5 | A client user can access only the engagements of their own client.                                                                                                                               |
| ---  | ---                                                                                                                                                                                              |
| AC-6 | No operation may read or use data from an engagement other than the one requested.                                                                                                               |
| ---  | ---                                                                                                                                                                                              |
| AC-7 | An admin can disable a user. A disabled user cannot sign in, and the backend refuses their requests. An admin cannot disable themselves or the last admin. A disabled user can be enabled again. |
| ---  | ---                                                                                                                                                                                              |

### Engagements and scope

| ID   | Requirement                                                                                                                                                                                                      |
| ---- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| EN-1 | A consultant can create an engagement for a client.                                                                                                                                                              |
| ---  | ---                                                                                                                                                                                                              |
| EN-2 | An engagement is in one of three states: draft, active or closed. A closed engagement is read-only. An engagement moves from draft to active to closed, and never back. A run can start only while it is active. |
| ---  | ---                                                                                                                                                                                                              |
| EN-3 | Every ISO 27001 control is applicable to a new engagement by default.                                                                                                                                            |
| ---  | ---                                                                                                                                                                                                              |
| EN-4 | A consultant can mark a control as not applicable, and must give a justification.                                                                                                                                |
| ---  | ---                                                                                                                                                                                                              |
| EN-5 | Consultants and client users can add and edit scope items of type system, asset or vendor.                                                                                                                       |
| ---  | ---                                                                                                                                                                                                              |
| EN-6 | A scope item that an assessment refers to cannot be deleted.                                                                                                                                                     |
| ---  | ---                                                                                                                                                                                                              |
| EN-7 | Withdrawn. The organization profile is removed.                                                                                                                                                                  |
| ---  | ---                                                                                                                                                                                                              |
| EN-8 | Withdrawn. Implementation statements are removed.                                                                                                                                                                |
| ---  | ---                                                                                                                                                                                                              |
| EN-9 | The lead consultant must be an active consultant or admin. Leading an engagement gives no extra access.                                                                                                          |
| ---  | ---                                                                                                                                                                                                              |

### Controls and assessment criteria

| ID   | Requirement                                                                                                                                                                                |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| CT-1 | Each control has assessment criteria. They state the evidence to expect, the conditions for a pass, the conditions for a fail, and whether evidence that the control operates is required. |
| ---  | ---                                                                                                                                                                                        |
| CT-2 | An admin can add a new version of a control's assessment criteria. An earlier version is never edited or deleted.                                                                          |
| ---  | ---                                                                                                                                                                                        |
| CT-3 | Each control has one active version of its criteria. A run uses the versions that were active when it started.                                                                             |
| ---  | ---                                                                                                                                                                                        |
| CT-4 | A run cannot start while an applicable control has no active criteria.                                                                                                                     |
| ---  | ---                                                                                                                                                                                        |
| CT-5 | Assessment criteria belong to the firm and apply to every engagement.                                                                                                                      |
| ---  | ---                                                                                                                                                                                        |
| CT-6 | The control catalog is loaded by a developer task, not through the API. A control is never deleted.                                                                                        |
| ---  | ---                                                                                                                                                                                        |

### Documents

| ID   | Requirement                                                                                                                                    |
| ---- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| DO-1 | A user uploads a file directly to storage through a time-limited upload link. The backend does not receive the file's contents in the request. |
| ---  | ---                                                                                                                                            |
| DO-2 | The backend accepts PDF, DOCX, XLSX, CSV and PPTX files, and rejects all other types and any file over 50 MB. The size limit is configurable.  |
| ---  | ---                                                                                                                                            |
| DO-3 | A file identical to one in the engagement that is not deleted is not processed again. It ends as failed, with the reason duplicate.            |
| ---  | ---                                                                                                                                            |
| DO-4 | A stored file is never modified. Replacing a document stores a new file.                                                                       |
| ---  | ---                                                                                                                                            |
| DO-5 | Each document has a status: pending, processing, ready or failed. A failed document shows the reason.                                          |
| ---  | ---                                                                                                                                            |
| DO-6 | An authorized user can download an original file through a time-limited link.                                                                  |
| ---  | ---                                                                                                                                            |
| DO-7 | A user who may upload documents may delete a document or replace it with a new version.                                                        |
| ---  | ---                                                                                                                                            |
| DO-8 | A deleted or replaced document is excluded from search and from later runs.                                                                    |
| ---  | ---                                                                                                                                            |
| DO-9 | A file cannot be changed after its upload is confirmed. Processing and downloads always use the confirmed version.                             |
| ---  | ---                                                                                                                                            |

### Processing

| ID   | Requirement                                                                                                                                         |
| ---- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| PR-1 | Each uploaded document is processed in the background. The upload request does not wait for it.                                                     |
| ---  | ---                                                                                                                                                 |
| PR-2 | Processing extracts the text, splits it into chunks, indexes the chunks for search, and links chunks to the scope items they mention.               |
| ---  | ---                                                                                                                                                 |
| PR-3 | Each chunk records its location in the source: page for PDF, heading and paragraph for DOCX, sheet and cell range for XLSX and CSV, slide for PPTX. |
| ---  | ---                                                                                                                                                 |
| PR-4 | Any processing step can be repeated without creating duplicate data.                                                                                |
| ---  | ---                                                                                                                                                 |
| PR-5 | A document that fails a step is retried from that step. After three failed attempts it is marked failed.                                            |
| ---  | ---                                                                                                                                                 |
| PR-6 | A scanned PDF is read with text recognition, using Amazon Textract. A document that still yields no text is marked failed with that reason.         |
| ---  | ---                                                                                                                                                 |
| PR-7 | Every uploaded document ends as either ready or failed. None is left in an intermediate state.                                                      |
| ---  | ---                                                                                                                                                 |
| PR-8 | An upload that is never confirmed ends as failed after 30 minutes.                                                                                  |
| ---  | ---                                                                                                                                                 |

### Assessment

A control's verdict is one of six values. The system sets four of them, and only a consultant can set pass or fail.

| Verdict                 | Set by     | Meaning                                                                                                  |
| ----------------------- | ---------- | -------------------------------------------------------------------------------------------------------- |
| Preliminary pass        | System     | The evidence supports the control. A consultant must still review it.                                    |
| ---                     | ---        | ---                                                                                                      |
| Preliminary fail        | System     | The evidence shows the control is not met. A consultant must still review it.                            |
| ---                     | ---        | ---                                                                                                      |
| Interpretation required | System     | The system could not reach a decision on the evidence present.                                           |
| ---                     | ---        | ---                                                                                                      |
| Insufficient evidence   | System     | There is not enough evidence for a preliminary pass. A consultant can also record it as a final verdict. |
| ---                     | ---        | ---                                                                                                      |
| Pass                    | Consultant | A consultant has verified that the control is met.                                                       |
| ---                     | ---        | ---                                                                                                      |
| Fail                    | Consultant | A consultant has verified that the control is not met.                                                   |
| ---                     | ---        | ---                                                                                                      |

| ID    | Requirement                                                                                                                                                       |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AS-1  | Only a consultant can start an assessment run. A run never starts automatically.                                                                                  |
| ---   | ---                                                                                                                                                               |
| AS-2  | A run assesses every applicable control, using only the ready documents of that engagement.                                                                       |
| ---   | ---                                                                                                                                                               |
| AS-3  | Each assessment has one verdict: preliminary pass, preliminary fail, interpretation required, or insufficient evidence.                                           |
| ---   | ---                                                                                                                                                               |
| AS-4  | Each assessment has a written rationale and zero or more citations.                                                                                               |
| ---   | ---                                                                                                                                                               |
| AS-5  | Each citation names one chunk and quotes text from it. The backend checks that the quoted text appears in that chunk.                                             |
| ---   | ---                                                                                                                                                               |
| AS-6  | A citation that fails the check is marked unverified and is not presented as evidence.                                                                            |
| ---   | ---                                                                                                                                                               |
| AS-7  | A preliminary pass or preliminary fail with no verified citation is recorded as insufficient evidence.                                                            |
| ---   | ---                                                                                                                                                               |
| AS-8  | Each run records the model and the prompt version it used. Each assessment records the assessment criteria version it used.                                       |
| ---   | ---                                                                                                                                                               |
| AS-9  | An assessment is never edited or deleted. A new run adds new assessments and marks the earlier ones as superseded.                                                |
| ---   | ---                                                                                                                                                               |
| AS-10 | A run that stops partway can resume without repeating the controls already assessed.                                                                              |
| ---   | ---                                                                                                                                                               |
| AS-11 | Each assessment is made from three things: the control's text and its assessment criteria, the engagement's scope items, and the evidence found for that control. |
| ---   | ---                                                                                                                                                               |
| AS-12 | Each piece of evidence is given to the model with its document's name, type and upload date.                                                                      |
| ---   | ---                                                                                                                                                               |
| AS-13 | Text inside a document is treated as evidence only, never as an instruction to the system.                                                                        |
| ---   | ---                                                                                                                                                               |
| AS-14 | An engagement has at most one run queued or running at a time.                                                                                                    |
| ---   | ---                                                                                                                                                               |
| AS-15 | A failed run can be resumed only if it is the engagement's latest run.                                                                                            |
| ---   | ---                                                                                                                                                               |

### Review

| ID    | Requirement                                                                                                                                                                                                                                                                                                                  |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| RV-1  | A consultant can view each current assessment with its verdict, rationale, citations, and the source location of each citation.                                                                                                                                                                                              |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-2  | A consultant records a decision on an assessment: accept the verdict, or override it with a different one. Accepting a preliminary pass makes the final verdict pass, accepting a preliminary fail makes it fail, and accepting insufficient evidence keeps it. Interpretation required cannot be accepted, only overridden. |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-3  | An override must include a note.                                                                                                                                                                                                                                                                                             |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-4  | A consultant's final verdict is one of: pass, fail or insufficient evidence. Not applicable comes only from applicability (EN-4).                                                                                                                                                                                            |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-5  | A decision is never edited or deleted. A later decision on the same assessment becomes the decision of record.                                                                                                                                                                                                               |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-6  | The backend records who performed each upload, scope change, applicability change, run and decision, and when.                                                                                                                                                                                                               |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-7  | When a new run supersedes an assessment that has a decision, the new assessment is flagged for re-review and shows the earlier decision.                                                                                                                                                                                     |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-8  | When a cited document is deleted or replaced, each assessment that cites it is flagged for re-review, and the flag names the document.                                                                                                                                                                                       |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-9  | A flag does not change a verdict or start a run. The consultant decides what to do.                                                                                                                                                                                                                                          |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-10 | A consultant can export an engagement's current verdicts, rationales, citations and decisions as an XLSX file.                                                                                                                                                                                                               |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-11 | A decision on an assessment that is no longer current is rejected, and the consultant is shown the current assessment.                                                                                                                                                                                                       |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-12 | Recording a decision clears the assessment's open flags. A consultant can also clear a flag without recording a decision.                                                                                                                                                                                                    |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |
| RV-13 | After a re-run, results show a control's latest consultant verdict, marked under review, until a consultant decides on the new assessment.                                                                                                                                                                                   |
| ---   | ---                                                                                                                                                                                                                                                                                                                          |

## API

The backend exposes one JSON API over HTTPS, with every path under /api.

| Resource                           | Operations                                                                                                | Roles                                          |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| /api/me                            | Get the signed-in user                                                                                    | All                                            |
| ---                                | ---                                                                                                       | ---                                            |
| /api/clients                       | Create                                                                                                    | Admin                                          |
| ---                                | ---                                                                                                       | ---                                            |
| /api/clients                       | List, get                                                                                                 | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/users                         | Invite, disable, enable, resend invitation                                                                | Admin                                          |
| ---                                | ---                                                                                                       | ---                                            |
| /api/users                         | List, get                                                                                                 | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/controls                      | List, get, with the active criteria version                                                               | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/controls/{id}/criteria        | Add a version, list versions, activate a version                                                          | Admin (consultants can list)                   |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements                   | Create, update name and lead, activate, close                                                             | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements                   | List, get                                                                                                 | All (client users see their own client's only) |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/controls     | List, set applicability, list a control's assessment history                                              | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/scope-items  | Create, list, update, delete                                                                              | All                                            |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/documents    | Request an upload link (also used to replace), confirm upload, list, get, request a download link, delete | All                                            |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/runs         | Start, list, get progress, resume                                                                         | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/assessments  | List current, get one with citations and decisions, record a decision                                     | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/review-flags | List open flags, clear a flag                                                                             | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/results      | Get each control's latest consultant verdict, not applicable, or pending, with totals                     | All                                            |
| ---                                | ---                                                                                                       | ---                                            |
| /api/engagements/{id}/export       | Download results as XLSX                                                                                  | Admin, consultant                              |
| ---                                | ---                                                                                                       | ---                                            |

- Errors use the standard problem details format, with an HTTP status code that matches the failure.
- List operations return results in pages.
- State changes are named actions, such as activate, close, resume and clear. Work that takes time is accepted at once and then polled.
- The technical specification lists every endpoint with its status codes.
- A request for an engagement the user may not access returns "not found", so that it does not reveal the engagement exists. This check runs before the role check, so a client user who asks for another client's engagement always gets "not found".

## Quality requirements

| ID   | Area          | Requirement                                                                                                                                                    |
| ---- | ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| QR-1 | Isolation     | Stored files, search and assessments are separated by engagement. A test must show that a search in one engagement cannot return another engagement's content. |
| ---  | ---           | ---                                                                                                                                                            |
| QR-2 | Security      | All data is encrypted in transit and at rest.                                                                                                                  |
| ---  | ---           | ---                                                                                                                                                            |
| QR-3 | Security      | The backend holds no long-lived credentials. It reaches AWS services through its assigned role.                                                                |
| ---  | ---           | ---                                                                                                                                                            |
| QR-4 | Model use     | Every model call goes through Amazon Bedrock in the firm's own AWS account.                                                                                    |
| ---  | ---           | ---                                                                                                                                                            |
| QR-5 | Reliability   | A message may be delivered more than once without causing duplicate or incorrect data.                                                                         |
| ---  | ---           | ---                                                                                                                                                            |
| QR-6 | Reliability   | A message that cannot be processed is moved to a dead-letter queue and raises an alert.                                                                        |
| ---  | ---           | ---                                                                                                                                                            |
| QR-7 | Traceability  | Every verdict can be traced to its run, model, prompt version, assessment criteria version, and cited passages.                                                |
| ---  | ---           | ---                                                                                                                                                            |
| QR-8 | Observability | Each request and background job writes logs that share one correlation ID.                                                                                     |
| ---  | ---           | ---                                                                                                                                                            |
| QR-9 | Performance   | Targets will be set after the first working version is measured.                                                                                               |
| ---  | ---           | ---                                                                                                                                                            |