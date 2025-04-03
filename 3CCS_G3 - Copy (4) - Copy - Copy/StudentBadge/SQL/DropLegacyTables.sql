-- Script to drop legacy tables after migration to the unified Users table structure
-- IMPORTANT: Only run this after verifying that the migration was successful!

-- Check for foreign key constraints and drop them first
DECLARE @sql NVARCHAR(MAX) = N'';

-- Find all foreign keys referencing the tables we want to drop
SELECT @sql += N'
ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + 
' DROP CONSTRAINT ' + QUOTENAME(fk.name) + ';'
FROM sys.foreign_keys AS fk
WHERE 
    OBJECT_NAME(fk.referenced_object_id) IN ('Students', 'Teachers', 'Employers', 'Admins');

-- Execute the generated SQL to drop foreign keys
IF LEN(@sql) > 0
BEGIN
    PRINT 'Dropping foreign key constraints...';
    EXEC sp_executesql @sql;
    PRINT 'Foreign key constraints dropped.';
END
ELSE
BEGIN
    PRINT 'No foreign key constraints found to drop.';
END

-- Now drop the tables
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Students')
BEGIN
    DROP TABLE dbo.Students;
    PRINT 'Dropped Students table.';
END
ELSE
BEGIN
    PRINT 'Students table does not exist or was already dropped.';
END

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Teachers')
BEGIN
    DROP TABLE dbo.Teachers;
    PRINT 'Dropped Teachers table.';
END
ELSE
BEGIN
    PRINT 'Teachers table does not exist or was already dropped.';
END

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Employers')
BEGIN
    DROP TABLE dbo.Employers;
    PRINT 'Dropped Employers table.';
END
ELSE
BEGIN
    PRINT 'Employers table does not exist or was already dropped.';
END

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Admins')
BEGIN
    DROP TABLE dbo.Admins;
    PRINT 'Dropped Admins table.';
END
ELSE
BEGIN
    PRINT 'Admins table does not exist or was already dropped.';
END

PRINT 'Legacy tables cleanup completed successfully.'; 