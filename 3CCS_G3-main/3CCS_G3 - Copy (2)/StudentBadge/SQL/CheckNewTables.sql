-- Script to check for new tables in the database
SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;

-- Check structure of StudentDetails table
IF OBJECT_ID('dbo.StudentDetails', 'U') IS NOT NULL
BEGIN
    PRINT 'StudentDetails table exists';
    EXEC sp_columns 'StudentDetails';
END
ELSE
BEGIN
    PRINT 'StudentDetails table does not exist';
END

-- Check structure of EmployerStudentMessages table
IF OBJECT_ID('dbo.EmployerStudentMessages', 'U') IS NOT NULL
BEGIN
    PRINT 'EmployerStudentMessages table exists';
    EXEC sp_columns 'EmployerStudentMessages';
END
ELSE
BEGIN
    PRINT 'EmployerStudentMessages table does not exist';
END

-- Check structure of Employers table
IF OBJECT_ID('dbo.Employers', 'U') IS NOT NULL
BEGIN
    PRINT 'Employers table exists';
    EXEC sp_columns 'Employers';
END
ELSE
BEGIN
    PRINT 'Employers table does not exist';
END 