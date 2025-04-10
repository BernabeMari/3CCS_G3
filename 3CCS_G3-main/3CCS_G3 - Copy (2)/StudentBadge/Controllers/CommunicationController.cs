using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Dynamic;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class CommunicationController : Controller
    {
        private readonly ILogger<CommunicationController> _logger;
        private readonly string _connectionString;

        public CommunicationController(ILogger<CommunicationController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("GetEmployerMessages/{employerId}")]
        public async Task<IActionResult> GetEmployerMessages(string employerId)
        {
            try
            {
                if (string.IsNullOrEmpty(employerId))
                {
                    employerId = HttpContext.Session.GetString("EmployerId");
                    if (string.IsNullOrEmpty(employerId))
                    {
                        return Json(new { success = false, message = "Employer ID is required" });
                    }
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if new table structure exists
                    var checkTableCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                        connection
                    );
                    bool useNewTable = Convert.ToInt32(await checkTableCmd.ExecuteScalarAsync()) > 0;

                    string query;
                    if (useNewTable)
                    {
                        query = @"
                            SELECT DISTINCT 
                                m.StudentId,
                                u.FullName as StudentName,
                                m.Message as Message,
                                m.SentTime as SentTime,
                                m.IsRead,
                                m.IsFromEmployer
                            FROM EmployerStudentMessages m
                            JOIN Users u ON m.StudentId = u.UserId
                            WHERE m.EmployerId = @EmployerId
                            AND m.SentTime IN (
                                SELECT MAX(SentTime)
                                FROM EmployerStudentMessages
                                WHERE EmployerId = @EmployerId
                                GROUP BY StudentId
                            )
                            ORDER BY m.SentTime DESC";
                    }
                    else
                    {
                        query = @"
                            SELECT DISTINCT 
                                m.StudentId,
                                s.FullName as StudentName,
                                m.MessageContent as Message,
                                m.SentDateTime as SentTime,
                                m.IsRead,
                                m.IsFromEmployer
                            FROM Messages m
                            JOIN Students s ON m.StudentId = s.IdNumber
                            WHERE m.EmployerId = @EmployerId
                            AND m.SentDateTime IN (
                                SELECT MAX(SentDateTime)
                                FROM Messages
                                WHERE EmployerId = @EmployerId
                                GROUP BY StudentId
                            )
                            ORDER BY m.SentDateTime DESC";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        var messages = new List<object>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                messages.Add(new
                                {
                                    studentId = reader["StudentId"].ToString(),
                                    studentName = reader["StudentName"].ToString(),
                                    message = reader["Message"].ToString(),
                                    sentTime = Convert.ToDateTime(reader["SentTime"]),
                                    isRead = Convert.ToBoolean(reader["IsRead"]),
                                    isFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"])
                                });
                            }
                        }

                        return Json(new { success = true, messages = messages });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in GetEmployerMessages: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
            }
        }

        [HttpGet]
        [Route("GetStudentMessages")]
        public async Task<IActionResult> GetStudentMessages(string studentId, string employerId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    studentId = HttpContext.Session.GetString("IdNumber");
                    if (string.IsNullOrEmpty(studentId))
                    {
                        return Json(new { success = false, message = "Student ID is required" });
                    }
                }

                if (string.IsNullOrEmpty(employerId))
                {
                    return Json(new { success = false, message = "Employer ID is required" });
                }

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    
                    // Check if new table structure exists
                    var checkTableCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                        conn
                    );
                    bool useNewTable = Convert.ToInt32(await checkTableCmd.ExecuteScalarAsync()) > 0;

                    string query;
                    if (useNewTable)
                    {
                        query = @"
                            SELECT 
                                m.Message as Content,
                                m.SentTime,
                                m.IsFromEmployer,
                                m.IsRead,
                                u.FullName as EmployerName,
                                ed.Company
                            FROM EmployerStudentMessages m
                            JOIN Users u ON m.EmployerId = u.UserId
                            JOIN EmployerDetails ed ON u.UserId = ed.UserId
                            WHERE m.StudentId = @StudentId AND m.EmployerId = @EmployerId
                            ORDER BY m.SentTime ASC";
                    }
                    else
                    {
                        query = @"
                            SELECT 
                                m.MessageContent as Content,
                                m.SentDateTime as SentTime,
                                m.IsFromEmployer,
                                m.IsRead,
                                e.FullName as EmployerName,
                                e.Company
                            FROM Messages m
                            JOIN Employers e ON m.EmployerId = e.EmployerId
                            WHERE m.StudentId = @StudentId AND m.EmployerId = @EmployerId
                            ORDER BY m.SentDateTime ASC";
                    }

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@StudentId", studentId);
                        cmd.Parameters.AddWithValue("@EmployerId", employerId);

                        var messages = new List<MessageViewModel>();
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                messages.Add(new MessageViewModel
                                {
                                    Content = reader["Content"].ToString(),
                                    SentTime = Convert.ToDateTime(reader["SentTime"]),
                                    IsFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"]),
                                    IsRead = Convert.ToBoolean(reader["IsRead"]),
                                    EmployerName = reader["EmployerName"].ToString(),
                                    Company = reader["Company"].ToString()
                                });
                            }
                        }

                        // Mark messages as read
                        string updateQuery = useNewTable
                            ? "UPDATE EmployerStudentMessages SET IsRead = 1 WHERE StudentId = @StudentId AND EmployerId = @EmployerId AND IsRead = 0 AND IsFromEmployer = 1"
                            : "UPDATE Messages SET IsRead = 1 WHERE StudentId = @StudentId AND EmployerId = @EmployerId AND IsRead = 0 AND IsFromEmployer = 1";

                        using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
                        {
                            updateCmd.Parameters.AddWithValue("@StudentId", studentId);
                            updateCmd.Parameters.AddWithValue("@EmployerId", employerId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }

                        return Json(new { success = true, messages = messages });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting student messages");
                return Json(new { success = false, message = "Failed to load messages. Please try again." });
            }
        }

        [HttpPost]
        [Route("SendMessage")]
        public async Task<IActionResult> SendMessage(string studentId, string employerId, string message, bool isFromEmployer)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(employerId) || string.IsNullOrEmpty(message))
                {
                    return Json(new { success = false, message = "Student ID, Employer ID and message content are required." });
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if new table structure exists
                    var checkTableCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                        connection
                    );
                    bool useNewTable = Convert.ToInt32(await checkTableCmd.ExecuteScalarAsync()) > 0;
                    string query;

                    if (useNewTable)
                    {
                        // Ensure the new table exists
                        if (!useNewTable)
                        {
                            string createTableSql = @"
                                CREATE TABLE EmployerStudentMessages (
                                    MessageId INT IDENTITY(1,1) PRIMARY KEY,
                                    EmployerId NVARCHAR(128) NOT NULL,
                                    StudentId NVARCHAR(128) NOT NULL,
                                    Message NVARCHAR(MAX) NOT NULL,
                                    SentTime DATETIME NOT NULL DEFAULT GETDATE(),
                                    IsRead BIT NOT NULL DEFAULT 0,
                                    IsFromEmployer BIT NOT NULL
                                )";

                            using (var createTableCmd = new SqlCommand(createTableSql, connection))
                            {
                                await createTableCmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Insert into new table
                        query = @"
                            INSERT INTO EmployerStudentMessages (EmployerId, StudentId, Message, SentTime, IsRead, IsFromEmployer)
                            VALUES (@EmployerId, @StudentId, @Message, GETDATE(), 0, @IsFromEmployer);
                            SELECT SCOPE_IDENTITY();";
                    }
                    else
                    {
                        // Ensure old table exists
                        var checkOldTableCmd = new SqlCommand(
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Messages'",
                            connection
                        );
                        bool messagesTableExists = Convert.ToInt32(await checkOldTableCmd.ExecuteScalarAsync()) > 0;

                        if (!messagesTableExists)
                        {
                            string createOldTableSql = @"
                                CREATE TABLE Messages (
                                    MessageId INT IDENTITY(1,1) PRIMARY KEY,
                                    EmployerId NVARCHAR(50) NOT NULL,
                                    StudentId NVARCHAR(50) NOT NULL,
                                    MessageContent NVARCHAR(MAX) NOT NULL,
                                    SentDateTime DATETIME NOT NULL DEFAULT GETDATE(),
                                    IsRead BIT NOT NULL DEFAULT 0,
                                    IsFromEmployer BIT NOT NULL
                                )";

                            using (var createOldTableCmd = new SqlCommand(createOldTableSql, connection))
                            {
                                await createOldTableCmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Insert into old table
                        query = @"
                            INSERT INTO Messages (EmployerId, StudentId, MessageContent, SentDateTime, IsRead, IsFromEmployer)
                            VALUES (@EmployerId, @StudentId, @Message, GETDATE(), 0, @IsFromEmployer);
                            SELECT SCOPE_IDENTITY();";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        command.Parameters.AddWithValue("@Message", message);
                        command.Parameters.AddWithValue("@IsFromEmployer", isFromEmployer);

                        var newId = await command.ExecuteScalarAsync();
                        var messageId = Convert.ToInt32(newId);

                        return Json(new { success = true, messageId = messageId });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message");
                return Json(new { success = false, message = "Failed to send message. Please try again." });
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
    }

    // View Model for Messages
    public class MessageViewModel
    {
        public string Content { get; set; }
        public DateTime SentTime { get; set; }
        public bool IsFromEmployer { get; set; }
        public bool IsRead { get; set; }
        public string EmployerName { get; set; }
        public string Company { get; set; }
    }
}