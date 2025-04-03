-- Create the main Users table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'Users') AND type in (N'U'))
BEGIN
    CREATE TABLE Users (
        UserId NVARCHAR(50) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL UNIQUE,
        Password NVARCHAR(100) NOT NULL,
        FullName NVARCHAR(100) NOT NULL,
        Role NVARCHAR(20) NOT NULL, -- 'student', 'teacher', 'employer', 'admin'
        Email NVARCHAR(100),
        PhoneNumber NVARCHAR(20),
        IsActive BIT DEFAULT 1,
        CreatedAt DATETIME DEFAULT GETDATE(),
        LastLoginAt DATETIME,
        CONSTRAINT CHK_Role CHECK (Role IN ('student', 'teacher', 'employer', 'admin'))
    );
END
ELSE
BEGIN
    PRINT 'Table Users already exists. Skipping creation.';
END

-- Create StudentDetails table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'StudentDetails') AND type in (N'U'))
BEGIN
    CREATE TABLE StudentDetails (
        UserId NVARCHAR(50) PRIMARY KEY,
        IdNumber NVARCHAR(50) UNIQUE,
        Course NVARCHAR(50),
        Section NVARCHAR(50),
        Score INT DEFAULT 0,
        Achievements NVARCHAR(MAX),
        Comments NVARCHAR(MAX),
        BadgeColor NVARCHAR(20) DEFAULT 'green',
        IsProfileVisible BIT DEFAULT 1,
        IsResumeVisible BIT DEFAULT 1,
        ProfilePicturePath NVARCHAR(MAX),
        ResumeFileName NVARCHAR(255),
        FOREIGN KEY (UserId) REFERENCES Users(UserId)
    );
END
ELSE
BEGIN
    PRINT 'Table StudentDetails already exists. Skipping creation.';
END

-- Create TeacherDetails table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'TeacherDetails') AND type in (N'U'))
BEGIN
    CREATE TABLE TeacherDetails (
        UserId NVARCHAR(50) PRIMARY KEY,
        Department NVARCHAR(100),
        Position NVARCHAR(100),
        FOREIGN KEY (UserId) REFERENCES Users(UserId)
    );
END
ELSE
BEGIN
    PRINT 'Table TeacherDetails already exists. Skipping creation.';
END

-- Create EmployerDetails table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'EmployerDetails') AND type in (N'U'))
BEGIN
    CREATE TABLE EmployerDetails (
        UserId NVARCHAR(50) PRIMARY KEY,
        Company NVARCHAR(100),
        Address NVARCHAR(200),
        Description NVARCHAR(MAX),
        ProfilePicturePath NVARCHAR(MAX),
        FOREIGN KEY (UserId) REFERENCES Users(UserId)
    );
END
ELSE
BEGIN
    PRINT 'Table EmployerDetails already exists. Skipping creation.';
END

-- Create Messages table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'Messages') AND type in (N'U'))
BEGIN
    CREATE TABLE Messages (
        MessageId INT IDENTITY(1,1) PRIMARY KEY,
        SenderId NVARCHAR(50) NOT NULL,
        ReceiverId NVARCHAR(50) NOT NULL,
        MessageContent NVARCHAR(MAX) NOT NULL,
        SentTime DATETIME NOT NULL DEFAULT GETDATE(),
        IsFromEmployer BIT NOT NULL DEFAULT 0,
        IsRead BIT NOT NULL DEFAULT 0
    );
END
ELSE
BEGIN
    PRINT 'Table Messages already exists. Skipping creation.';
END

-- Create VideoCalls table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'VideoCalls') AND type in (N'U'))
BEGIN
    CREATE TABLE VideoCalls (
        CallId INT IDENTITY(1,1) PRIMARY KEY,
        EmployerId NVARCHAR(50) NOT NULL,
        StudentId NVARCHAR(50) NOT NULL,
        StartTime DATETIME NOT NULL DEFAULT GETDATE(),
        EndTime DATETIME NULL,
        Status NVARCHAR(20) NOT NULL DEFAULT 'requested'
    );
