-- Check if column exists before adding it
IF NOT EXISTS (
    SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'ProgrammingTests' AND COLUMN_NAME = 'YearLevel'
)
BEGIN
    -- Add the YearLevel column with a default value of 1
    ALTER TABLE ProgrammingTests
    ADD YearLevel INT NOT NULL DEFAULT 1;
    
    PRINT 'YearLevel column added to ProgrammingTests table';
END
ELSE
BEGIN
    PRINT 'YearLevel column already exists in ProgrammingTests table';
END 