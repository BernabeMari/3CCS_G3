-- Add a default value of 100 to the Score column in AttendanceRecords table
-- This makes the Score parameter optional in the RecordAttendance method

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AttendanceRecords' AND COLUMN_NAME = 'Score')
BEGIN
    -- Add a default constraint to the Score column
    DECLARE @ConstraintName nvarchar(200);
    
    -- Check if a default constraint already exists
    SELECT @ConstraintName = name
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID('AttendanceRecords')
    AND parent_column_id = (
        SELECT column_id
        FROM sys.columns
        WHERE object_id = OBJECT_ID('AttendanceRecords')
        AND name = 'Score'
    );
    
    -- Drop existing default constraint if it exists
    IF @ConstraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE AttendanceRecords DROP CONSTRAINT ' + @ConstraintName);
    END
    
    -- Add new default constraint
    ALTER TABLE AttendanceRecords
    ADD CONSTRAINT DF_AttendanceRecords_Score DEFAULT 100 FOR Score;
    
    PRINT 'Default value of 100 added to Score column in AttendanceRecords table';
END
ELSE
BEGIN
    PRINT 'Score column not found in AttendanceRecords table';
END 