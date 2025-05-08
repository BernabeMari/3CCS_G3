-- Add component score columns to StudentDetails table
-- This script adds columns to track individual score components

-- Check if AcademicGradesScore column exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'AcademicGradesScore')
BEGIN
    ALTER TABLE StudentDetails ADD AcademicGradesScore DECIMAL(5,2) DEFAULT 0 NOT NULL;
    PRINT 'Added AcademicGradesScore column';
END

-- Check if CompletedChallengesScore column exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'CompletedChallengesScore')
BEGIN
    ALTER TABLE StudentDetails ADD CompletedChallengesScore DECIMAL(5,2) DEFAULT 0 NOT NULL;
    PRINT 'Added CompletedChallengesScore column';
END

-- Check if MasteryScore column exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'MasteryScore')
BEGIN
    ALTER TABLE StudentDetails ADD MasteryScore DECIMAL(5,2) DEFAULT 0 NOT NULL;
    PRINT 'Added MasteryScore column';
END

-- Check if SeminarsWebinarsScore column exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'SeminarsWebinarsScore')
BEGIN
    ALTER TABLE StudentDetails ADD SeminarsWebinarsScore DECIMAL(5,2) DEFAULT 0 NOT NULL;
    PRINT 'Added SeminarsWebinarsScore column';
END

-- Check if ExtracurricularScore column exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ExtracurricularScore')
BEGIN
    ALTER TABLE StudentDetails ADD ExtracurricularScore DECIMAL(5,2) DEFAULT 0 NOT NULL;
    PRINT 'Added ExtracurricularScore column';
END

PRINT 'Score component columns update completed.'; 