-- Add GradeLevel column to StudentDetails table
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails')
BEGIN
    -- Check if column already exists before adding it
    IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'GradeLevel')
    BEGIN
        ALTER TABLE StudentDetails ADD GradeLevel INT NULL;
        PRINT 'Added GradeLevel column';
    END
    ELSE
    BEGIN
        PRINT 'GradeLevel column already exists.';
    END

    PRINT 'GradeLevel column check completed.';
END
ELSE
BEGIN
    PRINT 'StudentDetails table does not exist.';
END 