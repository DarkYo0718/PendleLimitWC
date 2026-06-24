Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
ps1 = fso.BuildPath(scriptDir, "Start-PendleTrayMonitor.ps1")
cmd = "powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Chr(34) & ps1 & Chr(34)
shell.Run cmd, 0, False
