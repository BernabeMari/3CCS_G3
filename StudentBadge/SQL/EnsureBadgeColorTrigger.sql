-- This script ensures the badge color trigger is installed
-- First, check if the trigger exists
IF NOT EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_UpdateBadgeColor')
BEGIN
    PRINT 'Installing badge color trigger for StudentDetails table...'
    
    -- Create trigger to automatically update badge color when score changes
    EXEC('
    CREATE TRIGGER trg_UpdateBadgeColor
    ON StudentDetails
    AFTER UPDATE
    AS
    BEGIN
        -- Only run if the Score column was updated
        IF UPDATE(Score)
        BEGIN
            -- Update the badge color based on the new score
            UPDATE sd
            SET BadgeColor = 
                CASE 
                    WHEN i.Score >= 95 THEN ''platinum''
                    WHEN i.Score >= 85 THEN ''gold''
                    WHEN i.Score >= 75 THEN ''silver''
                    WHEN i.Score >= 65 THEN ''bronze''
                    WHEN i.Score >= 50 THEN ''rising-star''
                    WHEN i.Score >= 1 THEN ''needs''
                    ELSE ''none''
                END
            FROM StudentDetails sd
            INNER JOIN inserted i ON sd.IdNumber = i.IdNumber
        END
    END
    ')

    PRINT 'Badge color trigger installed for StudentDetails table!'
END
ELSE
BEGIN
    PRINT 'Badge color trigger already exists for StudentDetails table.'
END
GO

-- Check for legacy Students table
IF OBJECT_ID('Students', 'U') IS NOT NULL
BEGIN
    -- Check if the legacy trigger exists
    IF NOT EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_UpdateBadgeColor_Legacy')
    BEGIN
        PRINT 'Installing badge color trigger for legacy Students table...'
        
        -- Create trigger for legacy Students table
        EXEC('
        CREATE TRIGGER trg_UpdateBadgeColor_Legacy
        ON Students
        AFTER UPDATE
        AS
        BEGIN
            -- Only run if the Score column was updated
            IF UPDATE(Score)
            BEGIN
                -- Update the badge color based on the new score
                UPDATE s
                SET BadgeColor = 
                    CASE 
                        WHEN i.Score >= 95 THEN ''platinum''
                        WHEN i.Score >= 85 THEN ''gold''
                        WHEN i.Score >= 75 THEN ''silver''
                        WHEN i.Score >= 65 THEN ''bronze''
                        WHEN i.Score >= 50 THEN ''rising-star''
                        WHEN i.Score >= 1 THEN ''needs''
                        ELSE ''none''
                    END
                FROM Students s
                INNER JOIN inserted i ON s.IdNumber = i.IdNumber
            END
        END
        ')

        PRINT 'Badge color trigger installed for legacy Students table!'
    END
    ELSE
    BEGIN
        PRINT 'Badge color trigger already exists for legacy Students table.'
    END
END
GO

-- Verify existing badge colors
PRINT 'Updating any incorrect badge colors in the database...'

-- Update StudentDetails table
UPDATE StudentDetails
SET BadgeColor = 
    CASE 
        WHEN Score >= 95 THEN 'platinum'
        WHEN Score >= 85 THEN 'gold'
        WHEN Score >= 75 THEN 'silver'
        WHEN Score >= 65 THEN 'bronze'
        WHEN Score >= 50 THEN 'rising-star'
        WHEN Score >= 1 THEN 'needs'
        ELSE 'none'
    END
WHERE BadgeColor <> 
    CASE 
        WHEN Score >= 95 THEN 'platinum'
        WHEN Score >= 85 THEN 'gold'
        WHEN Score >= 75 THEN 'silver'
        WHEN Score >= 65 THEN 'bronze'
        WHEN Score >= 50 THEN 'rising-star'
        WHEN Score >= 1 THEN 'needs'
        ELSE 'none'
    END
GO

-- If legacy table exists, update it too
IF OBJECT_ID('Students', 'U') IS NOT NULL
BEGIN
    UPDATE Students
    SET BadgeColor = 
        CASE 
            WHEN Score >= 95 THEN 'platinum'
            WHEN Score >= 85 THEN 'gold'
            WHEN Score >= 75 THEN 'silver'
            WHEN Score >= 65 THEN 'bronze'
            WHEN Score >= 50 THEN 'rising-star'
            WHEN Score >= 1 THEN 'needs'
            ELSE 'none'
        END
    WHERE BadgeColor <> 
        CASE 
            WHEN Score >= 95 THEN 'platinum'
            WHEN Score >= 85 THEN 'gold'
            WHEN Score >= 75 THEN 'silver'
            WHEN Score >= 65 THEN 'bronze'
            WHEN Score >= 50 THEN 'rising-star'
            WHEN Score >= 1 THEN 'needs'
            ELSE 'none'
        END
END
GO

PRINT 'Badge color consistency check complete!'
GO 