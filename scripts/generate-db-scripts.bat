@echo off
REM ProdControlAV Database Schema Deployment Script
REM This script generates SQL migration scripts for manual database deployment

setlocal EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%.."
set "API_PROJECT_DIR=%PROJECT_DIR%\src\ProdControlAV.API"
set "OUTPUT_DIR=%PROJECT_DIR%\database-scripts"

echo 🔧 ProdControlAV Database Schema Script Generator
echo ================================================
echo Project Directory: %PROJECT_DIR%
echo API Project Directory: %API_PROJECT_DIR%
echo Output Directory: %OUTPUT_DIR%
echo.

REM Ensure we're in the right directory
if not exist "%API_PROJECT_DIR%\ProdControlAV.API.csproj" (
    echo ❌ Error: ProdControlAV.API project not found at %API_PROJECT_DIR%
    exit /b 1
)

REM Check if dotnet-ef is installed
dotnet-ef --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ⚠️  dotnet-ef not found. Installing Entity Framework tools...
    dotnet tool install --global dotnet-ef
    if %errorlevel% neq 0 (
        echo ❌ Failed to install dotnet-ef tools
        exit /b 1
    )
)

REM Create output directory
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo 📝 Generating SQL migration scripts...
echo.

cd /d "%API_PROJECT_DIR%"

REM Generate SQL script for the complete schema
echo 🔄 Generating complete schema script (from scratch)...

REM Create a temporary appsettings file with a dummy connection string for script generation
echo {> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo   "ConnectionStrings": {>> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo     "DefaultConnection": "Server=localhost;Database=ProdControlAV;Integrated Security=true;TrustServerCertificate=true;">> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo   },>> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo   "Database": {>> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo     "Provider": "SqlServer">> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo   }>> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"
echo }>> "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"

REM Generate the SQL script using the temporary configuration
set "DOTNET_ENVIRONMENT=ScriptGeneration"
dotnet ef migrations script --output "%OUTPUT_DIR%\01-complete-schema.sql" --verbose

REM Clean up temporary file
if exist "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json" del "%API_PROJECT_DIR%\appsettings.ScriptGeneration.json"

if %errorlevel% equ 0 (
    echo ✅ Complete schema script generated: database-scripts\01-complete-schema.sql
) else (
    echo ❌ Failed to generate complete schema script
    exit /b 1
)

echo.
echo 📋 Generated SQL script files:
echo ==============================
dir "%OUTPUT_DIR%"

echo.
echo 📖 Usage Instructions:
echo ======================
echo 1. For a new database: Run '01-complete-schema.sql'
echo 2. For existing database: Run the appropriate incremental scripts in order
echo 3. Always backup your database before applying schema changes
echo 4. Test scripts on a staging environment first
echo.
echo 🔗 Connection String Setup:
echo Set your SQL Server connection string in appsettings.Production.json:
echo   "ConnectionStrings": {
echo     "DefaultConnection": "Server=your-server;Database=your-db;User Id=your-user;Password=your-password;Encrypt=True;"
echo   }
echo.
echo ✅ Schema script generation completed successfully!

pause