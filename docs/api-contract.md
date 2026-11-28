# Tamkeen Backend API

Local development listens on `http://localhost:5088`. Use the IDs returned by the API. Seed IDs are database-generated. Bodies and responses use camelCase JSON except for file uploads and PDF downloads. Dates are ISO 8601 UTC timestamps. All successful endpoints return HTTP 200. No pagination is applied in this MVP.

## Authentication

Send `Authorization: Bearer <accessToken>` on signed-in requests. On token expiry, sign in again. For Google, obtain an ID token for the configured web client ID from Google Identity Services.

| Method and path | Access | Request body | Response |
| --- | --- | --- | --- |
| POST `/api/auth/register` | Public | `{fullName,email,password}` | `Session` |
| POST `/api/auth/login` | Public | `{email,password}` | `Session` |
| POST `/api/auth/google` | Public, optional bearer to link | `{idToken}` | `Session` |
| GET `/api/auth/me` | Signed in | None | `{user: Profile, enrollments: [{id,trackId,status}]}` |

```ts
type Profile = { id: string; fullName: string; email: string; createdAt: string };
type Session = { accessToken: string; expiresAt: string; user: Profile };
```

The password must satisfy Identity's default rules (at least six characters including an uppercase letter, lowercase letter, digit and non-alphanumeric character). Invalid credentials return 401. Identity temporarily locks accounts after repeated failed password checks. Register returns 400 for validation or Identity errors. Google validates the signature, issuer, expiry, audience and verified email. To link an existing password account, first sign in, then submit the Google ID token with that account's bearer token. A conflicting link returns 409.

## Tracks and tasks

| Method and path | Access | Request body | Response |
| --- | --- | --- | --- |
| GET `/api/tracks` | Public | None | `Track[]`, sorted by display order |
| POST `/api/tracks/{trackId}/enroll` | Signed in | None | `{id,trackId,status,enrolledAt}` |
| GET `/api/tracks/{trackId}/my-progress` | Signed in, enrolled | None | `EnrollmentProgress` |
| GET `/api/tasks/{taskId}` | Signed in, enrolled | None | `TaskDetails`, attempts newest first |

```ts
type Track = {
  id: number; slug: string; titleEn: string; titleAr: string; descriptionAr: string;
  status: "Active" | "ComingSoon"; displayOrder: number; taskCount: number;
};
type TrackTask = {
  id: number; trackId: number; orderIndex: number; titleAr: string;
  descriptionAr: string; requirementsAr: string; taskType: "Daily" | "Weekly";
  durationHours: number; allowedSubmissionMode: "Files" | "GitHubOrFiles";
};
type Progress = {
  status: "Locked" | "Active" | "Completed"; unlockedAt: string | null;
  deadlineAt: string | null; completedAt: string | null; bestScore: number | null;
  attemptCount: number;
};
type FinalEvaluation = {
  overallScore: number | null; logic: number | null; security: number | null;
  codeQuality: number | null; compliance: number | null; recommendation: string | null;
};
type EnrollmentProgress = {
  id: number; trackId: number; status: "InProgress" | "Completed"; enrolledAt: string;
  completedAt: string | null; finalEvaluation: FinalEvaluation;
  tasks: (Progress & Pick<TrackTask, "orderIndex" | "titleAr" | "taskType" | "durationHours" | "allowedSubmissionMode">
    & {taskId: number; latestEvaluation: SavedReport | null})[];
};
type TaskDetails = Progress & {task: TrackTask; submissions: SavedReport[]};
```

Enrolling opens the first daily task for 24 hours. Passing opens the next task immediately. Its 24-hour or 168-hour window starts at unlock time. A late submission is accepted and its timing is available to the evaluator. A locked task can be inspected by its enrollee. Enrolling twice or enrolling in a coming-soon track returns 409. Unknown tracks and missing personal progress return 404.

## Submission and evaluation

| Method and path | Access | Request body | Response |
| --- | --- | --- | --- |
| POST `/api/tasks/{taskId}/submit-files` | Signed in, active task | Multipart, repeat the `files` field once per file | `SavedReport` |
| POST `/api/tasks/{taskId}/submit-github` | Signed in, active weekly task | `{"repoUrl":"https://github.com/OWNER/REPO","branch":"main"}` | `SavedReport` |
| GET `/api/submissions/{submissionId}` | Owner | None | `SavedReport` |

