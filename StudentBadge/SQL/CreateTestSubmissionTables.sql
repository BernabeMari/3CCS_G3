-- Create TestSubmissions table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'TestSubmissions') AND type in (N'U'))
BEGIN
    CREATE TABLE TestSubmissions (
        SubmissionId INT PRIMARY KEY IDENTITY(1,1),
        TestId INT NOT NULL,
        StudentId NVARCHAR(50) NOT NULL,
        SubmissionDate DATETIME DEFAULT GETDATE(),
        IsGraded BIT DEFAULT 0,
        FOREIGN KEY (TestId) REFERENCES ProgrammingTests(TestId) ON DELETE CASCADE
    );
    
    PRINT 'TestSubmissions table created successfully.';
END
ELSE
BEGIN
    -- Check if IsGraded column exists, add it if not
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'TestSubmissions') AND name = 'IsGraded')
    BEGIN
        ALTER TABLE TestSubmissions ADD IsGraded BIT DEFAULT 0;
        PRINT 'IsGraded column added to TestSubmissions table.';
    END
    ELSE
    BEGIN
        PRINT 'IsGraded column already exists in TestSubmissions table.';
    END
    
    PRINT 'TestSubmissions table already exists. Checked for IsGraded column.';
END

-- Create TestAnswers table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'TestAnswers') AND type in (N'U'))
BEGIN
    CREATE TABLE TestAnswers (
        AnswerId INT PRIMARY KEY IDENTITY(1,1),
        SubmissionId INT NOT NULL,
        QuestionId INT NOT NULL,
        AnswerText NVARCHAR(MAX),
        Points INT DEFAULT 0,
        FOREIGN KEY (SubmissionId) REFERENCES TestSubmissions(SubmissionId) ON DELETE CASCADE,
        FOREIGN KEY (QuestionId) REFERENCES ProgrammingQuestions(QuestionId)
    );
    
    PRINT 'TestAnswers table created successfully.';
END
ELSE
BEGIN
    PRINT 'TestAnswers table already exists. Skipping creation.';
END 