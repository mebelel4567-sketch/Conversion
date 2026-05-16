Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "cmd.exe /c cd ""C:\Users\Bartek\MyConverterApp\MyConverterApp"" && dotnet run", 0, False