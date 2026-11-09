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

## 3. Run the app locally

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

Open http://localhost:5173. The page should say **API status: OK** (green). If it says **unavailable** (red), the backend in Window 1 isn't running.

## 4. Run the tests

```powershell
cd backend;  dotnet test
cd ..\frontend;  npm test
```

## 5. Database and demo password

_Added in Steps 2–3._

## Security reminders

- Never commit passwords, connection strings, scans, or tunnel credentials.
- Only use fake tickets and fake orders for testing.
- Keep SQL Server on localhost only; expose only the API through Cloudflare Tunnel.
