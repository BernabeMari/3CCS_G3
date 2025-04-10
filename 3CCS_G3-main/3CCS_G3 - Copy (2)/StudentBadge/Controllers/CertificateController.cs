using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Dynamic;
using System.IO;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class CertificateController : Controller
    {
        private readonly ILogger<CertificateController> _logger;
        private readonly string _connectionString;

        public CertificateController(ILogger<CertificateController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("GetCertificates")]
        public async Task<IActionResult> GetCertificates()
        {
            try
            {
                List<dynamic> certificates = new List<dynamic>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "Certificates"))
                    {
                        await EnsureCertificatesTableExists(connection);
                        return Json(new { success = true, certificates = certificates });
                    }
                    
                    string query = @"
                        SELECT 
                            CertificateId, 
                            StudentId,
                            StudentName,
                            TestId,
                            TestName,
                            ProgrammingLanguage,
                            GradeLevel,
                            Score,
                            IssueDate
                        FROM 
                            Certificates
                        ORDER BY 
                            IssueDate DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                dynamic certificate = new ExpandoObject();
                                certificate.id = Convert.ToInt32(reader["CertificateId"]);
                                certificate.studentId = reader["StudentId"].ToString();
                                certificate.studentName = reader["StudentName"].ToString();
                                certificate.testId = Convert.ToInt32(reader["TestId"]);
                                certificate.testName = reader["TestName"].ToString();
                                certificate.programmingLanguage = reader["ProgrammingLanguage"].ToString();
                                certificate.gradeLevel = Convert.ToInt32(reader["GradeLevel"]);
                                certificate.score = Convert.ToInt32(reader["Score"]);
                                certificate.issueDate = Convert.ToDateTime(reader["IssueDate"]);
                                
                                certificates.Add(certificate);
                            }
                        }
                    }
                }
                
                return Json(new { success = true, certificates = certificates });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting certificates");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetStudentCertificates/{studentId}")]
        public async Task<IActionResult> GetStudentCertificates(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                List<dynamic> certificates = new List<dynamic>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "Certificates"))
                    {
                        await EnsureCertificatesTableExists(connection);
                        return Json(new { success = true, certificates = certificates });
                    }
                    
                    string query = @"
                        SELECT 
                            CertificateId, 
                            StudentId,
                            StudentName,
                            TestId,
                            TestName,
                            ProgrammingLanguage,
                            GradeLevel,
                            Score,
                            IssueDate
                        FROM 
                            Certificates
                        WHERE
                            StudentId = @StudentId 
                        ORDER BY 
                            IssueDate DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                dynamic certificate = new ExpandoObject();
                                certificate.id = Convert.ToInt32(reader["CertificateId"]);
                                certificate.studentId = reader["StudentId"].ToString();
                                certificate.studentName = reader["StudentName"].ToString();
                                certificate.testId = Convert.ToInt32(reader["TestId"]);
                                certificate.testName = reader["TestName"].ToString();
                                certificate.programmingLanguage = reader["ProgrammingLanguage"].ToString();
                                certificate.gradeLevel = Convert.ToInt32(reader["GradeLevel"]);
                                certificate.score = Convert.ToInt32(reader["Score"]);
                                certificate.issueDate = Convert.ToDateTime(reader["IssueDate"]);
                                
                                certificates.Add(certificate);
                            }
                        }
                    }
                }
                
                return Json(new { success = true, certificates = certificates });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting certificates for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetCertificate/{id}")]
        public async Task<IActionResult> GetCertificate(int id)
        {
            try
            {
                if (id <= 0)
                {
                    return Json(new { success = false, message = "Certificate ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "Certificates"))
                    {
                        return Json(new { success = false, message = "Certificates table does not exist." });
                    }
                    
                    string query = @"
                        SELECT 
                            CertificateId, 
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
                        FROM 
                            Certificates
                        WHERE
                            CertificateId = @CertificateId";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CertificateId", id);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                dynamic certificate = new ExpandoObject();
                                certificate.id = Convert.ToInt32(reader["CertificateId"]);
                                certificate.studentId = reader["StudentId"].ToString();
                                certificate.studentName = reader["StudentName"].ToString();
                                certificate.testId = Convert.ToInt32(reader["TestId"]);
                                certificate.testName = reader["TestName"].ToString();
                                certificate.programmingLanguage = reader["ProgrammingLanguage"].ToString();
                                certificate.gradeLevel = Convert.ToInt32(reader["GradeLevel"]);
                                certificate.score = Convert.ToInt32(reader["Score"]);
                                certificate.issueDate = Convert.ToDateTime(reader["IssueDate"]);
                                
                                // Binary and content data - handle nulls
                                if (!reader.IsDBNull(reader.GetOrdinal("CertificateContent")))
                                {
                                    certificate.content = reader["CertificateContent"].ToString();
                                }
                                
                                if (!reader.IsDBNull(reader.GetOrdinal("CertificateContentType")))
                                {
                                    certificate.contentType = reader["CertificateContentType"].ToString();
                                }
                                
                                // Don't return binary data in JSON - just indicate if it exists
                                certificate.hasData = !reader.IsDBNull(reader.GetOrdinal("CertificateData"));
                                
                                return Json(new { success = true, certificate = certificate });
                            }
                            else
                            {
                                return Json(new { success = false, message = "Certificate not found." });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting certificate with ID {CertificateId}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("DownloadCertificate/{id}")]
        public async Task<IActionResult> DownloadCertificate(int id)
        {
            try
            {
                if (id <= 0)
                {
                    return BadRequest("Certificate ID is required.");
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "Certificates"))
                    {
                        return NotFound("Certificates table does not exist.");
                    }
                    
                    string query = @"
                        SELECT 
                            CertificateData,
                            CertificateContentType,
                            StudentName,
                            TestName
                        FROM 
                            Certificates
                        WHERE
                            CertificateId = @CertificateId";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CertificateId", id);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                if (reader.IsDBNull(reader.GetOrdinal("CertificateData")))
                                {
                                    return NotFound("Certificate data not found.");
                                }
                                
                                byte[] certificateData = (byte[])reader["CertificateData"];
                                string contentType = reader.IsDBNull(reader.GetOrdinal("CertificateContentType")) 
                                    ? "application/pdf" 
                                    : reader["CertificateContentType"].ToString();
                                
                                string studentName = reader["StudentName"].ToString();
                                string testName = reader["TestName"].ToString();
                                
                                string fileName = $"Certificate_{studentName}_{testName}.pdf";
                                
                                return File(certificateData, contentType, fileName);
                            }
                            else
                            {
                                return NotFound("Certificate not found.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading certificate with ID {CertificateId}", id);
                return StatusCode(500, "Error downloading certificate: " + ex.Message);
            }
        }
        
        [HttpDelete]
        [Route("DeleteCertificate/{id}")]
        public async Task<IActionResult> DeleteCertificate(int id)
        {
            try
            {
                if (id <= 0)
                {
                    return Json(new { success = false, message = "Certificate ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "Certificates"))
                    {
                        return Json(new { success = false, message = "Certificates table does not exist." });
                    }
                    
                    string deleteQuery = "DELETE FROM Certificates WHERE CertificateId = @CertificateId";
                    
                    using (var command = new SqlCommand(deleteQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CertificateId", id);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Certificate deleted successfully." });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Certificate not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting certificate with ID {CertificateId}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to check if a table exists
        private async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
        
        // Helper method to ensure Certificates table exists
        private async Task EnsureCertificatesTableExists(SqlConnection connection)
        {
            if (!await TableExists(connection, "Certificates"))
            {
                string createTableSql = @"
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
                
                using (var command = new SqlCommand(createTableSql, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}