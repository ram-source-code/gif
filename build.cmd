@echo off
rem Builds GifWinLive.exe with the C# compiler that ships inside Windows
rem (.NET Framework 4.x). No Visual Studio or SDK needed.
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if not exist "%FW%\csc.exe" (
    echo .NET Framework 4 compiler not found. It is part of every Windows 10/11 install.
    exit /b 1
)
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:GifWinLive.exe ^
  /win32icon:src\app.ico /win32manifest:src\app.manifest ^
  /lib:"%FW%\WPF" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Built %CD%\GifWinLive.exe
