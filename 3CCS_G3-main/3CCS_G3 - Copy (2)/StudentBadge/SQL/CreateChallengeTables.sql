-- Create Challenges table
CREATE TABLE Challenges (
    ChallengeId INT IDENTITY(1,1) PRIMARY KEY,
    TeacherId NVARCHAR(50) NOT NULL,
    ChallengeName NVARCHAR(255) NOT NULL,
    ProgrammingLanguage NVARCHAR(50) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    YearLevel INT NOT NULL,
    CreatedDate DATETIME NOT NULL,
    LastUpdatedDate DATETIME NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_Challenges_Teachers FOREIGN KEY (TeacherId) REFERENCES Users(UserId)
);

-- Create ChallengeQuestions table
CREATE TABLE ChallengeQuestions (
    QuestionId INT IDENTITY(1,1) PRIMARY KEY,
    ChallengeId INT NOT NULL,
    QuestionText NVARCHAR(MAX) NOT NULL,
    AnswerText NVARCHAR(MAX) NOT NULL,
    CodeSnippet NVARCHAR(MAX) NULL,
    Points INT NOT NULL,
    CreatedDate DATETIME NOT NULL,
    LastUpdatedDate DATETIME NULL,
    CONSTRAINT FK_ChallengeQuestions_Challenges FOREIGN KEY (ChallengeId) REFERENCES Challenges(ChallengeId)
);

-- Create ChallengeSubmissions table
CREATE TABLE ChallengeSubmissions (
    SubmissionId INT IDENTITY(1,1) PRIMARY KEY,
    ChallengeId INT NOT NULL,
    StudentId NVARCHAR(50) NOT NULL,
    SubmissionDate DATETIME NOT NULL,
    CONSTRAINT FK_ChallengeSubmissions_Challenges FOREIGN KEY (ChallengeId) REFERENCES Challenges(ChallengeId),
    CONSTRAINT FK_ChallengeSubmissions_Students FOREIGN KEY (StudentId) REFERENCES Users(UserId)
);

-- Create ChallengeAnswers table
CREATE TABLE ChallengeAnswers (
    AnswerId INT IDENTITY(1,1) PRIMARY KEY,
    SubmissionId INT NOT NULL,
    QuestionId INT NOT NULL,
    AnswerText NVARCHAR(MAX) NOT NULL,
    CONSTRAINT FK_ChallengeAnswers_ChallengeSubmissions FOREIGN KEY (SubmissionId) REFERENCES ChallengeSubmissions(SubmissionId),
    CONSTRAINT FK_ChallengeAnswers_ChallengeQuestions FOREIGN KEY (QuestionId) REFERENCES ChallengeQuestions(QuestionId)
); 