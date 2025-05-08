-- Drop the trigger if it already exists
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_UpdateBadgeColor')
DROP TRIGGER trg_UpdateBadgeColor
GO

-- Create trigger to automatically update badge color when score changes
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
                WHEN i.Score >= 95 THEN 'platinum'
                WHEN i.Score >= 85 THEN 'gold'
                WHEN i.Score >= 75 THEN 'silver'
                WHEN i.Score >= 65 THEN 'bronze'
                WHEN i.Score >= 50 THEN 'rising-star'
                WHEN i.Score >= 1 THEN 'needs'
                ELSE 'none'
            END
        FROM StudentDetails sd
        INNER JOIN inserted i ON sd.IdNumber = i.IdNumber
    END
END
GO

-- Also create a trigger for the legacy Students table if it exists
IF OBJECT_ID('Students', 'U') IS NOT NULL
BEGIN
    -- Drop the trigger if it already exists
    IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_UpdateBadgeColor_Legacy')
    DROP TRIGGER trg_UpdateBadgeColor_Legacy
    GO

    -- Create trigger for legacy Students table
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
                    WHEN i.Score >= 95 THEN 'platinum'
                    WHEN i.Score >= 85 THEN 'gold'
                    WHEN i.Score >= 75 THEN 'silver'
                    WHEN i.Score >= 65 THEN 'bronze'
                    WHEN i.Score >= 50 THEN 'rising-star'
                    WHEN i.Score >= 1 THEN 'needs'
                    ELSE 'none'
                END
            FROM Students s
            INNER JOIN inserted i ON s.IdNumber = i.IdNumber
        END
    END
END
GO

PRINT 'Badge color triggers created successfully!' 