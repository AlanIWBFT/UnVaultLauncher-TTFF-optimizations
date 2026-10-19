dotnet Build/Avalonia/BuildPatchedAvalonia.cs
if errorlevel 1 exit /b %errorlevel%
dotnet publish Source/App -p:PublishProfile=win-x64
