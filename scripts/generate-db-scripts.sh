#!/bin/bash

# ProdControlAV Database Schema Deployment Script
# This script generates SQL migration scripts for manual database deployment

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
API_PROJECT_DIR="$PROJECT_DIR/src/ProdControlAV.API"
OUTPUT_DIR="$PROJECT_DIR/database-scripts"

echo "🔧 ProdControlAV Database Schema Script Generator"
echo "================================================"
echo "Project Directory: $PROJECT_DIR"
echo "API Project Directory: $API_PROJECT_DIR"
echo "Output Directory: $OUTPUT_DIR"
echo ""

# Ensure we're in the right directory
if [ ! -f "$API_PROJECT_DIR/ProdControlAV.API.csproj" ]; then
    echo "❌ Error: ProdControlAV.API project not found at $API_PROJECT_DIR"
    exit 1
fi

# Check if dotnet-ef is installed
if ! command -v dotnet-ef &> /dev/null; then
    echo "⚠️  dotnet-ef not found. Installing Entity Framework tools..."
    dotnet tool install --global dotnet-ef
    if [ $? -ne 0 ]; then
        echo "❌ Failed to install dotnet-ef tools"
        exit 1
    fi
fi

# Create output directory
mkdir -p "$OUTPUT_DIR"

echo "📝 Generating SQL migration scripts..."
echo ""

cd "$API_PROJECT_DIR"

# Generate SQL script for the complete schema
echo "🔄 Generating complete schema script (from scratch)..."

# Create a temporary appsettings file with a dummy connection string for script generation
TEMP_APPSETTINGS="$API_PROJECT_DIR/appsettings.ScriptGeneration.json"
cat > "$TEMP_APPSETTINGS" << 'EOF'
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=ProdControlAV;Integrated Security=true;TrustServerCertificate=true;"
  },
  "Database": {
    "Provider": "SqlServer"
  }
}
EOF

# Generate the SQL script using the temporary configuration
DOTNET_ENVIRONMENT=ScriptGeneration dotnet ef migrations script --output "$OUTPUT_DIR/01-complete-schema.sql" --verbose

# Clean up temporary file
rm -f "$TEMP_APPSETTINGS"

if [ $? -eq 0 ]; then
    echo "✅ Complete schema script generated: database-scripts/01-complete-schema.sql"
else
    echo "❌ Failed to generate complete schema script"
    exit 1
fi

# Generate SQL script for individual migrations (if there are multiple)
MIGRATION_COUNT=$(find Migrations -name "*.cs" -not -name "*Designer.cs" -not -name "*ModelSnapshot.cs" | wc -l)

if [ "$MIGRATION_COUNT" -gt 1 ]; then
    echo ""
    echo "🔄 Generating incremental migration scripts..."
    
    # List all migrations
    MIGRATIONS=($(ls Migrations/*.cs | grep -v Designer.cs | grep -v ModelSnapshot.cs | sort))
    
    for i in "${!MIGRATIONS[@]}"; do
        if [ $i -eq 0 ]; then
            continue # Skip first migration as it's already in complete schema
        fi
        
        PREV_MIGRATION=$(basename "${MIGRATIONS[$((i-1))]}" .cs)
        CURRENT_MIGRATION=$(basename "${MIGRATIONS[$i]}" .cs)
        
        echo "   Generating: $PREV_MIGRATION → $CURRENT_MIGRATION"
        dotnet ef migrations script "$PREV_MIGRATION" "$CURRENT_MIGRATION" \
            --output "$OUTPUT_DIR/$(printf "%02d" $((i+1)))-${CURRENT_MIGRATION}.sql"
    done
fi

echo ""
echo "📋 Generated SQL script files:"
echo "=============================="
ls -la "$OUTPUT_DIR"

echo ""
echo "📖 Usage Instructions:"
echo "======================"
echo "1. For a new database: Run '01-complete-schema.sql'"
echo "2. For existing database: Run the appropriate incremental scripts in order"
echo "3. Always backup your database before applying schema changes"
echo "4. Test scripts on a staging environment first"
echo ""
echo "🔗 Connection String Setup:"
echo "Set your SQL Server connection string in appsettings.Production.json:"
echo '  "ConnectionStrings": {'
echo '    "DefaultConnection": "Server=your-server;Database=your-db;User Id=your-user;Password=your-password;Encrypt=True;"'
echo '  }'
echo ""
echo "✅ Schema script generation completed successfully!"