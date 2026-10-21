dotnet Build/Avalonia/BuildPatchedAvalonia.cs
if errorlevel 1 exit /b %errorlevel%
set "PATH=%ProgramFiles%\LLVM\bin;%PATH%"
dotnet publish Source/App -p:PublishProfile=win-x64
