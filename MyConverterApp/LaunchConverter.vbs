Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

' Skrypt sam sprawdza folder, w którym się znajduje
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)

WshShell.CurrentDirectory = scriptDir
WshShell.Run "dotnet run", 0, False