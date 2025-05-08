CREATE TABLE MarkedStudents (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    EmployerId NVARCHAR(50) NOT NULL,
    StudentId NVARCHAR(50) NOT NULL,
    DateMarked DATETIME DEFAULT GETDATE(),
    Notes NVARCHAR(MAX),
    CONSTRAINT FK_MarkedStudents_Employers FOREIGN KEY (EmployerId) 
        REFERENCES Users(UserId),
    CONSTRAINT FK_MarkedStudents_Students FOREIGN KEY (StudentId) 
        REFERENCES StudentDetails(IdNumber),
    CONSTRAINT UQ_MarkedStudents_EmployerStudent UNIQUE (EmployerId, StudentId)
); 