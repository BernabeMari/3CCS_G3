-- Add new grade columns to StudentDetails table
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails')
BEGIN
    -- Check if columns already exist before adding them
    IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'FirstYearGrade')
    BEGIN
        ALTER TABLE StudentDetails ADD FirstYearGrade DECIMAL(5,2) NULL;
        PRINT 'Added FirstYearGrade column';
    END

    IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'SecondYearGrade')
    BEGIN
        ALTER TABLE StudentDetails ADD SecondYearGrade DECIMAL(5,2) NULL;
        PRINT 'Added SecondYearGrade column';
    END

    IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ThirdYearGrade')
    BEGIN
        ALTER TABLE StudentDetails ADD ThirdYearGrade DECIMAL(5,2) NULL;
        PRINT 'Added ThirdYearGrade column';
    END

    IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'FourthYearGrade')
    BEGIN
        ALTER TABLE StudentDetails ADD FourthYearGrade DECIMAL(5,2) NULL;
        PRINT 'Added FourthYearGrade column';
    END

    IF NOT EXISTS(SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'AchievementScore')
    BEGIN
        ALTER TABLE StudentDetails ADD AchievementScore DECIMAL(5,2) NULL;
        PRINT 'Added AchievementScore column';
    END

    PRINT 'All columns added successfully.';
END
ELSE
BEGIN
    PRINT 'StudentDetails table does not exist.';
END 