@echo off
rem ============================================================
rem PlcRecipeStudio one-click publish script (ASCII only for cmd.exe)
rem Output: publish\WpfApp  (copy-and-run, self-contained)
rem         publish\Server  (API host, framework-dependent)
rem ============================================================
setlocal
cd /d "%~dp0.."

echo [1/3] Publishing WPF app (self-contained)...
dotnet publish PlcRecipe.WpfApp\PlcRecipe.WpfApp.csproj -c Release -r win-x64 --self-contained true -o publish\WpfApp || goto :fail

echo [2/3] Publishing API host (optional, for MES integration)...
dotnet publish PlcRecipe.Server\PlcRecipe.Server.csproj -c Release -o publish\Server || goto :fail

echo [3/3] Copying docs folder...
xcopy /y /i docs publish\WpfApp\docs >nul 2>&1

echo.
echo Done. Copy the whole publish\WpfApp folder to the target PC and run PlcRecipeStudio.exe
goto :eof

:fail
echo Publish FAILED.
exit /b 1
