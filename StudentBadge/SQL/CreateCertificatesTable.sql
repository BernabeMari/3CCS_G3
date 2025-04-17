CREATE TABLE Certificates (
    CertificateId INT IDENTITY(1,1) PRIMARY KEY,
    StudentId NVARCHAR(50) NOT NULL,
    StudentName NVARCHAR(100) NOT NULL,
    TestId INT NOT NULL,
    TestName NVARCHAR(100) NOT NULL,
    ProgrammingLanguage NVARCHAR(50) NOT NULL,
    GradeLevel INT NOT NULL,
    Score INT NOT NULL,
    IssueDate DATETIME NOT NULL,
    CertificateContent NVARCHAR(MAX) NULL,
    CertificateData VARBINARY(MAX) NULL,
    CertificateContentType NVARCHAR(100) NULL,
    FOREIGN KEY (StudentId) REFERENCES StudentDetails(IdNumber),
    FOREIGN KEY (TestId) REFERENCES ProgrammingTests(TestId)
); 