-- Drop existing trigger if it exists
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'TR_ScoreWeights_EnsureTotalIs100')
BEGIN
    DROP TRIGGER TR_ScoreWeights_EnsureTotalIs100;
END
GO

-- Create the table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScoreWeights')
BEGIN
    -- Create a table to store score category weights
    CREATE TABLE ScoreWeights (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CategoryName NVARCHAR(50) NOT NULL,
        Weight DECIMAL(5,2) NOT NULL,
        Description NVARCHAR(255),
        CreatedDate DATETIME DEFAULT GETDATE(),
        ModifiedDate DATETIME DEFAULT GETDATE()
    );

    -- Insert default weights
    INSERT INTO ScoreWeights (CategoryName, Weight, Description)
    VALUES 
        ('AcademicGrades', 30.00, 'Weight for academic grades scores'),
        ('CompletedChallenges', 20.00, 'Weight for completed challenges scores'),
        ('Mastery', 20.00, 'Weight for mastery scores'),
        ('SeminarsWebinars', 10.00, 'Weight for seminars and webinars scores'),
        ('Extracurricular', 20.00, 'Weight for extracurricular activities scores');
END
GO

-- Create a trigger to ensure the total weight is always 100%
CREATE TRIGGER TR_ScoreWeights_EnsureTotalIs100
ON ScoreWeights
AFTER INSERT, UPDATE
AS
BEGIN
    DECLARE @TotalWeight DECIMAL(5,2);
    
    -- Calculate the sum and round to 2 decimal places to avoid precision issues
    SELECT @TotalWeight = ROUND(SUM(Weight), 2) FROM ScoreWeights;
    
    -- Use a small margin for rounding errors (0.1% tolerance)
    IF ABS(@TotalWeight - 100.00) > 0.1
    BEGIN
        DECLARE @ErrorMsg NVARCHAR(200);
        SET @ErrorMsg = CONCAT('Total weight must equal 100%% (Current total: ', FORMAT(@TotalWeight, 'N2'), '%%)');
        RAISERROR(@ErrorMsg, 16, 1);
        ROLLBACK TRANSACTION;
    END
END
GO 