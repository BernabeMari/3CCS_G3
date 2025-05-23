-- Add VisibleFromDate column to Challenges table (only if it doesn't exist)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Challenges' AND COLUMN_NAME = 'VisibleFromDate')
BEGIN
    ALTER TABLE Challenges ADD VisibleFromDate DATETIME NULL;
    PRINT 'VisibleFromDate column added successfully.';
END
ELSE
BEGIN
    PRINT 'VisibleFromDate column already exists.';
END
