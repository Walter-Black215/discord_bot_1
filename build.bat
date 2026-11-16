@echo off
rem Build DiscordAnalyzer.exe with the C# compiler that is built into Windows (.NET Framework 4.8)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /out:"%~dp0DiscordAnalyzer.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll "%~dp0src\*.cs"
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see errors above.
  pause
  exit /b 1
)
echo BUILD OK: DiscordAnalyzer.exe
