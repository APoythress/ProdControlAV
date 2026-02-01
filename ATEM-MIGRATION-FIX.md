# ATEM Database Migration Fix

## Issue
After publishing changes from PR #159 (ATEM Support), the application fails with the following SQL error:
```
Invalid column name 'AtemEnabled'.
Invalid column name 'AtemTransitionDefaultRate'.
Invalid column name 'AtemTransitionDefaultType'.
```

## Root Cause
The ATEM support feature added three new properties to the `Device` model:
- `AtemEnabled` (bool?, nullable)
- `AtemTransitionDefaultRate` (int?, nullable)
- `AtemTransitionDefaultType` (string?, nullable)

However, no database migration was created to add these columns to the database schema, causing a mismatch between the code and database.

## Solution
A new Entity Framework migration has been created: `20260201001628_AddAtemDeviceFields`

This migration adds the missing columns to the `Devices` table, along with other properties that were also missing:
- `RecordingStatus` (bool?, nullable) - for Video device recording status
- Command table enhancements for status monitoring

## How to Apply the Fix

### Option 1: Using Entity Framework CLI (Recommended for Development)
```bash
cd src/ProdControlAV.API
dotnet ef database update
```

### Option 2: Generate and Apply SQL Scripts (Production Deployment)
```bash
# Generate migration scripts
./scripts/generate-db-scripts.sh  # Linux/macOS
# or
scripts\generate-db-scripts.bat   # Windows

# Apply the generated SQL script to your Azure SQL Database
# The script will be in: database-scripts/02-add-atem-fields.sql (or similar)
```

### Option 3: Azure SQL Database via Azure Portal
1. Generate the SQL script as shown in Option 2
2. Navigate to your Azure SQL Database in the Azure Portal
3. Open Query Editor
4. Paste and execute the generated SQL script

## Verification
After applying the migration, the application should start without errors and the ATEM features will be fully functional.

To verify the columns were added successfully:
```sql
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Devices' 
  AND COLUMN_NAME LIKE 'Atem%'
ORDER BY COLUMN_NAME;
```

Expected result:
```
COLUMN_NAME                    DATA_TYPE    IS_NULLABLE
AtemEnabled                    bit          YES
AtemTransitionDefaultRate      int          YES
AtemTransitionDefaultType      nvarchar     YES
```

## Migration Details
The migration file can be found at:
- `src/ProdControlAV.API/Migrations/20260201001628_AddAtemDeviceFields.cs`

This migration includes both `Up()` and `Down()` methods for safe forward and rollback operations.
