@echo off
setlocal
dotnet build PlcRecipeStudio.slnx -c Release || exit /b 1
dotnet test PlcRecipe.Tests -c Release --no-build || exit /b 1
dotnet pack PlcRecipe -c Release --no-build || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release-check.ps1"
