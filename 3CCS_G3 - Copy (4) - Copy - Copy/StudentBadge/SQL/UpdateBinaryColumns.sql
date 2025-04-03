-- Script to update tables for binary storage of profile images and resumes

-- STEP 1: Create new columns in StudentDetails table
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails')
BEGIN
    -- Add ProfilePictureData column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ProfilePictureData')
    BEGIN
        ALTER TABLE StudentDetails ADD ProfilePictureData VARBINARY(MAX) NULL;
        PRINT 'Added ProfilePictureData column to StudentDetails';
    END

    -- Add ResumeData column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ResumeData')
    BEGIN
        ALTER TABLE StudentDetails ADD ResumeData VARBINARY(MAX) NULL;
        PRINT 'Added ResumeData column to StudentDetails';
    END
    
    -- Add ProfileMetadata column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ProfileMetadata')
    BEGIN
        ALTER TABLE StudentDetails ADD ProfileMetadata NVARCHAR(MAX) NULL;
        PRINT 'Added ProfileMetadata column to StudentDetails';
    END
    
    -- Add ResumeMetadata column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ResumeMetadata')
    BEGIN
        ALTER TABLE StudentDetails ADD ResumeMetadata NVARCHAR(MAX) NULL;
        PRINT 'Added ResumeMetadata column to StudentDetails';
    END
    
    -- Add OriginalResumeFileName column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'OriginalResumeFileName')
    BEGIN
        ALTER TABLE StudentDetails ADD OriginalResumeFileName NVARCHAR(255) NULL;
        PRINT 'Added OriginalResumeFileName column to StudentDetails';
    END
END
GO -- Force a batch break here to commit schema changes

-- STEP 2: Create new columns in Students table
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Students')
BEGIN
    -- Add ProfilePictureData column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'ProfilePictureData')
    BEGIN
        ALTER TABLE Students ADD ProfilePictureData VARBINARY(MAX) NULL;
        PRINT 'Added ProfilePictureData column to Students';
    END

    -- Add ResumeData column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'ResumeData')
    BEGIN
        ALTER TABLE Students ADD ResumeData VARBINARY(MAX) NULL;
        PRINT 'Added ResumeData column to Students';
    END
    
    -- Add ProfileMetadata column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'ProfileMetadata')
    BEGIN
        ALTER TABLE Students ADD ProfileMetadata NVARCHAR(MAX) NULL;
        PRINT 'Added ProfileMetadata column to Students';
    END
    
    -- Add ResumeMetadata column if it doesn't exist
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'ResumeMetadata')
    BEGIN
        ALTER TABLE Students ADD ResumeMetadata NVARCHAR(MAX) NULL;
        PRINT 'Added ResumeMetadata column to Students';
    END
END
GO -- Force a batch break here to commit schema changes

-- STEP 3: Now read file data and update StudentDetails if columns exist
-- We need to read files from paths since we're not using base64 data
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ProfilePictureData')
BEGIN
    PRINT 'Preparing to update profile pictures in StudentDetails';
    
    -- For files, set the metadata based on file extension
    UPDATE StudentDetails
    SET ProfileMetadata = 
        CASE 
            WHEN ProfilePicturePath LIKE '%.jpg' OR ProfilePicturePath LIKE '%.jpeg' THEN '{"ContentType":"image/jpeg","Source":"file"}'
            WHEN ProfilePicturePath LIKE '%.png' THEN '{"ContentType":"image/png","Source":"file"}'
            WHEN ProfilePicturePath LIKE '%.gif' THEN '{"ContentType":"image/gif","Source":"file"}'
            ELSE '{"ContentType":"image/jpeg","Source":"file"}'
        END
    WHERE ProfilePicturePath IS NOT NULL AND ProfilePicturePath NOT LIKE 'data:%';
    
    PRINT 'Updated ProfileMetadata in StudentDetails for file paths';
    
    -- For base64 data, convert to binary
    UPDATE StudentDetails
    SET ProfilePictureData = 
        CONVERT(VARBINARY(MAX), 
            SUBSTRING(ProfilePicturePath, 
                CHARINDEX(',', ProfilePicturePath) + 1, 
                LEN(ProfilePicturePath) - CHARINDEX(',', ProfilePicturePath)),
            1),
        ProfileMetadata = 
        CASE 
            WHEN ProfilePicturePath LIKE 'data:image/jpeg%' THEN '{"ContentType":"image/jpeg","Source":"base64"}'
            WHEN ProfilePicturePath LIKE 'data:image/png%' THEN '{"ContentType":"image/png","Source":"base64"}'
            WHEN ProfilePicturePath LIKE 'data:image/gif%' THEN '{"ContentType":"image/gif","Source":"base64"}'
            ELSE '{"ContentType":"image/jpeg","Source":"base64"}'
        END
    WHERE ProfilePicturePath LIKE 'data:image%';
    
    PRINT 'Updated ProfilePictureData in StudentDetails for base64 data';
END
GO -- Force a batch break here