END
ELSE
BEGIN
    PRINT 'Table VideoCalls already exists. Skipping creation.';
END

-- Check if data migration is needed
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Students') AND
   NOT EXISTS (SELECT * FROM Users WHERE Role = 'student')
BEGIN
    PRINT 'Migrating student data...';
    -- Students
    INSERT INTO Users (UserId, Username, Password, FullName, Role, IsActive, CreatedAt)
    SELECT 
        IdNumber,
        Username,
        Password,
        FullName,
        'student',
        ISNULL(IsProfileVisible, 1),
        GETDATE()
    FROM Students;

    INSERT INTO StudentDetails (UserId, IdNumber, Course, Section, Score, Achievements, Comments, BadgeColor, IsProfileVisible, IsResumeVisible, ProfilePicturePath, ResumeFileName)
    SELECT 
        IdNumber,
        IdNumber,
        Course,
        Section,
        ISNULL(Score, 0),
        Achievements,
        Comments,
        ISNULL(BadgeColor, 'green'),
        ISNULL(IsProfileVisible, 1),
        ISNULL(IsResumeVisible, 1),
        ProfilePicturePath,
        ResumeFileName
    FROM Students;
    PRINT 'Student data migration complete.';
END
ELSE
BEGIN
    PRINT 'Student data already migrated or no students to migrate.';
END

-- Check if teacher migration is needed
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Teachers') AND
   NOT EXISTS (SELECT * FROM Users WHERE Role = 'teacher')
BEGIN
    PRINT 'Migrating teacher data...';
    -- Teachers
    INSERT INTO Users (UserId, Username, Password, FullName, Role, IsActive, CreatedAt)
    SELECT 
        TeacherId,
        Username,
        Password,
        FullName,
        'teacher',
        1,
        GETDATE()
    FROM Teachers;

    INSERT INTO TeacherDetails (UserId, Department, Position)
    SELECT 
        TeacherId,
        Department,
        Position
    FROM Teachers;
    PRINT 'Teacher data migration complete.';
END
ELSE
BEGIN
    PRINT 'Teacher data already migrated or no teachers to migrate.';
END

-- Check if employer migration is needed
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Employers') AND
   NOT EXISTS (SELECT * FROM Users WHERE Role = 'employer')
BEGIN
    PRINT 'Migrating employer data...';
    -- Employers
    INSERT INTO Users (UserId, Username, Password, FullName, Role, Email, PhoneNumber, IsActive, CreatedAt)
    SELECT 
        EmployerId,
        Username,
        Password,
        FullName,
        'employer',
        Email,
        PhoneNumber,
        ISNULL(IsActive, 1),
        GETDATE()
    FROM Employers;

    INSERT INTO EmployerDetails (UserId, Company, Address, Description, ProfilePicturePath)
    SELECT 
        EmployerId,
        Company,
        Address,
        Description,
        ProfilePicturePath
    FROM Employers;
    PRINT 'Employer data migration complete.';
END
ELSE
BEGIN
    PRINT 'Employer data already migrated or no employers to migrate.';
END

-- Check if admin migration is needed
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Admins') AND
   NOT EXISTS (SELECT * FROM Users WHERE Role = 'admin')
BEGIN
    PRINT 'Migrating admin data...';
    -- Admins
    INSERT INTO Users (UserId, Username, Password, FullName, Role, IsActive, CreatedAt)
    SELECT 
        'ADM' + CAST(ROW_NUMBER() OVER (ORDER BY Username) AS NVARCHAR(10)),
        Username,
        Password,
        FullName,
        'admin',
        1,
        GETDATE()
    FROM Admins;
    PRINT 'Admin data migration complete.';
END
ELSE
BEGIN
    PRINT 'Admin data already migrated or no admins to migrate.';
END

