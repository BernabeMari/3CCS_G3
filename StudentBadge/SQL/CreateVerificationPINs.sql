-- Check if VerificationPINs table exists and create it if it doesn't
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'VerificationPINs')
BEGIN
    PRINT 'Creating VerificationPINs table...'
    
    CREATE TABLE [dbo].[VerificationPINs] (
        [PINId] INT IDENTITY(1,1) PRIMARY KEY,
        [PIN] NVARCHAR(10) NOT NULL,
        [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
        [ExpiryDate] DATETIME NOT NULL,
        [IsUsed] BIT NOT NULL DEFAULT 0,
        [UsedById] INT NULL,
        [UsedAt] DATETIME NULL
    )
    
    -- Create an index on PIN for faster lookups
    CREATE INDEX [IX_VerificationPINs_PIN] ON [dbo].[VerificationPINs] ([PIN])
    
    PRINT 'VerificationPINs table created successfully.'
END
ELSE
BEGIN
    PRINT 'VerificationPINs table already exists.'
END

-- Check if IsVerified column exists in Users table and add it if it doesn't
IF NOT EXISTS (
    SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'IsVerified'
)
BEGIN
    PRINT 'Adding IsVerified column to Users table...'
    
    ALTER TABLE [dbo].[Users]
    ADD [IsVerified] BIT NOT NULL DEFAULT 1
    
    PRINT 'IsVerified column added to Users table successfully.'
END
ELSE
BEGIN
    PRINT 'IsVerified column already exists in Users table.'
END

PRINT 'Verification system setup complete'; 