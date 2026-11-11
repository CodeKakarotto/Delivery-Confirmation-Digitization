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

### Where uploaded scans are stored

Scans are saved (with hidden photo metadata such as GPS location removed) in a **private folder**:

```
%LOCALAPPDATA%\MichaelsFlowers\Scans
```

The folder is created automatically. It is never served directly to the internet – scans can only be viewed through the app by a signed-in Employee or Manager. To use a different folder:

```powershell
dotnet user-secrets set "ScanStorage:Folder" "D:\MichaelsFlowersData\Scans" --project src/Confirmations.Api
```

**Back up this folder together with the database** (Step 11 adds a backup script).

Upload rules: JPEG or PNG only (checked from the file's contents, not its name), one delivery sheet per file, up to 10 MB each.

### Reading sheets (Tesseract OCR)

Every uploaded sheet is read **automatically in the background** a few seconds after upload (uploads never wait for it). Scans left unread when the backend was off are read when it starts again. Tesseract runs on this computer only – scans are never sent anywhere.

The backend expects Tesseract at `C:\Program Files\Tesseract-OCR\tesseract.exe`. If it is installed somewhere else:

```powershell
dotnet user-secrets set "Ocr:TesseractPath" "D:\Tools\Tesseract-OCR\tesseract.exe" --project src/Confirmations.Api
```

The reader expects the ticket layout below (one delivery per sheet). The headings must be printed exactly like this; values go underneath:

| ZONE | ORDER ID | DELIVER TO | TOTAL PRODUCTS | DELIVERY INSTRUCTIONS |
|---|---|---|---|---|
| North | MF-10003 | Test Recipient 3<br>103 Example St | 4 | Leave at side door<br><br>Received by: *(signature on the line below)* |

What it reads is only a **suggestion**: it fills the Pending record, and a person must check and approve it. If a sheet can't be read, the record is marked "OCR failed" and still waits for manual entry.

## 4. Run the app locally

**Easiest:** double-click **`Start-App.bat`** in the repo folder. It opens two windows ("Backend" and "Website" – keep them open), waits until the app is ready, and opens http://localhost:5173 in your browser. To stop the app, close those two windows.

**By hand** (if you prefer): open **two** PowerShell windows in the repo folder.

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

## 6. Troubleshooting

| You see | What to do |
|---|---|
| `'dotnet' is not recognized` or `'npm' is not recognized` | The terminal was opened before .NET or Node.js was installed. **Close VS Code (or PowerShell) completely and open it again.** If it still happens, re-run the installer. |
| Page says **API status: unavailable** (red) | The backend isn't running. Start Window 1 (section 4). |
| Backend stops with `Connection string 'Confirmations' is not set` | Run the first command in section 3. |
| Sign-in always says **Incorrect email or password** | Run the `DemoUsers:Password` command in section 3, then restart the backend. |
| `Too many wrong passwords` | Wait 5 minutes and try again. |
| Every sheet is marked "OCR failed" | Check Tesseract is installed (section 1) and the path above is right, then restart the backend. |
| Backend says the port is already in use | The backend is already running in another window. Close that window or use it. |

## Security reminders

- Never commit passwords, connection strings, scans, or tunnel credentials.
- Only use fake tickets and fake orders for testing.
- Keep SQL Server on localhost only; expose only the API through Cloudflare Tunnel.
