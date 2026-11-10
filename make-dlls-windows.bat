@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make_dlls_windows.ps1"
set "exitCode=%ERRORLEVEL%"
exit /b %exitCode%
