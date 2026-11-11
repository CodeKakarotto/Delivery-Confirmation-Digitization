# Michael's Flowers – Digitized Delivery Confirmations

Capstone prototype (Business Challenge 3, Solution 3).

Drivers keep using paper delivery sheets. An employee scans each sheet, Tesseract OCR reads the fields, the system flags problems (missing info, duplicates, unknown order IDs), and the employee checks, corrects, and approves. Approved confirmations are saved with their scan and can be searched anytime.

## Ticket format

One delivery per sheet, fields in one row, left to right:

| Zone | Order ID | Deliver To | Total Products | Delivery Instructions (incl. "Received by" + signature) |
|---|---|---|---|---|

## Tech stack

- **Frontend:** React + TypeScript + Vite (hosted on Cloudflare)
- **Backend:** ASP.NET Core Web API (C#)
- **Database:** SQL Server Express (Windows Authentication, Entity Framework Core)
- **OCR:** Tesseract (command-line, runs on the host computer – scans never leave our systems)
- **Connection:** Cloudflare Tunnel from the host computer to the API

## Roles

| Role | Can do |
|---|---|
| Employee | Upload scans, review/correct/approve, search records |
| Manager | Everything an Employee can, plus resolve flagged records and void confirmations |
| Driver | Nothing in this app (keeps using paper) |

## Demo login accounts

Test accounts are created automatically when the backend first starts. They contain **fake data only**.

| Username | Role |
|---|---|
| `employee@demo.local` | Employee |
| `manager@demo.local` | Manager |
| `driver@demo.local` | Driver (blocked – shown a message instead) |

**Passwords are not stored in this repo** (the repo is public). The host computer sets one demo password in .NET User Secrets – see [SETUP.md](SETUP.md), section 3. Ask the team chat for it.

## Screens so far

| Screen | What it shows |
|---|---|
| **Scan delivery sheets** | Upload pictures of the paper sheets. Below it, the **Orders** box lists recently saved uploads (# · Zone · Order ID · Deliver to · Total products · Delivery instructions · Exceptions / notes) and updates by itself while sheets are being read. |
| **Check & approve** | Sheets waiting for review, oldest first. Open one to see the **scan beside the editable fields**, the problems found, and what the scan originally said. Answer **Signature present: yes/no**, add optional **Exceptions / notes**, then **Approve**, **Save changes**, **Send to manager** (note required) or **Reject** (reason optional). Approval is refused – with every reason listed – unless all fields are filled in, the order exists and isn't already approved, and the signature question is answered (a "No" needs a note). The approver is always the signed-in user; every action is in the audit log. |
| **See all scans** | Everything scanned in the last 12 weeks, grouped **by week**; click a week to see its **days**, click a day to see each scan with the **time it was recorded**, whether it was read (✓) or couldn't be read (✗), and its exceptions. |
| **Sheet viewer** | Click any scan in either list to see the **scanned paper sheet itself**, whether it is **Confirmed, Denied, or Waiting for review**, and everything read from it. |

The **Order ID** is never blank: it shows the ID, "Reading…", or a red **Missing**. The **Exceptions / notes** column shows delivery exceptions spotted in the instructions (Left at door, Left with neighbor, Nobody home, Refused, Returned to shop) and problems found (No signature, Order not found, Duplicate order, …). Notes typed on the Check & approve screen are also scanned for these exceptions.

## Database (SQL Server Express)

| Table | One row is… |
|---|---|
| `Orders` | An order the shop knows about (20 **fake** test orders `MF-10001`–`MF-10020` for now) |
| `ScanDocuments` | One uploaded scan file (stored on disk; the database keeps its location and fingerprint) |
| `OcrResults` | What Tesseract read from a scan, with a confidence score per field (never edited) |
| `DeliveryConfirmations` | The employee-checked version of a scan; status Pending / Approved / Rejected / Flagged / Voided |
| `ValidationFlags` | A problem to look at (missing field, duplicate, unknown order, no signature, …) |
| `AuditLog` | Who did what and when (field names only, never names/addresses) |

Rules the database enforces: one confirmation record per scan, one **approved** confirmation per order, unique order numbers, nothing permanently deleted, and simultaneous edits can't overwrite each other.

## Setup

See [SETUP.md](SETUP.md) to run this on a Windows computer.

## Status

- [x] Step 0 – Repository setup
- [x] Step 1 – Project setup (frontend, backend, tests)
- [x] Step 2 – Login and roles
- [x] Step 3 – Database design
- [x] Step 4 – Upload and scan storage (JPEG/PNG; PDF not supported yet)
- [x] Step 5 – Tesseract OCR (background reading; 10/10 fields correct on 10 fake tickets)
- [x] Step 6 – Validation flags (missing fields, unknown/duplicate/already-confirmed orders, bad product count, no signature, hard-to-read fields, unreadable sheets)
- [x] Step 7 – Check & approve screen (approve / reject / send to manager, typed Exceptions / notes)
- [ ] Step 8 – Search dashboard
- [ ] Step 9 – Exceptions list
- [ ] Step 10 – Cloudflare hosting and Tunnel
- [ ] Step 11 – Security tests and backups

## Project layout

```
backend/
  Confirmations.slnx                 .NET solution
  src/Confirmations.Api/             ASP.NET Core Web API
  tests/Confirmations.Api.Tests/     xUnit tests
frontend/                            React + TypeScript + Vite
```
