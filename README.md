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

## Setup

See [SETUP.md](SETUP.md) to run this on a Windows computer.

## Status

- [x] Step 0 – Repository setup
- [x] Step 1 – Project setup (frontend, backend, tests)

## Project layout

```
backend/
  Confirmations.slnx                 .NET solution
  src/Confirmations.Api/             ASP.NET Core Web API
  tests/Confirmations.Api.Tests/     xUnit tests
frontend/                            React + TypeScript + Vite
```
- [x] Step 2 – Login and roles
- [ ] Step 3 – Database design
- [ ] Step 4 – Upload and scan storage
- [ ] Step 5 – Tesseract OCR
- [ ] Step 6 – Validation flags
- [ ] Step 7 – Review screen
- [ ] Step 8 – Search dashboard
- [ ] Step 9 – Exceptions list
- [ ] Step 10 – Cloudflare hosting and Tunnel
- [ ] Step 11 – Security tests and backups
