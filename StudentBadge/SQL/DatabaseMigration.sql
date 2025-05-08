-- Database Migration Script
-- Script to migrate data from old tables to new structure

-- First, check if new tables already exist
IF OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
    -- Create Users table
    CREATE TABLE [dbo].[Users] (
        [UserId] NVARCHAR(50) PRIMARY KEY,
        [Username] NVARCHAR(100) NOT NULL,
        [Password] NVARCHAR(100) NOT NULL,
        [FullName] NVARCHAR(100) NOT NULL,
        [Role] NVARCHAR(20) NOT NULL, -- 'student', 'employer', 'admin', 'teacher'
        [CreatedAt] DATETIME DEFAULT GETDATE()
    );
    
    PRINT 'Users table created';
END
ELSE
BEGIN
    PRINT 'Users table already exists';
END

-- Check if StudentDetails table exists
IF OBJECT_ID('dbo.StudentDetails', 'U') IS NULL
BEGIN
    -- Create StudentDetails table
    CREATE TABLE [dbo].[StudentDetails] (
        [StudentDetailsId] INT IDENTITY(1,1) PRIMARY KEY,
        [UserId] NVARCHAR(50) NOT NULL,
        [IdNumber] NVARCHAR(50) NOT NULL UNIQUE,
        [Course] NVARCHAR(100),
        [Section] NVARCHAR(50),
        [Score] INT DEFAULT 0,
        [Achievements] NVARCHAR(MAX),
        [Comments] NVARCHAR(MAX),
        [BadgeColor] NVARCHAR(50) DEFAULT 'green',
        [IsProfileVisible] BIT DEFAULT 1,
        [IsResumeVisible] BIT DEFAULT 1,
        [ProfilePicturePath] NVARCHAR(MAX),
        [ResumeFileName] NVARCHAR(MAX),
        [OriginalResumeFileName] NVARCHAR(255),
        FOREIGN KEY ([UserId]) REFERENCES [Users]([UserId])
    );
    
    PRINT 'StudentDetails table created';
END
ELSE
BEGIN
    PRINT 'StudentDetails table already exists';
END

-- Check if EmployerDetails table exists
IF OBJECT_ID('dbo.EmployerDetails', 'U') IS NULL
BEGIN
    -- Create EmployerDetails table
    CREATE TABLE [dbo].[EmployerDetails] (
        [EmployerDetailsId] INT IDENTITY(1,1) PRIMARY KEY,
        [UserId] NVARCHAR(50) NOT NULL,
        [Company] NVARCHAR(100),
        [Email] NVARCHAR(100),
        [PhoneNumber] NVARCHAR(50),
        [Description] NVARCHAR(MAX),
        [ProfilePicturePath] NVARCHAR(MAX),
        FOREIGN KEY ([UserId]) REFERENCES [Users]([UserId])
    );
    
    PRINT 'EmployerDetails table created';
END
ELSE
BEGIN
    PRINT 'EmployerDetails table already exists';
END

-- Check if EmployerStudentMessages table exists
IF OBJECT_ID('dbo.EmployerStudentMessages', 'U') IS NULL
BEGIN
    -- Create EmployerStudentMessages table
    CREATE TABLE [dbo].[EmployerStudentMessages] (
        [MessageId] INT IDENTITY(1,1) PRIMARY KEY,
        [EmployerId] NVARCHAR(50) NOT NULL,
        [StudentId] NVARCHAR(50) NOT NULL,
        [Message] NVARCHAR(MAX) NOT NULL,
        [IsFromEmployer] BIT NOT NULL,
        [SentTime] DATETIME NOT NULL,
        [IsRead] BIT NOT NULL DEFAULT 0
    );
    
    PRINT 'EmployerStudentMessages table created';
END
ELSE
BEGIN
    PRINT 'EmployerStudentMessages table already exists';
END

-- Migrate data from old tables if they exist
-- Migrate Students to Users and StudentDetails
IF OBJECT_ID('dbo.Students', 'U') IS NOT NULL AND OBJECT_ID('dbo.Users', 'U') IS NOT NULL
BEGIN
    -- Check if migration has already been done
    IF NOT EXISTS (SELECT * FROM Users WHERE Role = 'student')
    BEGIN
        -- Insert students into Users table
        INSERT INTO Users (UserId, Username, Password, FullName, Role)
        SELECT IdNumber, Username, Password, FullName, 'student'
        FROM Students;
        
        -- Insert student details into StudentDetails table
        INSERT INTO StudentDetails (UserId, IdNumber, Course, Section, Score, Achievements, Comments, 
                                   BadgeColor, IsProfileVisible, IsResumeVisible, ProfilePicturePath, ResumeFileName)
        SELECT IdNumber, IdNumber, Course, Section, Score, Achievements, Comments, 
               BadgeColor, IsProfileVisible, IsResumeVisible, ProfilePicturePath, ResumeFileName
        FROM Students;
        
        PRINT 'Students data migrated to new tables';
    END
    ELSE
    BEGIN
        PRINT 'Student data already migrated';
    END
END

-- Migrate Employers to Users and EmployerDetails
IF OBJECT_ID('dbo.Employers', 'U') IS NOT NULL AND OBJECT_ID('dbo.Users', 'U') IS NOT NULL
BEGIN
    -- Check if migration has already been done
    IF NOT EXISTS (SELECT * FROM Users WHERE Role = 'employer')
    BEGIN
        -- Insert employers into Users table
        INSERT INTO Users (UserId, Username, Password, FullName, Role)
        SELECT EmployerId, Username, Password, FullName, 'employer'
        FROM Employers;
        
        -- Insert employer details into EmployerDetails table
        INSERT INTO EmployerDetails (UserId, Company, Email, PhoneNumber, Description, ProfilePicturePath)
        SELECT EmployerId, Company, Email, PhoneNumber, Description, ProfilePicturePath
        FROM Employers;
        
        PRINT 'Employers data migrated to new tables';
    END
    ELSE
    BEGIN
        PRINT 'Employer data already migrated';
    END
END

-- Migrate messages if old table exists
IF OBJECT_ID('dbo.Messages', 'U') IS NOT NULL AND OBJECT_ID('dbo.EmployerStudentMessages', 'U') IS NOT NULL
BEGIN
    -- Check if migration has already been done
    IF NOT EXISTS (SELECT TOP 1 * FROM EmployerStudentMessages)
    BEGIN
        -- Insert messages into new table
        INSERT INTO EmployerStudentMessages (EmployerId, StudentId, Message, IsFromEmployer, SentTime, IsRead)
        SELECT EmployerId, StudentId, Message, IsFromEmployer, SentTime, IsRead
        FROM Messages;
        
        PRINT 'Messages data migrated to new table';
    END
    ELSE
    BEGIN
        PRINT 'Message data already migrated';
    END
END

PRINT 'Migration completed successfully!'; 