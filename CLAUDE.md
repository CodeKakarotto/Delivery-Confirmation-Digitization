## Project Context
- This is a university capstone application for Michael's Flowers.
- The current development focus is Business Challenge 3, Solution 3: Digitized Delivery Confirmations (OCR).
- Stack: React, TypeScript, Vite, ASP.NET Core Web API (C#), SQL Server Express, Tesseract OCR, and Cloudflare (hosting and Cloudflare Tunnel).
- Languages: TypeScript for the React frontend; C# for the ASP.NET Core backend. Do not write backend code in TypeScript/JavaScript.
- Hosting: the React frontend is hosted on Cloudflare alongside the other Michael's Flowers prototypes. The ASP.NET Core backend, SQL Server Express, and Tesseract run on a host computer and are reached through Cloudflare Tunnel (`cloudflared`).
- No Azure and no MySQL. The Assignment 8 proposal listed these, but the team replaced them.
- Two developers (Nicholas and Arifur) collaborate through GitHub feature branches and pull requests.
- Preserve the existing application and its functionality, including the other prototypes on Cloudflare.

## Communication and Planning
- Explain technical concepts and proposed changes in plain English.
- Before making code changes, inspect the relevant files, explain the plan, and wait for explicit approval ("go").
- Ask clarifying questions when requirements are unclear.
- Make small, focused changes rather than rewriting the application.
- Do not add unrelated features without approval.
- If prebuilt code is transferred into VS Code, read through it first, summarize what it does in plain English, and ask the developers questions about it so Nicholas and Arifur are on the same page before planning next steps (Yafet's guidance).
- Questions to ask before building:
  - Which branch is the source of truth? What is finished vs. experimental?
  - Which computer hosts ASP.NET Core and SQL Server Express? Will it stay on during testing and the sponsor demo?
  - Which Cloudflare account and domain are the other prototypes using? What address should the frontend and API use?
  - Should SQL Server use Windows Authentication or a SQL login? Entity Framework Core or Dapper?
  - Run Tesseract through a .NET wrapper (NuGet package) or the command-line program?
  - Login: Cloudflare Access in front of all prototypes, ASP.NET Core Identity, or something else? Is a Manager role needed?
  - Where is each field located on the ticket, and what are the formats (e.g., `order_id`, zone)? Is there a blank ticket template for fake test scans?
  - Which fields are required? Is there a fixed list of valid zones?
  - Should `order_id` be matched against existing order records? Where do those records live?

## Security and Costs
- Never hardcode or commit passwords, tokens, API keys, connection strings, or Cloudflare Tunnel credentials.
- Keep secrets in .NET User Secrets or environment variables on the host computer, and in secure Cloudflare settings. Any `appsettings.*.json` or tunnel credential file containing secrets must be in `.gitignore`.
- Never display actual secret values in responses or logs.
- Ask before any deployment, paid API operation, or action that could incur costs. Cloudflare Tunnel, SQL Server Express, and Tesseract are free; ask before using any paid Cloudflare feature.
- Never use real customer information, tickets, or signatures for testing without explicit authorization.
- No paid or third-party OCR services. Tesseract runs on the host computer, so scans never leave our own systems.

**Cloudflare Tunnel**
- Expose only the ASP.NET Core API through the tunnel. Never expose SQL Server (port 1433), Remote Desktop, or the scan folder.
- The host computer opens no inbound ports; all traffic comes through the tunnel.
- Optional, ask first: protect the frontend and API with Cloudflare Access (staff-only login layer shared with the other prototypes).

**SQL Server Express**
- Keep SQL Server listening on localhost only.
- Disable the `sa` account or give it a strong password.
- The application uses a least-privilege database login that can only read and write its own database.
- Use parameterized queries only (EF Core LINQ or Dapper/`SqlParameter`). Never build SQL by joining strings, and never use `FromSqlRaw` with user input.
- Limits: 10 GB per database (store scans on disk, not in the database); no SQL Server Agent (back up with a `sqlcmd` `BACKUP DATABASE` script run by Windows Task Scheduler); no Transparent Data Encryption (use BitLocker on the host computer).

**Application**
- Treat OCR text as untrusted input: validate every field on the server and display it as plain text.
- Uploads: check the real file type (not just the extension), enforce size and page limits, use random file names, store scans in a private folder outside any publicly served folder, serve them only through an authorized API endpoint, and strip EXIF metadata.
- Never log deliver-to names/addresses, signatures, or delivery instructions.
- Use server timestamps, an audit log (who uploaded, edited, approved, voided, and when), and soft delete only.
- HTTPS only (Cloudflare handles public HTTPS); CORS limited to the frontend's Cloudflare address; generic error messages (no stack traces or SQL errors).
- Back up both the database and the scan folder to a second location, and test a restore.
- Optional, ask first: rate limiting and malware scanning of uploads.

## Development and Testing
- Work within the existing project structure.
- Do not change authentication, database schemas, or API contracts without approval.
- Run relevant tests after approved changes when possible.
- Explain errors and test results clearly; do not claim tests passed unless they ran successfully.
- Do not delete files or overwrite substantial existing work without approval.
- Create database tables only through reviewed Entity Framework migrations or SQL scripts, and only after approval.

## GitHub Collaboration
- Use separate feature branches for development (e.g., `feature/ocr-extraction`).
- Do not commit, push, merge, or deploy without explicit approval.
- Do not modify the main branch directly.
- Review changed files before proposing a commit.
- Never commit real customer scans, signatures, OCR output, or tunnel credentials.

## End-of-Session Summary
- Summarize completed changes in plain English.
- List modified files, tests run and their results, unresolved issues, and recommended next steps.
- State whether changes are only local, committed, pushed to GitHub, or deployed to Cloudflare.

## Digitized Delivery Confirmations

### Project Context
The Michael's Flowers application will eventually integrate three prototypes into one system:
1. TSP-Based Route Optimization
2. Delivery Status Tracking
3. Delivery Confirmation Management (Digitized Confirmations)

The current development phase focuses exclusively on Digitized Confirmations.

Do not implement functionality belonging to the other two prototypes unless explicitly requested. Out of scope: routing/maps/TSP, live delivery status or GPS, a driver mobile app, and automatic FTD/Teleflora/1-800-Flowers integration. Do not modify the other prototypes or their Cloudflare setup.

**Background (Assignment 8)**
- Today, drivers collect customer signatures and write the delivery time on a paper delivery sheet. If no one is home, the driver leaves the flowers at the door or with a neighbor and writes this exception on the sheet.
- After deliveries, drivers bring the sheets back to the shop. Processing staff check them against the route manifest and manually type each confirmation into FTD, Teleflora, or 1-800-Flowers.
- The shop handles about 30–50 delivery confirmations per day. The end-of-day confirmation process takes about 20 minutes when there are no major issues.
- Current time costs: recording signature and time (about 3 min per delivery, driver); driving back to the shop (about 25 min per trip, driver); checking sheets against the manifest (about 5 min, staff); entering each confirmation into FTD/Teleflora/1-800-Flowers (about 3 min per delivery, staff).
- Estimated total: $8,811.84 per month ($2,410.85 labor and $6,400.99 equipment), or about $4.84 per order.
- Confirmation information is not always available right away, so employees sometimes search paper records or call drivers to verify a delivery.

**Solution 3: Digitized Confirmations**
- Goal: keep the paper confirmation process that drivers prefer, but remove most of the manual typing by employees.
- Why the sponsor liked it (Sponsor Meeting 4, October 29, 2026): some drivers want to keep using paper. This digitizes records without forcing drivers onto a mobile app, and makes delivery records searchable.
- Innovative approaches: drivers keep the paper process; OCR reads ticket fields from scanned sheets; employees review, correct, and save the information.

**User Workflow**
1. The driver fills out the paper delivery sheet as usual.
2. An employee scans the sheets; OCR extracts the ticket fields and the system flags problems (missing info, duplicates, unknown order IDs).
3. The employee checks the fields against the scan, corrects them, and approves.
4. The confirmation is saved with a unique ID and its scan, searchable anytime. Managers handle flagged records.

**Acceptance Criteria**
- Sponsor: easier to record and find delivery confirmations; fewer missing and duplicate records; employees can still use paper delivery sheets.
- Team: a digital database for confirmations; checks for missing information and duplicates; a dashboard to search delivery records; tests confirming information is saved correctly.
- Every feature must trace back to at least one of these criteria.

### User Roles

The application will support Employee and Driver roles, plus a Manager role only if approved.

**Employee Permissions**
- Access the confirmation dashboard.
- Upload scanned delivery tickets.
- Review, correct, and approve OCR-extracted confirmations.
- Search and view confirmation records and their scans.
- Cannot permanently delete confirmations or scans.

**Manager Permissions** (only if approved)
- All employee permissions.
- Review and resolve flagged records with a required note.
- Void a confirmation with a reason (the original stays in the audit history).

**Driver Permissions**
- Continue using paper delivery sheets.
- Cannot access confirmation screens, scans, or search.
- Driver accounts must be blocked from all confirmation endpoints in the backend.

### Delivery Confirmation Requirements
- Employees upload JPEG/PNG/PDF scans in batches through the ASP.NET Core API. Each file gets a document ID, uploader, and server timestamp, and is saved to a private folder on the host computer. SQL Server stores only the file path, hash, and metadata.
- Tesseract OCR runs in the ASP.NET Core backend. OCR results are suggestions only; the backend validates everything before saving.
- OCR must extract these ticket fields:
  - `zone`: delivery zone printed on the ticket.
  - `order_id`: unique order ID, used to link and de-duplicate confirmations.
  - `deliver_to`: recipient name and/or delivery address.
  - `total_products`: number of products on the order (whole number).
  - `delivery_instructions`: delivery instructions or notes on the ticket.
  - `delivery_signature`: OCR cannot read signatures. Detect whether the signature area has a mark, and have the employee confirm "signature present: yes/no." Never use signatures to identify people.
- Save raw OCR text and per-field confidence scores. Do not add other fields without approval.
- Validation produces flags for review, never automatic decisions: missing required fields, `order_id` not found or already confirmed, re-uploaded file (file hash), `total_products` not a positive whole number, unknown `zone` (if a zone list exists), missing signature, and low-confidence fields.
- The review screen shows the scan beside the editable fields, with approve, reject, or send to manager. Keep both original OCR values and corrections.
- Nothing becomes a confirmation without a person approving it. If OCR fails, the record still goes to review for manual entry.
- Approved confirmations persist in SQL Server Express with a unique ID, linked to the `order_id` and the original scan. Scans are never overwritten. One active confirmation per `order_id`.
- Confirmations must reference actual user accounts and orders, not just names.
- The search dashboard filters by `order_id`, deliver-to, zone, or scan date range, with status (pending, approved, rejected, flagged).
- The exceptions list shows missing info, duplicate `order_id`s, missing signatures, and unreadable scans.

### Authentication and Authorization
- Reuse existing authentication where possible; do not introduce a new authentication provider without explicit approval. Azure AD/Entra ID is not allowed.
- If the backend needs its own login, propose an approach (e.g., Cloudflare Access, or ASP.NET Core Identity stored in SQL Server Express with hashed passwords) and explain it before building.
- Enforce role-based access control in the ASP.NET Core backend (`[Authorize]` policies).
- Do not rely solely on hiding frontend buttons or pages to restrict access.
- Validate the authenticated user's identity and role before returning protected data, including scans.
- Prevent users from accessing records or scans they are not allowed to see by modifying API requests.
- Never trust a client-provided user ID as proof of identity; the approving user comes from the server session.

### Development Guidelines
- Inspect existing implementations before adding new components, database tables, or API endpoints.
- Reuse existing users, roles, and order records where possible.
- Keep the approved architecture: React on Cloudflare, ASP.NET Core + SQL Server Express + Tesseract on the host computer, connected by Cloudflare Tunnel.
- Avoid unnecessary dependencies or architectural changes. Ask before installing Tesseract, NuGet or npm packages, `cloudflared`, or SQL Server components, and before creating SQL logins, Windows scheduled tasks, firewall rules, or tunnel routes.
- Keep frontend and backend changes minimal and consistent with existing patterns.
- Keep screens simple so employee training stays short (sponsor wants "little training").
- Add or update tests for new functionality.
- Test authorization boundaries: unauthenticated and driver requests are rejected; employees can't use manager-only actions; changing IDs doesn't leak data; client-supplied user IDs are ignored; scans can't be fetched without authorization; OCR text containing script tags displays safely; disguised or oversized files are rejected; SQL Server is not reachable through the tunnel.
- Do not modify existing files, database schemas, or configurations without explicit approval.
- Present a proposed implementation plan before making changes.
- Suggested build order: database design → upload and scan storage → Tesseract OCR on fake tickets → validation → review screen → search dashboard → exceptions list → Cloudflare hosting and Tunnel setup → security tests.

**Training (for the final deliverable)**
- Team: Tesseract OCR, React, ASP.NET Core, SQL Server Express, Cloudflare Tunnel, and document processing workflows.
- Employees: scanning/uploading tickets and reviewing/correcting extracted data.
- Managers: reviewing exceptions, duplicates, and missing-information reports.

## Test-Driven Development (TDD)

Follow the Red-Green-Refactor development cycle for new features, bug fixes, and changes to existing behavior.

### RED — Write Failing Tests
- Identify the expected behavior before implementing changes.
- Write automated tests that verify the expected behavior.
- Run the tests and confirm they fail for the intended reason.
- Do not modify production code during this phase.

### GREEN — Implement Minimum Changes
- Make only the changes necessary to pass the failing tests.
- Preserve existing functionality and architecture.
- Run the relevant tests until they pass.

### REFACTOR — Improve and Verify
- Improve code quality only when necessary.
- Keep all tests passing.
- Run relevant regression tests, TypeScript checks, frontend builds, and `dotnet build` / `dotnet test`.
- Report the final test results.

### Testing OCR
- Mock Tesseract output in unit tests.
- Use fake tickets (clean, blurry, rotated, missing field, no signature, duplicate `order_id`) for integration tests.
- Do not require perfect OCR; test that bad results get flagged.
- Report accuracy per field in plain English (e.g., "`order_id` read correctly 9/10").

### Human-in-the-Loop (HITL)
- Inspect existing code before proposing changes.
- Present an implementation plan and request explicit approval before modifying files.
- Request approval before database migrations, dependency installations, destructive commands, commits, pushes, or deployments.
- Explain unexpected failures and proposed changes before proceeding outside the approved scope.
- Never bypass failing tests simply to make the test suite pass.
- Do not modify or weaken tests without explaining why and obtaining approval.
- Report which tests were added, which initially failed, and which passed after implementation.

### Existing Functionality
- Do not rewrite existing working features solely to introduce TDD.
- Apply TDD to new development and bug fixes.
- Reuse the project's existing test frameworks and conventions whenever possible.
