using StudentBadge.Models;
using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;

namespace StudentBadge.Services
{
    public class CertificateService
    {
        private readonly string _connectionString;

        public CertificateService(string connectionString)
        {
            _connectionString = connectionString;
            EnsureCertificatesTableExistsAsync().GetAwaiter().GetResult();
        }

        private async Task EnsureCertificatesTableExistsAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    
                    // Check if the Certificates table exists
                    string checkTableSql = @"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'Certificates'";
                    
                    using (var command = new SqlCommand(checkTableSql, connection))
                    {
                        int tableExists = Convert.ToInt32(await command.ExecuteScalarAsync());
                        
                        if (tableExists == 0)
                        {
                            // Check if the required tables for foreign keys exist
                            string checkForeignKeyTablesSql = @"
                                SELECT 
                                    (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ProgrammingTests') AS ProgrammingTestsExists";
                            
                            bool programmingTestsTableExists = false;
                            
                            using (var fkCommand = new SqlCommand(checkForeignKeyTablesSql, connection))
                            {
                                using (var reader = await fkCommand.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        programmingTestsTableExists = reader.GetInt32(0) > 0;
                                    }
                                }
                            }
                            
                            // Table doesn't exist, create it
                            string createTableSql;
                            
                            if (programmingTestsTableExists)
                            {
                                // Create table with foreign key to ProgrammingTests
                                createTableSql = @"
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
                                        CONSTRAINT FK_Certificates_TestId FOREIGN KEY (TestId) REFERENCES ProgrammingTests(TestId)
                                    )";
                            }
                            else
                            {
                                // Create table without foreign key references to avoid errors
                                createTableSql = @"
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
                                        CertificateContentType NVARCHAR(100) NULL
                                    )";
                            }
                                
