Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
baseDir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = fso.BuildPath(baseDir, "PendleChartViewer\bin\Release\net10.0-windows\PendleChartViewer.exe")

If fso.FileExists(exePath) Then
    shell.Run """" & exePath & """", 1, False
Else
    MsgBox "PendleChartViewer.exe not found. Build the VB.NET project first.", vbExclamation, "Pendle Chart Viewer"
End If
