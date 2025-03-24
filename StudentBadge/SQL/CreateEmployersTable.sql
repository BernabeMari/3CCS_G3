CREATE TABLE Employers (
    EmployerId NVARCHAR(50) PRIMARY KEY,
    Username NVARCHAR(100) NOT NULL UNIQUE,
    Password NVARCHAR(100) NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Company NVARCHAR(100) NOT NULL,
    Email NVARCHAR(100),
    PhoneNumber NVARCHAR(20),
    Address NVARCHAR(200),
    Description NVARCHAR(MAX),
    ProfilePicturePath NVARCHAR(255),
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
); 