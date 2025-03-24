CREATE TABLE VideoCalls (
    CallId INT IDENTITY(1,1) PRIMARY KEY,
    EmployerId NVARCHAR(50) NOT NULL,
    StudentId NVARCHAR(50) NOT NULL,
    StartTime DATETIME NOT NULL DEFAULT GETDATE(),
    EndTime DATETIME NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'requested', -- 'requested', 'accepted', 'declined', 'completed', 'missed'
    FOREIGN KEY (EmployerId) REFERENCES Employers(EmployerId),
    FOREIGN KEY (StudentId) REFERENCES Students(IdNumber)
); 