                            using (var createCommand = new SqlCommand(createTableSql, connection))
                            {
                                await createCommand.ExecuteNonQueryAsync();
                                Console.WriteLine("Certificates table created successfully.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error ensuring Certificates table exists: {ex.Message}");
                    // Don't throw the exception - just log it and continue
                }
            }
        }

        public async Task<int> GenerateAndSaveCertificate(string studentId, string studentName, int testId, string testName, 
                                                        string programmingLanguage, int gradeLevel, int score)
        {
            try
            {
                // Create certificate object
                var certificate = new Certificate
                {
                    StudentId = studentId,
                    StudentName = studentName,
                    TestId = testId,
                    TestName = testName,
                    ProgrammingLanguage = programmingLanguage,
                    GradeLevel = gradeLevel,
                    Score = score,
                    IssueDate = DateTime.Now
                };

                // Generate both HTML and image versions of the certificate
                certificate.CertificateContent = GenerateCertificateHTML(certificate);
                
                // Generate image certificate and set the binary data
                using (var certificateImage = GenerateCertificateImage(certificate))
                using (var ms = new MemoryStream())
                {
                    // Save the image to a memory stream as PNG
                    certificateImage.Save(ms, ImageFormat.Png);
                    certificate.CertificateData = ms.ToArray();
                    certificate.CertificateContentType = "image/png";
                }

                // Save certificate to database
                return await SaveCertificate(certificate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating certificate: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private Image GenerateCertificateImage(Certificate certificate)
        {
            // Create a bitmap image for the certificate
            int width = 1000;
            int height = 700;
            Bitmap certificateImage = new Bitmap(width, height);
            
            // Create a graphics object from the image
            using (Graphics graphics = Graphics.FromImage(certificateImage))
            {
                // Set high quality drawing mode
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                
                // Fill the background
                graphics.FillRectangle(Brushes.White, 0, 0, width, height);
                
                // Create a decorative border
                using (Pen borderPen = new Pen(Color.FromArgb(0, 102, 204), 20))
                {
                    graphics.DrawRectangle(borderPen, 20, 20, width - 40, height - 40);
                }
                
                // Create title font and set up brushes
                Font titleFont = new Font("Arial", 28, FontStyle.Bold);
                Font subtitleFont = new Font("Arial", 18, FontStyle.Regular);
                Font nameFont = new Font("Arial", 24, FontStyle.Bold);
                Font regularFont = new Font("Arial", 14, FontStyle.Regular);
                Font detailsFont = new Font("Arial", 16, FontStyle.Regular);
                Font scoreFont = new Font("Arial", 18, FontStyle.Bold);
                
                Brush primaryColor = Brushes.DarkBlue;
                Brush secondaryColor = Brushes.Black;
                Brush accentColor = Brushes.Navy;
                
                // Draw the certificate title
                string title = "Certificate of Achievement";
                SizeF titleSize = graphics.MeasureString(title, titleFont);
                graphics.DrawString(title, titleFont, primaryColor, (width - titleSize.Width) / 2, 60);
                
                // Draw the subtitle
                string subtitle = "Programming Excellence Award";
                SizeF subtitleSize = graphics.MeasureString(subtitle, subtitleFont);
                graphics.DrawString(subtitle, subtitleFont, secondaryColor, (width - subtitleSize.Width) / 2, 110);
                
                // Draw "This is to certify that"
                string certifyText = "This is to certify that";
                SizeF certifySize = graphics.MeasureString(certifyText, regularFont);
                graphics.DrawString(certifyText, regularFont, secondaryColor, (width - certifySize.Width) / 2, 170);
                
                // Draw student name
                SizeF nameSize = graphics.MeasureString(certificate.StudentName, nameFont);
                graphics.DrawString(certificate.StudentName, nameFont, primaryColor, (width - nameSize.Width) / 2, 210);
                
                // Draw "has successfully completed the"
                string completedText = "has successfully completed the";
                SizeF completedSize = graphics.MeasureString(completedText, regularFont);
                graphics.DrawString(completedText, regularFont, secondaryColor, (width - completedSize.Width) / 2, 260);
                
                // Draw test name
                string testText = certificate.TestName;
                SizeF testSize = graphics.MeasureString(testText, detailsFont);
                graphics.DrawString(testText, detailsFont, accentColor, (width - testSize.Width) / 2, 310);
                
                // Draw details
                int detailsX = width / 2 - 150;
                int detailsY = 360;
                
                graphics.DrawString($"Programming Language: {certificate.ProgrammingLanguage}", detailsFont, 
                    secondaryColor, detailsX, detailsY);
                    
                graphics.DrawString($"Grade Level: {certificate.GradeLevel}", detailsFont, 
                    secondaryColor, detailsX, detailsY + 30);
                
                // Draw score
                string scoreText = $"Score: {certificate.Score}%";
                graphics.DrawString(scoreText, scoreFont, primaryColor, detailsX, detailsY + 65);
                
                // Draw issue date at the bottom
                string dateText = $"Issued on {certificate.IssueDate.ToString("MMMM dd, yyyy")}";
                SizeF dateSize = graphics.MeasureString(dateText, regularFont);
                graphics.DrawString(dateText, regularFont, secondaryColor, (width - dateSize.Width) / 2, height - 120);
                
                // Draw signature line
                int lineY = height - 80;
                graphics.DrawLine(new Pen(Color.Black, 1), width / 2 - 100, lineY, width / 2 + 100, lineY);
                
                string signatureText = "School Administrator";
                SizeF signatureSize = graphics.MeasureString(signatureText, regularFont);
                graphics.DrawString(signatureText, regularFont, secondaryColor, 
                    (width - signatureSize.Width) / 2, lineY + 10);
            }
            
            return certificateImage;
        }

        private string GenerateCertificateHTML(Certificate certificate)
        {
            // Generate the HTML content for the certificate (keep as fallback)
            string html = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <title>Programming Achievement Certificate</title>
                <style>
                    body {{
                        font-family: 'Arial', sans-serif;
                        text-align: center;
                        background-color: #f9f9f9;
                        margin: 0;
                        padding: 20px;
                    }}
                    .certificate {{
                        max-width: 800px;
                        margin: 0 auto;
                        background-color: white;
                        padding: 40px;
                        border: 20px solid #0066cc;
                        border-radius: 10px;
                        box-shadow: 0 0 10px rgba(0,0,0,0.1);
                    }}
                    .header {{
                        margin-bottom: 20px;
                    }}
                    .title {{
                        font-size: 36px;
                        color: #0066cc;
                        margin-bottom: 10px;
                        font-weight: bold;
                    }}
                    .subtitle {{
                        font-size: 24px;
                        color: #444;
                        margin-bottom: 30px;
                    }}
                    .student-name {{
                        font-size: 32px;
                        font-weight: bold;
                        color: #333;
                        margin: 20px 0;
                    }}
                    .details {{
                        margin: 30px 0;
                        font-size: 18px;
                        line-height: 1.6;
                    }}
                    .date {{
                        margin-top: 40px;
                        font-style: italic;
                    }}
                    .signature {{
                        margin-top: 60px;
                        border-top: 1px solid #ccc;
                        padding-top: 10px;
                        font-weight: bold;
                    }}
                    .score {{
                        font-size: 24px;
                        font-weight: bold;
                        color: #0066cc;
                    }}
                    @@media print {{
                        body {{
                            background-color: white;
                        }}
                        .certificate {{
                            border: 10px solid #0066cc;
                            box-shadow: none;
                        }}
                    }}
                </style>
            </head>
            <body>
                <div class=""certificate"">
                    <div class=""header"">
                        <div class=""title"">Certificate of Achievement</div>
                        <div class=""subtitle"">Programming Excellence Award</div>
                    </div>
                    
                    <p>This is to certify that</p>
                    <div class=""student-name"">{certificate.StudentName}</div>
                    <p>has successfully completed the</p>
                    <div class=""details"">
                        <strong>{certificate.TestName}</strong><br>
                        Programming Language: <strong>{certificate.ProgrammingLanguage}</strong><br>
                        Grade Level: <strong>{certificate.GradeLevel}</strong><br>
                        <div class=""score"">Score: {certificate.Score}%</div>
                    </div>
                    
                    <div class=""date"">Issued on {certificate.IssueDate.ToString("MMMM dd, yyyy")}</div>
                    
                    <div class=""signature"">School Administrator</div>
                </div>
            </body>
            </html>";

            return html;
        }

        private async Task<int> SaveCertificate(Certificate certificate)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    Console.WriteLine("Attempting to save certificate for student: " + certificate.StudentId);

                    // First check if a certificate already exists for this test and student
                    string checkExistingSql = @"
                        SELECT CertificateId FROM Certificates 
                        WHERE StudentId = @StudentId AND TestId = @TestId";
                        
                    int existingCertificateId = 0;
                    
                    using (var checkCommand = new SqlCommand(checkExistingSql, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@StudentId", certificate.StudentId);
                        checkCommand.Parameters.AddWithValue("@TestId", certificate.TestId);
                        
                        var checkResult = await checkCommand.ExecuteScalarAsync();
                        if (checkResult != null && checkResult != DBNull.Value)
                        {
                            existingCertificateId = Convert.ToInt32(checkResult);
                            Console.WriteLine($"Certificate already exists with ID: {existingCertificateId}");
                        }
                    }
                    
                    if (existingCertificateId > 0)
                    {
                        // Update existing certificate
                        string updateSql = @"
                            UPDATE Certificates SET
                                StudentName = @StudentName,
                                TestName = @TestName,
                                ProgrammingLanguage = @ProgrammingLanguage,
                                GradeLevel = @GradeLevel,
                                Score = @Score,
                                IssueDate = @IssueDate,
                                CertificateContent = @CertificateContent,
                                CertificateData = @CertificateData,
                                CertificateContentType = @CertificateContentType
                            WHERE CertificateId = @CertificateId";
                            
                        using (var updateCommand = new SqlCommand(updateSql, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@CertificateId", existingCertificateId);
                            updateCommand.Parameters.AddWithValue("@StudentName", certificate.StudentName);
                            updateCommand.Parameters.AddWithValue("@TestName", certificate.TestName);
                            updateCommand.Parameters.AddWithValue("@ProgrammingLanguage", certificate.ProgrammingLanguage);
                            updateCommand.Parameters.AddWithValue("@GradeLevel", certificate.GradeLevel);
                            updateCommand.Parameters.AddWithValue("@Score", certificate.Score);
                            updateCommand.Parameters.AddWithValue("@IssueDate", certificate.IssueDate);
                            updateCommand.Parameters.AddWithValue("@CertificateContent", certificate.CertificateContent ?? (object)DBNull.Value);
                            updateCommand.Parameters.AddWithValue("@CertificateData", certificate.CertificateData ?? (object)DBNull.Value);
                            updateCommand.Parameters.AddWithValue("@CertificateContentType", certificate.CertificateContentType ?? (object)DBNull.Value);
                            
                            await updateCommand.ExecuteNonQueryAsync();
                            Console.WriteLine($"Certificate updated successfully with ID: {existingCertificateId}");
                            return existingCertificateId;
                        }
                    }
                    else
                    {
                        // Insert new certificate
                        string insertSql = @"
                            INSERT INTO Certificates (
                                StudentId, 
                                StudentName,
                                TestId,
                                TestName,
                                ProgrammingLanguage,
                                GradeLevel,
                                Score,
                                IssueDate,
                                CertificateContent,
                                CertificateData,
                                CertificateContentType
                            )
                            VALUES (
                                @StudentId,
                                @StudentName,
                                @TestId,
                                @TestName,
                                @ProgrammingLanguage,
                                @GradeLevel,
                                @Score,
                                @IssueDate,
                                @CertificateContent,
                                @CertificateData,
                                @CertificateContentType
                            );
                            SELECT SCOPE_IDENTITY();";

                        using (var insertCommand = new SqlCommand(insertSql, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@StudentId", certificate.StudentId);
                            insertCommand.Parameters.AddWithValue("@StudentName", certificate.StudentName);
                            insertCommand.Parameters.AddWithValue("@TestId", certificate.TestId);
                            insertCommand.Parameters.AddWithValue("@TestName", certificate.TestName);
                            insertCommand.Parameters.AddWithValue("@ProgrammingLanguage", certificate.ProgrammingLanguage);
                            insertCommand.Parameters.AddWithValue("@GradeLevel", certificate.GradeLevel);
                            insertCommand.Parameters.AddWithValue("@Score", certificate.Score);
                            insertCommand.Parameters.AddWithValue("@IssueDate", certificate.IssueDate);
                            insertCommand.Parameters.AddWithValue("@CertificateContent", certificate.CertificateContent ?? (object)DBNull.Value);
                            
                            // Ensure the CertificateData is properly set
                            if (certificate.CertificateData != null && certificate.CertificateData.Length > 0)
                            {
                                insertCommand.Parameters.AddWithValue("@CertificateData", certificate.CertificateData);
                                Console.WriteLine($"Certificate data size: {certificate.CertificateData.Length} bytes");
                            }
                            else
                            {
                                insertCommand.Parameters.AddWithValue("@CertificateData", DBNull.Value);
                                Console.WriteLine("No certificate data to save");
                            }
                            
                            insertCommand.Parameters.AddWithValue("@CertificateContentType", certificate.CertificateContentType ?? (object)DBNull.Value);

                            var result = await insertCommand.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                int newCertificateId = Convert.ToInt32(result);
                                Console.WriteLine($"New certificate created with ID: {newCertificateId}");
                                return newCertificateId;
                            }
                            else
                            {
                                Console.WriteLine("Failed to get new certificate ID after insertion.");
                                return 0;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving certificate: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // Re-throw the exception to be handled by the caller
                throw new Exception($"Failed to save certificate: {ex.Message}", ex);
            }
        }

        public async Task<Certificate> GetCertificateById(int certificateId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT * FROM Certificates
                    WHERE CertificateId = @CertificateId";

                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@CertificateId", certificateId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Certificate
                            {
                                CertificateId = reader.GetInt32(reader.GetOrdinal("CertificateId")),
                                StudentId = reader.GetString(reader.GetOrdinal("StudentId")),
                                StudentName = reader.GetString(reader.GetOrdinal("StudentName")),
                                TestId = reader.GetInt32(reader.GetOrdinal("TestId")),
                                TestName = reader.GetString(reader.GetOrdinal("TestName")),
                                ProgrammingLanguage = reader.GetString(reader.GetOrdinal("ProgrammingLanguage")),
                                GradeLevel = reader.GetInt32(reader.GetOrdinal("GradeLevel")),
                                Score = reader.GetInt32(reader.GetOrdinal("Score")),
                                IssueDate = reader.GetDateTime(reader.GetOrdinal("IssueDate")),
                                CertificateContent = reader.IsDBNull(reader.GetOrdinal("CertificateContent")) ? null : reader.GetString(reader.GetOrdinal("CertificateContent")),
                                CertificateData = reader.IsDBNull(reader.GetOrdinal("CertificateData")) ? null : (byte[])reader["CertificateData"],
                                CertificateContentType = reader.IsDBNull(reader.GetOrdinal("CertificateContentType")) ? null : reader.GetString(reader.GetOrdinal("CertificateContentType"))
                            };
                        }
                        return null;
                    }
                }
            }
        }

        public async Task<Certificate[]> GetStudentCertificates(string studentId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT * FROM Certificates
                    WHERE StudentId = @StudentId
                    ORDER BY IssueDate DESC";

                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var certificates = new List<Certificate>();
                        
                        while (await reader.ReadAsync())
                        {
                            certificates.Add(new Certificate
                            {
                                CertificateId = reader.GetInt32(reader.GetOrdinal("CertificateId")),
                                StudentId = reader.GetString(reader.GetOrdinal("StudentId")),
                                StudentName = reader.GetString(reader.GetOrdinal("StudentName")),
                                TestId = reader.GetInt32(reader.GetOrdinal("TestId")),
                                TestName = reader.GetString(reader.GetOrdinal("TestName")),
                                ProgrammingLanguage = reader.GetString(reader.GetOrdinal("ProgrammingLanguage")),
                                GradeLevel = reader.GetInt32(reader.GetOrdinal("GradeLevel")),
                                Score = reader.GetInt32(reader.GetOrdinal("Score")),
                                IssueDate = reader.GetDateTime(reader.GetOrdinal("IssueDate")),
                                CertificateContent = reader.IsDBNull(reader.GetOrdinal("CertificateContent")) ? null : reader.GetString(reader.GetOrdinal("CertificateContent")),
                                // Skip loading the certificate data to reduce memory usage
                                CertificateContentType = reader.IsDBNull(reader.GetOrdinal("CertificateContentType")) ? null : reader.GetString(reader.GetOrdinal("CertificateContentType"))
                            });
                        }
                        
                        return certificates.ToArray();
                    }
                }
            }
        }
    }
} 