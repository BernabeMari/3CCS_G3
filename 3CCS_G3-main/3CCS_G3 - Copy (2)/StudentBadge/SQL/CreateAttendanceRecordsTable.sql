-- Create AttendanceRecords table to store seminar/webinar attendance
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'AttendanceRecords') AND type in (N'U'))
BEGIN
    PRINT 'Creating AttendanceRecords table...';
    
    CREATE TABLE AttendanceRecords (
        AttendanceId INT IDENTITY(1,1) PRIMARY KEY,
        StudentId NVARCHAR(50) NOT NULL,
        TeacherId NVARCHAR(50) NOT NULL,
        EventName NVARCHAR(200) NOT NULL,
        EventDescription NVARCHAR(MAX) NULL,
        EventDate DATETIME NOT NULL,
        RecordedDate DATETIME NOT NULL DEFAULT GETDATE(),
        Score DECIMAL(5,2) NOT NULL DEFAULT 0,
        ProofImageData VARBINARY(MAX) NULL,
        ProofImageContentType NVARCHAR(100) NULL,
        IsVerified BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_AttendanceRecords_StudentId FOREIGN KEY (StudentId) 
            REFERENCES Users(UserId),
        CONSTRAINT FK_AttendanceRecords_TeacherId FOREIGN KEY (TeacherId) 
            REFERENCES Users(UserId)
    );
    
    -- Create indexes for better performance
    CREATE INDEX IX_AttendanceRecords_StudentId ON AttendanceRecords(StudentId);
    CREATE INDEX IX_AttendanceRecords_TeacherId ON AttendanceRecords(TeacherId);
    
    PRINT 'AttendanceRecords table created successfully.';
END
ELSE
BEGIN
    PRINT 'Table AttendanceRecords already exists. Skipping creation.';
END 