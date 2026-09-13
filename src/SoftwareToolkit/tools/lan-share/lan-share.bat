@echo off
:: ============================================
::  lan-share.bat
::  LAN file sharing server launcher
::
::  Usage:
::    lan-share.bat                       (share current directory on default port 8088)
::    lan-share.bat D:\MyFolder           (share a specific directory)
::    lan-share.bat D:\MyFolder 9000      (specify directory and port)
::  Drag & drop a folder onto this file to share it.
:: ============================================

set "ARG1=%~1"
set "ARG2=%~2"

:: Build argument list dynamically
set "PS_ARGS=-ExecutionPolicy Bypass -File "%~dp0lan-share.ps1""

if not "%ARG1%"=="" (
    set "PS_ARGS=%PS_ARGS% -SharePath "%ARG1%""
)

:: Only pass -Port if arg2 is a valid number
if not "%ARG2%"=="" (
    echo %ARG2%| findstr /r "^[0-9][0-9]*$" >nul
    if not errorlevel 1 (
        set "PS_ARGS=%PS_ARGS% -Port %ARG2%"
    )
)

powershell %PS_ARGS%