-- Update resumes in StudentDetails
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ResumeData')
BEGIN
    PRINT 'Preparing to update resumes in StudentDetails';
    
    -- For files, set the metadata based on file extension
    UPDATE StudentDetails
    SET ResumeMetadata = 
        CASE 
            WHEN ResumeFileName LIKE '%.pdf' THEN '{"ContentType":"application/pdf","Source":"file"}'
            WHEN ResumeFileName LIKE '%.doc' THEN '{"ContentType":"application/msword","Source":"file"}'
            WHEN ResumeFileName LIKE '%.docx' THEN '{"ContentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","Source":"file"}'
            ELSE '{"ContentType":"application/pdf","Source":"file"}'
        END
    WHERE ResumeFileName IS NOT NULL AND ResumeFileName NOT LIKE 'data:%';
    
    PRINT 'Updated ResumeMetadata in StudentDetails for file paths';
    
    -- For base64 data, convert to binary
    UPDATE StudentDetails
    SET ResumeData = 
        CONVERT(VARBINARY(MAX), 
            SUBSTRING(ResumeFileName, 
                CHARINDEX(',', ResumeFileName) + 1, 
                LEN(ResumeFileName) - CHARINDEX(',', ResumeFileName)),
            1),
        ResumeMetadata = 
        CASE 
            WHEN ResumeFileName LIKE 'data:application/pdf%' THEN '{"ContentType":"application/pdf","Source":"base64"}'
            WHEN ResumeFileName LIKE 'data:application/msword%' THEN '{"ContentType":"application/msword","Source":"base64"}'
            WHEN ResumeFileName LIKE 'data:application/vnd.openxmlformats-officedocument.wordprocessingml.document%' THEN 
                '{"ContentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","Source":"base64"}'
            ELSE '{"ContentType":"application/pdf","Source":"base64"}'
        END
    WHERE ResumeFileName LIKE 'data:%';
    
    PRINT 'Updated ResumeData in StudentDetails for base64 data';
END
GO -- Force a batch break here

-- STEP 4: Update Students table with existing data
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'ProfilePictureData')
BEGIN
    PRINT 'Preparing to update profile pictures in Students';
    
    -- For files, set the metadata based on file extension
    UPDATE Students
    SET ProfileMetadata = 
        CASE 
            WHEN ProfilePicturePath LIKE '%.jpg' OR ProfilePicturePath LIKE '%.jpeg' THEN '{"ContentType":"image/jpeg","Source":"file"}'
            WHEN ProfilePicturePath LIKE '%.png' THEN '{"ContentType":"image/png","Source":"file"}'
            WHEN ProfilePicturePath LIKE '%.gif' THEN '{"ContentType":"image/gif","Source":"file"}'
            ELSE '{"ContentType":"image/jpeg","Source":"file"}'
        END
    WHERE ProfilePicturePath IS NOT NULL AND ProfilePicturePath NOT LIKE 'data:%';
    
    PRINT 'Updated ProfileMetadata in Students for file paths';
    
    -- For base64 data, convert to binary
    UPDATE Students
    SET ProfilePictureData = 
        CONVERT(VARBINARY(MAX), 
            SUBSTRING(ProfilePicturePath, 
                CHARINDEX(',', ProfilePicturePath) + 1, 
                LEN(ProfilePicturePath) - CHARINDEX(',', ProfilePicturePath)),
            1),
        ProfileMetadata = 
        CASE 
            WHEN ProfilePicturePath LIKE 'data:image/jpeg%' THEN '{"ContentType":"image/jpeg","Source":"base64"}'
            WHEN ProfilePicturePath LIKE 'data:image/png%' THEN '{"ContentType":"image/png","Source":"base64"}'
            WHEN ProfilePicturePath LIKE 'data:image/gif%' THEN '{"ContentType":"image/gif","Source":"base64"}'
            ELSE '{"ContentType":"image/jpeg","Source":"base64"}'
        END
    WHERE ProfilePicturePath LIKE 'data:image%';
    
    PRINT 'Updated ProfilePictureData in Students for base64 data';
END
GO -- Force a batch break here

-- Update resumes in Students
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'ResumeData')
BEGIN
    PRINT 'Preparing to update resumes in Students';
    
    -- For files, set the metadata based on file extension
    UPDATE Students
    SET ResumeMetadata = 
        CASE 
            WHEN ResumeFileName LIKE '%.pdf' THEN '{"ContentType":"application/pdf","Source":"file"}'
            WHEN ResumeFileName LIKE '%.doc' THEN '{"ContentType":"application/msword","Source":"file"}'
            WHEN ResumeFileName LIKE '%.docx' THEN '{"ContentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","Source":"file"}'
            ELSE '{"ContentType":"application/pdf","Source":"file"}'
        END
    WHERE ResumeFileName IS NOT NULL AND ResumeFileName NOT LIKE 'data:%';
    
    PRINT 'Updated ResumeMetadata in Students for file paths';
    
    -- For base64 data, convert to binary
    UPDATE Students
    SET ResumeData = 
        CONVERT(VARBINARY(MAX), 
            SUBSTRING(ResumeFileName, 
                CHARINDEX(',', ResumeFileName) + 1, 
                LEN(ResumeFileName) - CHARINDEX(',', ResumeFileName)),
            1),
        ResumeMetadata = 
        CASE 
            WHEN ResumeFileName LIKE 'data:application/pdf%' THEN '{"ContentType":"application/pdf","Source":"base64"}'
            WHEN ResumeFileName LIKE 'data:application/msword%' THEN '{"ContentType":"application/msword","Source":"base64"}'
            WHEN ResumeFileName LIKE 'data:application/vnd.openxmlformats-officedocument.wordprocessingml.document%' THEN 
                '{"ContentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document","Source":"base64"}'
            ELSE '{"ContentType":"application/pdf","Source":"base64"}'
        END
    WHERE ResumeFileName LIKE 'data:%';
    
    PRINT 'Updated ResumeData in Students for base64 data';
END
GO -- Force a batch break here

PRINT 'Database schema update completed successfully.';
GO 