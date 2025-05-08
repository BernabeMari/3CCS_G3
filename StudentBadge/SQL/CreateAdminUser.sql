-- Script to create an admin user
-- Username: zyb
-- Password: Bernabe202003! (will be hashed in application)

-- Check if the Users table exists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
BEGIN
    -- Create Users table if it doesn't exist
    CREATE TABLE Users (
        UserId NVARCHAR(50) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL UNIQUE,
        Password NVARCHAR(255) NOT NULL,
        FullName NVARCHAR(100) NOT NULL,
        Role NVARCHAR(20) NOT NULL,
        Email NVARCHAR(100),
        PhoneNumber NVARCHAR(20),
        IsActive BIT DEFAULT 1,
        IsVerified BIT DEFAULT 1,
        CreatedAt DATETIME DEFAULT GETDATE(),
        LastLoginAt DATETIME NULL,
        NeedsPasswordChange BIT DEFAULT 0,
        Surname NVARCHAR(100),
        MiddleName NVARCHAR(100),
        FormattedFullName NVARCHAR(200),
        FailedLoginAttempts INT DEFAULT 0,
        LastFailedLoginAt DATETIME NULL
    );
END

-- Admin user ID with timestamp for uniqueness
DECLARE @AdminId NVARCHAR(50) = 'ADMIN' + CONVERT(NVARCHAR(14), GETDATE(), 112) + CONVERT(NVARCHAR(6), GETDATE(), 108);
SET @AdminId = REPLACE(@AdminId, ':', '');

-- Check if username already exists
IF NOT EXISTS (SELECT * FROM Users WHERE Username = 'zyb')
BEGIN
    -- Note: The password will be stored as plaintext here but will be hashed when
    -- the application processes it. In production, never store plaintext passwords.
    INSERT INTO Users (
        UserId, Username, Password, FullName, Role, Email, IsActive, IsVerified
    )
    VALUES (
        @AdminId, 
        'zyb', 
        'Bernabe202003!', -- Will be hashed by application
        'Admin User',
        'admin',
        'admin@example.com',
        1, -- IsActive
        1  -- IsVerified
    );
    
    PRINT 'Admin user created with Username: zyb';
END
ELSE
BEGIN
    PRINT 'Username zyb already exists';
END 