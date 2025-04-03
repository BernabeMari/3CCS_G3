-- Check if Username column exists and add it if it doesn't
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'Username')
BEGIN
    ALTER TABLE Students 
    ADD Username NVARCHAR(100) NULL;
    
    PRINT 'Username column added to Students table';
END
ELSE
BEGIN
    PRINT 'Username column already exists in Students table';
END

-- Check if Password column exists and add it if it doesn't
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'Password')
BEGIN
    ALTER TABLE Students 
    ADD Password NVARCHAR(100) NULL;
    
    PRINT 'Password column added to Students table';
END
ELSE
BEGIN
    PRINT 'Password column already exists in Students table';
END

-- Update existing records to have default Username and Password
-- Use IdNumber as default Username and a standard Password
UPDATE Students
SET Username = IdNumber, 
    Password = 'DefaultPassword123'
WHERE Username IS NULL;

-- Add a message for the administrator to change this later
PRINT 'All existing student records have been updated with default usernames and passwords.';
PRINT 'For security reasons, please advise students to change their default passwords.'; 