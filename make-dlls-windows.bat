@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make_dlls_windows.ps1"
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" exit /b %exitCode%
echo Complete. Check the DLLs in the MAUI projects before publishing.
exit /b 0
