CREATE TABLE Messages (
    MessageId INT IDENTITY(1,1) PRIMARY KEY,
    SenderId NVARCHAR(50) NOT NULL,
    ReceiverId NVARCHAR(50) NOT NULL,
    MessageContent NVARCHAR(MAX) NOT NULL,
    SentTime DATETIME NOT NULL DEFAULT GETDATE(),
    IsFromEmployer BIT NOT NULL DEFAULT 0,
    IsRead BIT NOT NULL DEFAULT 0,
    FOREIGN KEY (SenderId) REFERENCES Employers(EmployerId),
    FOREIGN KEY (ReceiverId) REFERENCES Students(IdNumber)
); 