-- Check if the Score column exists in StudentDetails table
IF EXISTS (
    SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'StudentDetails' 
    AND COLUMN_NAME = 'Score'
)
BEGIN
    -- Create a temporary table with the same structure but without the Score column
    SELECT 
        UserId,
        IdNumber,
        Course,
        Section,
        Achievements,
        Comments,
        BadgeColor,
        ProfilePicturePath,
        IsProfileVisible,
        IsResumeVisible,
        ResumeFileName,
        FirstYearGrade,
        SecondYearGrade,
        ThirdYearGrade,
        FourthYearGrade,
        GradeLevel
        INTO #TempStudentDetails
    FROM StudentDetails;

    -- Drop the original table
    DROP TABLE StudentDetails;

    -- Rename the temp table to the original table name
    EXEC sp_rename '#TempStudentDetails', 'StudentDetails';

    PRINT 'Score column has been removed from StudentDetails table';
END
ELSE
BEGIN
    PRINT 'Score column does not exist in StudentDetails table';
END 