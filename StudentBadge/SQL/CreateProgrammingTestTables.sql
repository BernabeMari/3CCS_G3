-- Create ProgrammingTests table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'ProgrammingTests') AND type in (N'U'))
BEGIN
    -- First, check if the Teachers table exists
    IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'Teachers') AND type in (N'U'))
    BEGIN
        CREATE TABLE ProgrammingTests (
            TestId INT PRIMARY KEY IDENTITY(1,1),
            TeacherId NVARCHAR(50) NOT NULL,
            TestName NVARCHAR(100) NOT NULL,
            ProgrammingLanguage NVARCHAR(50) NOT NULL,
            Description NVARCHAR(MAX),
            CreatedDate DATETIME DEFAULT GETDATE(),
            LastUpdatedDate DATETIME,
            IsActive BIT DEFAULT 1,
            FOREIGN KEY (TeacherId) REFERENCES Teachers(TeacherId)
        );
        
        PRINT 'ProgrammingTests table created successfully with Teachers table reference.';
    END
    ELSE IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'Users') AND type in (N'U'))
    BEGIN
        -- If using the unified schema with Users table
        CREATE TABLE ProgrammingTests (
            TestId INT PRIMARY KEY IDENTITY(1,1),
            TeacherId NVARCHAR(50) NOT NULL,
            TestName NVARCHAR(100) NOT NULL,
            ProgrammingLanguage NVARCHAR(50) NOT NULL,
            Description NVARCHAR(MAX),
            CreatedDate DATETIME DEFAULT GETDATE(),
            LastUpdatedDate DATETIME,
            IsActive BIT DEFAULT 1,
            FOREIGN KEY (TeacherId) REFERENCES Users(UserId)
        );
        
        PRINT 'ProgrammingTests table created successfully with Users table reference.';
    END
    ELSE
    BEGIN
        -- If neither table exists, create without foreign key
        CREATE TABLE ProgrammingTests (
            TestId INT PRIMARY KEY IDENTITY(1,1),
            TeacherId NVARCHAR(50) NOT NULL,
            TestName NVARCHAR(100) NOT NULL,
            ProgrammingLanguage NVARCHAR(50) NOT NULL,
            Description NVARCHAR(MAX),
            CreatedDate DATETIME DEFAULT GETDATE(),
            LastUpdatedDate DATETIME,
            IsActive BIT DEFAULT 1
        );
        
        PRINT 'ProgrammingTests table created without foreign key constraint due to missing reference table.';
    END
END
ELSE
BEGIN
    PRINT 'ProgrammingTests table already exists. Skipping creation.';
END

-- Create ProgrammingQuestions table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'ProgrammingQuestions') AND type in (N'U'))
BEGIN
    CREATE TABLE ProgrammingQuestions (
        QuestionId INT PRIMARY KEY IDENTITY(1,1),
        TestId INT NOT NULL,
        QuestionText NVARCHAR(MAX) NOT NULL,
        AnswerText NVARCHAR(MAX),
        CodeSnippet NVARCHAR(MAX),
        Points INT DEFAULT 1,
        CreatedDate DATETIME DEFAULT GETDATE(),
        LastUpdatedDate DATETIME,
        FOREIGN KEY (TestId) REFERENCES ProgrammingTests(TestId) ON DELETE CASCADE
    );
    
    PRINT 'ProgrammingQuestions table created successfully.';
END
ELSE
BEGIN
    PRINT 'ProgrammingQuestions table already exists. Skipping creation.';
END 