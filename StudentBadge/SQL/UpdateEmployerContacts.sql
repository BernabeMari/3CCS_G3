-- Script to ensure employer contact information is properly stored
-- This fixes the issue where employer accounts can't message students
PRINT 'Checking contact information in Users and EmployerDetails tables...'

-- Check if Users table exists
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
BEGIN
    -- Check for Email column in Users table, add it if missing
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'Email')
    BEGIN
        PRINT 'Adding Email column to Users table...'
        ALTER TABLE Users ADD Email NVARCHAR(100)
        PRINT 'Email column added successfully!'
    END
    
    -- Check for PhoneNumber column in Users table, add it if missing
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'PhoneNumber')
    BEGIN
        PRINT 'Adding PhoneNumber column to Users table...'
        ALTER TABLE Users ADD PhoneNumber NVARCHAR(20)
        PRINT 'PhoneNumber column added successfully!'
    END
END
ELSE
BEGIN
    PRINT 'Users table not found. Database schema may be incorrect.'
END

-- Check for EmployerDetails table in unified schema
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails')
BEGIN
    PRINT 'Found EmployerDetails table...'
    
    -- Create temp variables to track if columns were added
    DECLARE @EmailAdded BIT = 0
    DECLARE @PhoneAdded BIT = 0
    
    -- Check if Email column exists in EmployerDetails
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'EmployerDetails' AND COLUMN_NAME = 'Email')
    BEGIN
        PRINT 'Adding Email column to EmployerDetails table...'
        ALTER TABLE EmployerDetails ADD Email NVARCHAR(100)
        SET @EmailAdded = 1
        PRINT 'Email column added to EmployerDetails!'
    END
    
    -- Check if PhoneNumber column exists in EmployerDetails
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'EmployerDetails' AND COLUMN_NAME = 'PhoneNumber')
    BEGIN
        PRINT 'Adding PhoneNumber column to EmployerDetails table...'
        ALTER TABLE EmployerDetails ADD PhoneNumber NVARCHAR(20)
        SET @PhoneAdded = 1
        PRINT 'PhoneNumber column added to EmployerDetails!'
    END
    
    -- If we added both columns, we'll need to synchronize data
    IF (@EmailAdded = 1 OR @PhoneAdded = 1)
    BEGIN
        PRINT 'Synchronizing contact information between Users and EmployerDetails tables...'
        
        -- This is a separate script to run after the columns have been added
        -- Users should be the source of truth for contact information
        PRINT 'Please run the following UPDATE statement manually after this script completes:'
        PRINT 'UPDATE ed SET ed.Email = u.Email, ed.PhoneNumber = u.PhoneNumber'
        PRINT 'FROM EmployerDetails ed INNER JOIN Users u ON ed.UserId = u.UserId'
        PRINT 'WHERE u.Role = ''employer'' AND (u.Email IS NOT NULL OR u.PhoneNumber IS NOT NULL)'
    END
    ELSE
    BEGIN
        PRINT 'Contact information columns already exist in EmployerDetails table.'
    END
END
ELSE
BEGIN
    PRINT 'EmployerDetails table not found. The system may be using the old schema.'
END

PRINT 'Contact information check complete!' 