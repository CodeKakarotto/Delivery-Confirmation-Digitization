@echo off
rem Double-click to start the Delivery Confirmations prototype on this computer.
rem First-time setup (database connection, demo password) is in SETUP.md, section 3.
title Michael's Flowers - Delivery Confirmations
cd /d "%~dp0"

rem Already running? Just open it.
powershell -NoProfile -Command "try { Invoke-RestMethod http://localhost:5173/api/health -TimeoutSec 2 | Out-Null; exit 0 } catch { exit 1 }"
if not errorlevel 1 goto open

where dotnet >nul 2>nul
if errorlevel 1 goto nodotnet
where npm >nul 2>nul
if errorlevel 1 goto nonode

if exist "frontend\node_modules" goto start
echo Installing website packages - first time only, please wait...
pushd frontend
call npm install
popd

:start
echo Starting the app. Two windows will open: "Backend" and "Website". Keep them open.
start "Backend - keep open" /D "%~dp0backend" cmd /k dotnet run --project src/Confirmations.Api --launch-profile http
start "Website - keep open" /D "%~dp0frontend" cmd /k npm run dev

echo Waiting for the app to be ready...
powershell -NoProfile -Command "for ($i = 0; $i -lt 90; $i++) { try { Invoke-RestMethod http://localhost:5173/api/health -TimeoutSec 2 | Out-Null; exit 0 } catch { Start-Sleep 2 } }; exit 1"
if errorlevel 1 goto failed

:open
start "" http://localhost:5173
echo.
echo The app is open in your browser: http://localhost:5173
echo To stop it, close the "Backend" and "Website" windows.
echo.
pause
exit /b 0

:nodotnet
echo The .NET SDK is not installed. See SETUP.md, section 1.
pause
exit /b 1

:nonode
echo Node.js is not installed. See SETUP.md, section 1.
pause
exit /b 1

:failed
echo The app did not start. Look at the "Backend" and "Website" windows for the error,
echo and see the Troubleshooting table in SETUP.md.
pause
exit /b 1
