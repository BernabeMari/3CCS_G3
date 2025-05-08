-- Sync contact information between Users and EmployerDetails tables
PRINT 'Synchronizing contact information from Users to EmployerDetails...'

-- Update EmployerDetails with email and phone from Users table
UPDATE ed 
SET ed.Email = u.Email, 
    ed.PhoneNumber = u.PhoneNumber
FROM EmployerDetails ed 
INNER JOIN Users u ON ed.UserId = u.UserId
WHERE u.Role = 'employer' 
  AND (u.Email IS NOT NULL OR u.PhoneNumber IS NOT NULL)

-- Count how many records were updated
DECLARE @UpdatedCount INT
SELECT @UpdatedCount = COUNT(*)
FROM EmployerDetails ed 
INNER JOIN Users u ON ed.UserId = u.UserId
WHERE u.Role = 'employer' 
  AND (ed.Email IS NOT NULL OR ed.PhoneNumber IS NOT NULL)

PRINT CAST(@UpdatedCount AS NVARCHAR) + ' employer record(s) now have contact information.'

PRINT 'Contact information synchronization complete!' 