-- Update foreign key references in other tables
-- First, check if FK_Messages_Users exists
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'FK_Messages_Users') AND parent_object_id = OBJECT_ID(N'Messages'))
BEGIN
    -- First, drop existing foreign key constraints if they exist
    IF EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'FK_Messages_Students') AND parent_object_id = OBJECT_ID(N'Messages'))
    BEGIN
        ALTER TABLE Messages DROP CONSTRAINT FK_Messages_Students;
    END

    -- Then add new foreign key constraint
    ALTER TABLE Messages
    ADD CONSTRAINT FK_Messages_Users FOREIGN KEY (ReceiverId) REFERENCES Users(UserId);
    PRINT 'Updated FK_Messages_Users constraint.';
END
ELSE
BEGIN
    PRINT 'FK_Messages_Users constraint already exists.';
END

-- Check if FK_VideoCalls_Students exists
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'FK_VideoCalls_Students') AND parent_object_id = OBJECT_ID(N'VideoCalls'))
BEGIN
    -- First, drop existing foreign key constraints if they exist
    IF EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'FK_VideoCalls_Students') AND parent_object_id = OBJECT_ID(N'VideoCalls'))
    BEGIN
        ALTER TABLE VideoCalls DROP CONSTRAINT FK_VideoCalls_Students;
    END

    -- Then add new foreign key constraint
    ALTER TABLE VideoCalls
    ADD CONSTRAINT FK_VideoCalls_Students FOREIGN KEY (StudentId) REFERENCES Users(UserId);
    PRINT 'Updated FK_VideoCalls_Students constraint.';
END
ELSE
BEGIN
    PRINT 'FK_VideoCalls_Students constraint already exists.';
END

-- Check if FK_VideoCalls_Employers exists
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'FK_VideoCalls_Employers') AND parent_object_id = OBJECT_ID(N'VideoCalls'))
BEGIN
    -- First, drop existing foreign key constraints if they exist
    IF EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'FK_VideoCalls_Employers') AND parent_object_id = OBJECT_ID(N'VideoCalls'))
    BEGIN
        ALTER TABLE VideoCalls DROP CONSTRAINT FK_VideoCalls_Employers;
    END

    -- Then add new foreign key constraint
    ALTER TABLE VideoCalls
    ADD CONSTRAINT FK_VideoCalls_Employers FOREIGN KEY (EmployerId) REFERENCES Users(UserId);
    PRINT 'Updated FK_VideoCalls_Employers constraint.';
END
ELSE
BEGIN
    PRINT 'FK_VideoCalls_Employers constraint already exists.';
END

-- Create indexes for better performance if they don't exist
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_Username' AND object_id = OBJECT_ID('Users'))
BEGIN
    CREATE INDEX IX_Users_Username ON Users(Username);
    PRINT 'Created IX_Users_Username index.';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_Role' AND object_id = OBJECT_ID('Users'))
BEGIN
    CREATE INDEX IX_Users_Role ON Users(Role);
    PRINT 'Created IX_Users_Role index.';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StudentDetails_IdNumber' AND object_id = OBJECT_ID('StudentDetails'))
BEGIN
    CREATE INDEX IX_StudentDetails_IdNumber ON StudentDetails(IdNumber);
    PRINT 'Created IX_StudentDetails_IdNumber index.';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_EmployerDetails_Company' AND object_id = OBJECT_ID('EmployerDetails'))
BEGIN
    CREATE INDEX IX_EmployerDetails_Company ON EmployerDetails(Company);
    PRINT 'Created IX_EmployerDetails_Company index.';
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_TeacherDetails_Department' AND object_id = OBJECT_ID('TeacherDetails'))
BEGIN
    CREATE INDEX IX_TeacherDetails_Department ON TeacherDetails(Department);
    PRINT 'Created IX_TeacherDetails_Department index.';
END

PRINT 'Script completed successfully.';

-- Note: After verifying the data migration, you can drop the old tables
-- DROP TABLE Students;
-- DROP TABLE Teachers;
-- DROP TABLE Employers;
-- DROP TABLE Admins; 