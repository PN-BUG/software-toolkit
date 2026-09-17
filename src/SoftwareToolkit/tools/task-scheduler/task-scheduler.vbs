Option Explicit

If WScript.Arguments.Named.Exists("validate") Then
    WScript.Echo "VALID"
    WScript.Quit 0
End If

Dim shell, fileSystem, scriptDirectory, scriptPath, arguments
Set shell = CreateObject("Shell.Application")
Set fileSystem = CreateObject("Scripting.FileSystemObject")

scriptDirectory = fileSystem.GetParentFolderName(WScript.ScriptFullName)
scriptPath = fileSystem.BuildPath(scriptDirectory, "task-scheduler.ps1")
arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptPath & """"

' runas requests elevation; window style 0 keeps the PowerShell console hidden.
shell.ShellExecute "powershell.exe", arguments, scriptDirectory, "runas", 0
