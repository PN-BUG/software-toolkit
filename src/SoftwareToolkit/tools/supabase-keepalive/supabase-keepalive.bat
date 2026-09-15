@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0SupabaseKeepAlive.ps1"
endlocal
