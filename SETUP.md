# Setup Guide (Host Computer)

For the teammate hosting the prototype. This guide grows with each build step.

**Requirement:** a Windows computer that stays on during testing and the demo.

## 1. Install prerequisites (all free)

| Tool | Version tested | Download |
|---|---|---|
| Git | 2.55 | https://git-scm.com/download/win |
| .NET SDK | 10.0 | https://dotnet.microsoft.com/download |
| Node.js (includes npm) | 24 LTS | https://nodejs.org |
| SQL Server Express | 2022 or later, instance name `SQLEXPRESS` | https://www.microsoft.com/sql-server/sql-server-downloads |
| sqlcmd | latest | https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility |
| Tesseract OCR | 5.x, English language data | https://github.com/UB-Mannheim/tesseract/wiki |

Notes:
- Install Tesseract to the default folder `C:\Program Files\Tesseract-OCR`. It does **not** need to be on PATH.
- During SQL Server Express setup, keep **Windows Authentication**.
- `cloudflared` is only needed for Step 10 (Cloudflare Tunnel).

Check everything is installed (PowerShell):

```powershell
git --version
dotnet --list-sdks
node --version
npm --version
sqlcmd -?
& "C:\Program Files\Tesseract-OCR\tesseract.exe" --version
Get-Service 'MSSQL$SQLEXPRESS'
```

## 2. Get the code

```powershell
git clone https://github.com/CodeKakarotto/Delivery-Confirmation-Digitization.git
cd Delivery-Confirmation-Digitization
```

## 3. Database connection and demo password (one time)

These are stored in **.NET User Secrets** on the host computer only – never in the repo.
Run from the repo folder:

```powershell
cd backend

# Where the database is (Windows Authentication – no password in it).
# Change SQLEXPRESS if your SQL Server instance has a different name.
dotnet user-secrets set "ConnectionStrings:Confirmations" "Server=localhost\SQLEXPRESS;Database=MichaelsFlowersConfirmations;Trusted_Connection=True;TrustServerCertificate=True" --project src/Confirmations.Api

# Password for ALL three demo accounts. Pick your own and share it in the team chat.
# Rules: at least 10 characters, with an uppercase letter, a lowercase letter, a number, and a symbol.
dotnet user-secrets set "DemoUsers:Password" "Your-Own-Demo-Passw0rd!" --project src/Confirmations.Api
```

Every time the backend starts it **creates or updates the database tables automatically**, adds 20 **fake** test orders (`MF-10001`–`MF-10020`) if there are no orders yet, and creates the demo accounts:

| Email | Role | Can use the app? |
|---|---|---|
| `employee@demo.local` | Employee | Yes |
| `manager@demo.local` | Manager | Yes (plus manager actions later) |
| `driver@demo.local` | Driver | No – shown "Driver accounts can't use the delivery confirmation app." |

To change the demo password later, run the `DemoUsers:Password` command again and restart the backend.

> Your Windows account needs permission to create a database in SQL Server Express. The account that installed SQL Server Express has it by default.

## 4. Run the app locally

Open **two** PowerShell windows in the repo folder.

**Window 1 – backend (API):**

```powershell
cd backend
dotnet run --project src/Confirmations.Api --launch-profile http
```

The API runs at http://localhost:5170. Check it: http://localhost:5170/api/health should show `{"status":"OK"}`.

**Window 2 – frontend:**

```powershell
cd frontend
npm install        # first time only
npm run dev
```

Open http://localhost:5173. You should see the **Sign in** form, and **API status: OK** (green) at the bottom. If it says **unavailable** (red), the backend in Window 1 isn't running.

Sign in with one of the demo accounts from section 3.

## 5. Run the tests

```powershell
cd backend;  dotnet test
cd ..\frontend;  npm test
```

Tests use a temporary in-memory database and their own test password, so they don't need SQL Server or User Secrets.

## Security reminders

- Never commit passwords, connection strings, scans, or tunnel credentials.
- Only use fake tickets and fake orders for testing.
- Keep SQL Server on localhost only; expose only the API through Cloudflare Tunnel.