```ts
type Scores = {logic: number; security: number; codeQuality: number; compliance: number};
type Issue = {
  criterion: keyof Scores; severity: "low" | "medium" | "high" | "critical";
  file: string | null; line: number | null; explanation: string; suggestion: string;
};
type Evaluation = {
  overallScore: number; status: "Completed" | "Rejected"; scores: Scores;
  summary: string; strengths: string[]; issues: Issue[];
};
type SavedReport = Evaluation & {
  id: number; taskId: number; attemptNumber: number; submissionType: "Files" | "GitHub";
  gitHubRepoUrl: string | null; gitHubBranch: string | null; submittedAt: string; isOnTime: boolean;
};
```

For uploads, let the browser set the multipart boundary.

```js
const body = new FormData();
for (const file of selectedFiles) body.append("files", file);
const response = await fetch(`${apiBase}/api/tasks/${taskId}/submit-files`, {
  method: "POST", headers: { Authorization: `Bearer ${accessToken}` }, body
});
```

Upload 1–5 UTF-8 text files totaling at most 2 MiB, with distinct names and extensions `.cs`, `.js`, `.ts`, `.py`, `.sql`, `.json` or `.txt`. Weekly tasks also accept a public GitHub repository and exact branch. The response header `X-Submission-Truncated` indicates whether the source was partially reviewed (25,000 numbered characters maximum). The server reads code as text. The evaluation POST routes share a limit of five requests per IP per minute.

The server determines task requirements, receipt time and attempt number. The weighted score is rounded to two decimals (35% logic, 20% security, 25% code quality, 20% compliance). A score of at least 70 completes the task. Rejection is a successful HTTP response and leaves the task active. A retry receives only the same trainee's latest rejection issues for that task. Refresh progress after a successful submission. Completed tasks are closed. The final evaluation averages the three passing attempts, with each task weighted equally.

| HTTP status | Meaning for the client |
| --- | --- |
| 400 / 413 | Invalid body, unsupported source, private repository, invalid submission mode or request size. Check limits and correct the input. |
| 401 | Sign in with a valid credential or bearer token. |
| 404 | Unknown resource, missing enrollment, or report/PDF outside the current user's account. |
| 409 | Task or enrollment state precludes the operation, or another request committed first. Reload progress. |
| 429 | Evaluation rate limit reached. Retry in the next window. |
| 502 / 503 / 504 | Provider error, missing service configuration, or timeout. A failed evaluation does not consume an attempt. |

Validation errors can include `errors`. Problem Details responses can include `status`, `title`, and `detail`. Handle the HTTP status even when the body is empty.

## Certificates

| Method and path | Access | Request body | Response |
| --- | --- | --- | --- |
| GET `/api/certificates/my` | Signed in | None | `{certificate: Certificate, finalEvaluation: FinalEvaluation}[]` |
| GET `/api/certificates/{serialNumber}/pdf` | Owner | None | `application/pdf` attachment named `{serialNumber}.pdf` |
| GET `/api/certificates/verify/{serialNumber}` | Public | None | `{authentic: true, certificate: Certificate}`, or 404 |

```ts
type Certificate = {
  serialNumber: string; traineeName: string; trackTitleAr: string;
  finalOverallScore: number; issuedAt: string;
};
```

The final successful submission issues one certificate per enrollment. The eight-character suffix in `TMK-2026-XXXXXXXX` is generated by the server. Fetch the PDF with the bearer header and download the response as a Blob. The PDF includes the public verification URL. A person who has the serial can see the displayed name, track, score and issue date; use synthetic profiles for demonstrations.

## Development agent endpoints

Swagger UI is available at `/swagger` in Development. The two anonymous development POST routes return `Evaluation` without saving progress. They share the evaluation rate limiter. Their optional `attemptNumber` defaults to 1 and `dueAtUtc` to unknown. Task-specific rejection history belongs to authenticated submissions.

| Method and path | Content type | Body |
| --- | --- | --- |
| POST `/api/agent-test/evaluate-files` | multipart/form-data | `taskDescription`, repeated `files`, optional `attemptNumber` and `dueAtUtc` |
| POST `/api/agent-test/evaluate-github` | application/json | `{taskDescription,repoUrl,branch,attemptNumber?,dueAtUtc?}` |
