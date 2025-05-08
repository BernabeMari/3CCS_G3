-- Create ExtraCurricularActivities table to store extra-curricular activities
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'ExtraCurricularActivities') AND type in (N'U'))
BEGIN
    PRINT 'Creating ExtraCurricularActivities table...';
    
    CREATE TABLE ExtraCurricularActivities (
        ActivityId INT IDENTITY(1,1) PRIMARY KEY,
        StudentId NVARCHAR(50) NOT NULL,
        TeacherId NVARCHAR(50) NOT NULL,
        ActivityName NVARCHAR(200) NOT NULL,
        ActivityDescription NVARCHAR(MAX) NULL,
        ActivityCategory NVARCHAR(100) NULL,
        ActivityDate DATETIME NOT NULL,
        RecordedDate DATETIME NOT NULL DEFAULT GETDATE(),
        Score DECIMAL(5,2) NOT NULL DEFAULT 0,
        ProofImageData VARBINARY(MAX) NULL,
        ProofImageContentType NVARCHAR(100) NULL,
        IsVerified BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_ExtraCurricularActivities_StudentId FOREIGN KEY (StudentId) 
            REFERENCES Users(UserId),
        CONSTRAINT FK_ExtraCurricularActivities_TeacherId FOREIGN KEY (TeacherId) 
            REFERENCES Users(UserId)
    );
    
    -- Create indexes for better performance
    CREATE INDEX IX_ExtraCurricularActivities_StudentId ON ExtraCurricularActivities(StudentId);
    CREATE INDEX IX_ExtraCurricularActivities_TeacherId ON ExtraCurricularActivities(TeacherId);
    
    PRINT 'ExtraCurricularActivities table created successfully.';
END
ELSE
BEGIN
    PRINT 'Table ExtraCurricularActivities already exists. Skipping creation.';
END 