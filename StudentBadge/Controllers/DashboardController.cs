using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using StudentBadge.Data;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using OfficeOpenXml;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using BCrypt.Net;
using Microsoft.AspNetCore.Hosting;
using StudentBadge.Services;
using System.Data;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using System.Data.Common;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Net;

public class DashboardController : Controller
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DashboardController> _logger;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly EmailService _emailService;
    private readonly BadgeService _badgeService;

    public DashboardController(IConfiguration configuration, ILogger<DashboardController> logger, 
        IWebHostEnvironment hostingEnvironment, EmailService emailService, BadgeService badgeService)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
        _logger = logger;
        _hostingEnvironment = hostingEnvironment;
        _emailService = emailService;
        _badgeService = badgeService;
    }

    private async Task<List<Student>> GetAllStudentsWithDetails()
    {
        var allStudents = new List<Student>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            
            // Check if the StudentDetails table exists
            bool tableExists = false;
            string checkTableQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = 'StudentDetails'";
            
            using (var command = new SqlCommand(checkTableQuery, connection))
            {
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                tableExists = (count > 0);
            }
            
            if (!tableExists)
            {
                // If the new table doesn't exist, return an empty list
                return allStudents;
            }
            
            // Check if GradeLevel column exists
            bool hasGradeLevelColumn = false;
            string checkColumnQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'GradeLevel'";
                
            using (var command = new SqlCommand(checkColumnQuery, connection))
            {
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                hasGradeLevelColumn = (count > 0);
            }
            
            // Build the query based on whether GradeLevel exists
            string gradeLevelSelect = hasGradeLevelColumn ? ", sd.GradeLevel" : "";
            string query = $@"
                SELECT sd.IdNumber, u.FullName, sd.Course, sd.Section, 
                       sd.IsProfileVisible, sd.ProfilePicturePath, sd.ResumeFileName, 
                       sd.Score, sd.Achievements, sd.Comments, sd.BadgeColor, sd.IsResumeVisible{gradeLevelSelect}
                FROM StudentDetails sd
                JOIN Users u ON sd.UserId = u.UserId
                WHERE u.Role = 'student'
                ORDER BY sd.Score DESC";

            using (var command = new SqlCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var student = new Student
                    {
                        IdNumber = reader["IdNumber"]?.ToString() ?? string.Empty,
                        FullName = reader["FullName"]?.ToString() ?? string.Empty,
                        Course = reader["Course"]?.ToString() ?? string.Empty,
                        Section = reader["Section"]?.ToString() ?? string.Empty,
                        IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]),
                        ProfilePicturePath = reader["ProfilePicturePath"] as string,
                        ResumePath = reader["ResumeFileName"] as string,
                        Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                        Achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "",
                        Comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "",
                        BadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None",
                        IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"])
                    };
                    
                    // Add grade level if it exists in the result
                    if (hasGradeLevelColumn && reader["GradeLevel"] != DBNull.Value)
                    {
                        student.GradeLevel = Convert.ToInt32(reader["GradeLevel"]);
                    }
                    else 
                    {
                        // Default to 0 (Unknown) if no grade level available
                        student.GradeLevel = 0;
                    }
                    
                    allStudents.Add(student);
                }
            }
        }

        // Filter out students with disabled profiles
        return allStudents.Where(s => s.IsProfileVisible).ToList();
    }
    // Add this action method to handle the Admin Dashboard
    public IActionResult AdminDashboard()
    {
        // Check if user is admin
        if (HttpContext.Session.GetString("Role") != "admin")
        {
            return RedirectToAction("Login", "Account");
        }

        // Set the admin name from session
        ViewBag.AdminName = HttpContext.Session.GetString("FullName") ?? "Admin";

        // Get students from database
        List<Student> students = new List<Student>();

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            
            // Check if new tables exist
            bool usingNewTables = false;
            string checkTableQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = 'StudentDetails'";
                
            using (var command = new SqlCommand(checkTableQuery, connection))
            {
                int count = Convert.ToInt32(command.ExecuteScalar());
                usingNewTables = (count > 0);
            }
            
            if (usingNewTables)
            {
                // Get students using new table structure
                string sql = @"
                    SELECT u.UserId, u.FullName, u.Username, u.Password, sd.IdNumber, sd.Course, sd.Section, sd.BadgeColor, sd.Score
                    FROM Users u
                    JOIN StudentDetails sd ON u.UserId = sd.UserId
                    WHERE u.Role = 'student'
                    ORDER BY u.FullName";
                    
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            students.Add(new Student
                            {
                                IdNumber = reader["IdNumber"]?.ToString() ?? "",
                                FullName = reader["FullName"]?.ToString() ?? "",
                                Username = reader["Username"]?.ToString() ?? "",
                                Password = reader["Password"]?.ToString() ?? "",
                                Course = reader["Course"]?.ToString() ?? "",
                                Section = reader["Section"]?.ToString() ?? "",
                                BadgeColor = reader["BadgeColor"]?.ToString() ?? "",
                                Score = Convert.ToInt32(reader["Score"])
                            });
                        }
                    }
                }
            }
            else
            {
                // Get students using old table structure
                string sql = @"
                    SELECT IdNumber, FullName, Username, Password, Course, Section, BadgeColor, Score
                    FROM Students
                    ORDER BY FullName";
                    
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            students.Add(new Student
                            {
                                IdNumber = reader["IdNumber"]?.ToString() ?? "",
                                FullName = reader["FullName"]?.ToString() ?? "",
                                Username = reader["Username"]?.ToString() ?? "",
                                Password = reader["Password"]?.ToString() ?? "",
                                Course = reader["Course"]?.ToString() ?? "",
                                Section = reader["Section"]?.ToString() ?? "",
                                BadgeColor = reader["BadgeColor"]?.ToString() ?? "",
                                Score = Convert.ToInt32(reader["Score"])
                            });
                        }
                    }
                }
            }
            
            // Check if the TeacherDetails table exists
            bool usingNewTeacherTables = false;
            string checkTeacherTableQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = 'TeacherDetails'";
                
            using (var command = new SqlCommand(checkTeacherTableQuery, connection))
            {
                int count = Convert.ToInt32(command.ExecuteScalar());
                usingNewTeacherTables = (count > 0);
            }
            
            // Get teachers
            var teachers = new List<Teacher>();
            
            if (usingNewTeacherTables)
            {
                // Get teachers using new tables
                string sql = @"
                    SELECT u.UserId, u.FullName, u.Username, td.Department, td.Position
                    FROM Users u
                    JOIN TeacherDetails td ON u.UserId = td.UserId
                    WHERE u.Role = 'teacher'
                    ORDER BY u.FullName";
                    
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            teachers.Add(new Teacher
                            {
                                TeacherId = reader["UserId"]?.ToString() ?? "",
                                FullName = reader["FullName"]?.ToString() ?? "",
                                Username = reader["Username"]?.ToString() ?? "",
                                Department = reader["Department"]?.ToString() ?? "",
                                Position = reader["Position"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            else
            {
                // Check if the old Teachers table exists
                bool teachersTableExists = false;
                string checkOldTeacherTableQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'Teachers'";
                    
                using (var command = new SqlCommand(checkOldTeacherTableQuery, connection))
                {
                    int count = Convert.ToInt32(command.ExecuteScalar());
                    teachersTableExists = (count > 0);
                }
                
                if (teachersTableExists)
                {
                    // Get teachers using old table
                    string sql = @"
                        SELECT TeacherId, FullName, Username, Department, Position
                        FROM Teachers
                        ORDER BY FullName";
                        
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                teachers.Add(new Teacher
                                {
                                    TeacherId = reader["TeacherId"]?.ToString() ?? "",
                                    FullName = reader["FullName"]?.ToString() ?? "",
                                    Username = reader["Username"]?.ToString() ?? "",
                                    Department = reader["Department"]?.ToString() ?? "",
                                    Position = reader["Position"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                }
            }
            
            ViewBag.Teachers = teachers;
            ViewBag.UsingNewTables = usingNewTables;
        }
        
        return View(students);
    }

    public IActionResult GetEmployerMessageHistory(string studentId)
    {
        try
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }

            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer session not found. Please log in again." });
            }

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                    conn
                );
                bool useNewTable = (int)checkTableCmd.ExecuteScalar() > 0;

                string query;
                if (useNewTable)
                {
                    query = @"
                        SELECT 
                            Message as Content,
                            SentTime,
                            IsFromEmployer,
                            u.FullName as EmployerName,
                            ed.Company
                        FROM EmployerStudentMessages m
                        JOIN Users u ON m.EmployerId = u.UserId
                        JOIN EmployerDetails ed ON u.UserId = ed.UserId
                        WHERE (m.EmployerId = @EmployerId AND m.StudentId = @StudentId)
                        ORDER BY SentTime ASC";
                }
                else
                {
                    query = @"
                        SELECT 
                            MessageContent as Content,
                            SentDateTime as SentTime,
                            IsFromEmployer,
                            e.FullName as EmployerName,
                            e.Company
                        FROM Messages m
                        JOIN Employers e ON m.EmployerId = e.EmployerId
                        WHERE (m.EmployerId = @EmployerId AND m.StudentId = @StudentId)
                        ORDER BY m.SentDateTime ASC";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);
                    cmd.Parameters.AddWithValue("@StudentId", studentId);

                    var messages = new List<object>();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            messages.Add(new
                            {
                                content = reader["Content"].ToString(),
                                sentTime = Convert.ToDateTime(reader["SentTime"]),
                                isFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"]),
                                employerName = reader["EmployerName"].ToString(),
                                company = reader["Company"].ToString()
                            });
                        }
                    }

                    return Json(new { success = true, messages = messages });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetEmployerMessageHistory: {ex.Message}");
            return Json(new { success = false, message = "Error loading messages. Please try again." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] MessageModel model)
    {
        try
        {
            if (string.IsNullOrEmpty(model.StudentId) || string.IsNullOrEmpty(model.Message))
            {
                return Json(new { success = false, message = "Student ID and message are required." });
            }

            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer session not found. Please log in again." });
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Check student's chat availability
                bool isChatAvailable = true; // Default to available if column doesn't exist
                string startTime = "00:00";
                string endTime = "23:59";
                
                // Check if chat availability columns exist
                bool hasChatSettings = false;
                string checkColumnsQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'IsChatAvailable'";
                
                using (var checkCommand = new SqlCommand(checkColumnsQuery, conn))
                {
                    int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                    hasChatSettings = (count > 0);
                }
                
                if (hasChatSettings)
                {
                    // Get chat availability settings
                    string availabilityQuery = @"
                        SELECT IsChatAvailable, ChatStartTime, ChatEndTime
                        FROM StudentDetails
                        WHERE IdNumber = @StudentId";
                        
                    using (var availCommand = new SqlCommand(availabilityQuery, conn))
                    {
                        availCommand.Parameters.AddWithValue("@StudentId", model.StudentId);
                        
                        using (var reader = await availCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                isChatAvailable = reader["IsChatAvailable"] != DBNull.Value && 
                                                 Convert.ToBoolean(reader["IsChatAvailable"]);
                                
                                startTime = reader["ChatStartTime"] != DBNull.Value ? 
                                           reader["ChatStartTime"].ToString() : "00:00";
                                
                                endTime = reader["ChatEndTime"] != DBNull.Value ? 
                                         reader["ChatEndTime"].ToString() : "23:59";
                            }
                        }
                    }
                    
                    // Check if current time is within allowed chat hours
                    if (isChatAvailable)
                    {
                        DateTime now = DateTime.Now;
                        DateTime currentTime = new DateTime(2000, 1, 1, now.Hour, now.Minute, 0);
                        
                        // Parse start and end times
                        DateTime start = DateTime.Parse("2000-01-01 " + startTime);
                        DateTime end = DateTime.Parse("2000-01-01 " + endTime);
                        
                        if (currentTime < start || currentTime > end)
                        {
                            return Json(new { 
                                success = false, 
                                message = $"Student is only available for chat between {startTime} and {endTime}."
                            });
                        }
                    }
                    else
                    {
                        return Json(new { 
                            success = false, 
                            message = "Student is not available for chat at this time."
                        });
                    }
                }

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                string query;
                if (useNewTable)
                {
                    query = @"
                        INSERT INTO EmployerStudentMessages (
                            EmployerId,
                            StudentId,
                            Message,
                            SentTime,
                            IsFromEmployer,
                            IsRead
                        ) VALUES (
                            @EmployerId,
                            @StudentId,
                            @Message,
                            @SentTime,
                            1,
                            0
                        )";
                }
                else
                {
                    query = @"
                        INSERT INTO Messages (
                            EmployerId,
                            StudentId,
                            MessageContent,
                            SentDateTime,
                            IsFromEmployer,
                            IsRead
                        ) VALUES (
                            @EmployerId,
                            @StudentId,
                            @Message,
                            @SentTime,
                            1,
                            0
                        )";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);
                    cmd.Parameters.AddWithValue("@StudentId", model.StudentId);
                    cmd.Parameters.AddWithValue("@Message", model.Message);
                    cmd.Parameters.AddWithValue("@SentTime", DateTime.Now);

                    int rowsAffected = await cmd.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true, message = "Message sent successfully." });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to send message. Please try again." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in SendMessage: {ex.Message}");
            return Json(new { success = false, message = "Error sending message. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployerChats(string employerId)
    {
        try
        {
            _logger.LogInformation($"GetEmployerChats called with employerId: {employerId}");
            
            // Handle literal '@ViewBag.EmployerId' passed as a string
            if (string.IsNullOrEmpty(employerId) || employerId.Contains("@ViewBag"))
            {
                _logger.LogWarning($"GetEmployerChats: Invalid employer ID: '{employerId}'. Attempting to get from session.");
                // Try to get from session as fallback
                employerId = HttpContext.Session.GetString("EmployerId");
                
                if (string.IsNullOrEmpty(employerId))
                {
                    _logger.LogError("GetEmployerChats: No valid employer ID in parameters or session");
                    return Json(new { 
                        success = false, 
                        message = "Invalid employer ID. Please refresh the page or log in again.",
                        error = "INVALID_EMPLOYER_ID"
                    });
                }
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;
                _logger.LogInformation($"Using new table structure: {useNewTable}");

                // Get all students who have had conversations with this employer
                string query;
                
                // First check if StudentDetails/Users table exists (new structure)
                var checkStudentDetailsCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    conn
                );
                bool hasStudentDetails = (int)await checkStudentDetailsCmd.ExecuteScalarAsync() > 0;
                
                var checkUsersCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users'",
                    conn
                );
                bool hasUsersTable = (int)await checkUsersCmd.ExecuteScalarAsync() > 0;
                
                var checkStudentsCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Students'",
                    conn
                );
                bool hasStudentsTable = (int)await checkStudentsCmd.ExecuteScalarAsync() > 0;
                
                if (useNewTable)
                {
                    // Check if the Messages table has any data
                    var countQuery = new SqlCommand(
                        "SELECT COUNT(*) FROM EmployerStudentMessages WHERE EmployerId = @EmployerId",
                        conn
                    );
                    countQuery.Parameters.AddWithValue("@EmployerId", employerId);
                    int messageCount = (int)await countQuery.ExecuteScalarAsync();
                    _logger.LogInformation($"Found {messageCount} messages in EmployerStudentMessages table");
                    
                    // Query for new table structure - determine best way to get student names
                    if (hasStudentDetails && hasUsersTable)
                    {
                        // Best case: We have full StudentDetails and Users tables
                        query = @"
                            SELECT DISTINCT 
                                esm.StudentId,
                                u.FullName,
                                sd.IdNumber,
                                sd.Course,
                                sd.Section
                            FROM EmployerStudentMessages esm
                            LEFT JOIN StudentDetails sd ON esm.StudentId = sd.IdNumber
                            LEFT JOIN Users u ON sd.UserId = u.UserId
                            WHERE esm.EmployerId = @EmployerId";
                    }
                    else if (hasUsersTable)
                    {
                        // Try direct join with Users
                        query = @"
                            SELECT DISTINCT 
                                esm.StudentId,
                                u.FullName,
                                u.UserId as IdNumber
                            FROM EmployerStudentMessages esm
                            LEFT JOIN Users u ON esm.StudentId = u.UserId
                            WHERE esm.EmployerId = @EmployerId";
                    }
                    else if (hasStudentsTable)
                    {
                        // Try joining with old Students table
                        query = @"
                            SELECT DISTINCT 
                                esm.StudentId,
                                s.FullName,
                                s.IdNumber
                            FROM EmployerStudentMessages esm
                            LEFT JOIN Students s ON esm.StudentId = s.IdNumber
                            WHERE esm.EmployerId = @EmployerId";
                    }
                    else
                    {
                        // Fallback to just getting the IDs without names
                        query = @"
                            SELECT DISTINCT 
                                StudentId
                            FROM EmployerStudentMessages
                            WHERE EmployerId = @EmployerId";
                    }
                }
                else
                {
                    // Check if the Messages table has any data
                    var countQuery = new SqlCommand(
                        "SELECT COUNT(*) FROM Messages WHERE EmployerId = @EmployerId",
                        conn
                    );
                    countQuery.Parameters.AddWithValue("@EmployerId", employerId);
                    int messageCount = (int)await countQuery.ExecuteScalarAsync();
                    _logger.LogInformation($"Found {messageCount} messages in Messages table");
                    
                    // For old structure, try direct join with Students
                    if (hasStudentsTable)
                    {
                        query = @"
                            SELECT DISTINCT 
                                m.StudentId,
                                s.FullName,
                                s.IdNumber
                            FROM Messages m
                            LEFT JOIN Students s ON m.StudentId = s.IdNumber
                            WHERE m.EmployerId = @EmployerId";
                    }
                    else
                    {
                        // Fallback to just getting the IDs
                        query = @"
                            SELECT DISTINCT 
                                StudentId
                            FROM Messages
                            WHERE EmployerId = @EmployerId";
                    }
                }

                _logger.LogInformation($"Executing chats query: {query}");
                
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        var chats = new List<object>();
                        int count = 0;
                        
                        while (await reader.ReadAsync())
                        {
                            count++;
                            // Extract student info
                            var studentId = reader["StudentId"].ToString();
                            
                            // Determine name - use FullName if it exists, otherwise default
                            string fullName = "Student"; 
                            if (HasColumn(reader, "FullName") && reader["FullName"] != DBNull.Value)
                            {
                                fullName = reader["FullName"].ToString();
                            }
                            
                            // Get student IdNumber if available
                            string idNumber = studentId;
                            if (HasColumn(reader, "IdNumber") && reader["IdNumber"] != DBNull.Value)
                            {
                                idNumber = reader["IdNumber"].ToString();
                            }
                            
                            // If we still don't have a name, try to get it directly from one of the tables
                            if (fullName == "Student" && !string.IsNullOrEmpty(studentId))
                            {
                                fullName = await GetStudentNameById(studentId, conn) ?? $"Student {studentId}";
                            }
                            
                            _logger.LogInformation($"Found student with ID: {studentId}, Name: {fullName}");
                            
                            chats.Add(new
                            {
                                studentId = studentId,
                                name = fullName,
                                idNumber = idNumber,
                                recentMessage = await GetMostRecentMessagePreview(employerId, studentId, conn)
                            });
                        }

                        _logger.LogInformation($"Found {count} chats for employer {employerId}");
                        return Json(new { success = true, chats = chats });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetEmployerChats: {ex.Message}");
            _logger.LogError($"Stack trace: {ex.StackTrace}");
            return Json(new { success = false, message = "Error loading conversations. Please try again." });
        }
    }
    
    // Helper method to check if a column exists in the reader
    private bool HasColumn(SqlDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
    
    // Helper method to get a student name by ID
    private async Task<string> GetStudentNameById(string studentId, SqlConnection conn)
    {
        // Try to find in Students table
        if (await TableExists(conn, "Students"))
        {
            using (var cmd = new SqlCommand("SELECT FullName FROM Students WHERE StudentId = @StudentId OR IdNumber = @StudentId", conn))
            {
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return result.ToString();
                }
            }
        }
        
        // Try to find in Users table
        if (await TableExists(conn, "Users"))
        {
            using (var cmd = new SqlCommand("SELECT FullName FROM Users WHERE UserId = @UserId", conn))
            {
                cmd.Parameters.AddWithValue("@UserId", studentId);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return result.ToString();
                }
            }
            
            // Try by IdNumber if it exists
            if (await ColumnExists(conn, "Users", "IdNumber"))
            {
                using (var cmd = new SqlCommand("SELECT FullName FROM Users WHERE IdNumber = @StudentId", conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString();
                    }
                }
            }
        }
        
        // Try to find in StudentDetails table
        if (await TableExists(conn, "StudentDetails"))
        {
            using (var cmd = new SqlCommand(@"
                SELECT u.FullName 
                FROM StudentDetails sd
                JOIN Users u ON sd.UserId = u.UserId
                WHERE sd.IdNumber = @StudentId", conn))
            {
                cmd.Parameters.AddWithValue("@StudentId", studentId);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return result.ToString();
                }
            }
        }
        
        return null;
    }
    
    // Helper method to check if a column exists in a table
    private async Task<bool> ColumnExists(SqlConnection connection, string tableName, string columnName)
    {
        var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName",
            connection
        );
        cmd.Parameters.AddWithValue("@TableName", tableName);
        cmd.Parameters.AddWithValue("@ColumnName", columnName);
        return (int)await cmd.ExecuteScalarAsync() > 0;
    }

    private async Task<object> GetMostRecentMessagePreview(string employerId, string studentId, SqlConnection existingConnection)
    {
        try
        {
            // Check if new table structure exists without opening a new connection
            var checkTableCmd = new SqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerStudentMessages'",
                existingConnection
            );
            bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

            string query;
            if (useNewTable)
            {
                query = @"
                    SELECT TOP 1 
                        Message as Content, 
                        SentTime, 
                        IsRead, 
                        IsFromEmployer
                    FROM EmployerStudentMessages
                    WHERE EmployerId = @EmployerId AND StudentId = @StudentId
                    ORDER BY SentTime DESC";
            }
            else
            {
                query = @"
                    SELECT TOP 1 
                        MessageContent as Content, 
                        SentDateTime as SentTime, 
                        IsRead, 
                        IsFromEmployer
                    FROM Messages
                    WHERE EmployerId = @EmployerId AND StudentId = @StudentId
                    ORDER BY SentDateTime DESC";
            }

            using (SqlCommand cmd = new SqlCommand(query, existingConnection))
            {
                cmd.Parameters.AddWithValue("@EmployerId", employerId);
                cmd.Parameters.AddWithValue("@StudentId", studentId);

                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new
                        {
                            content = reader["Content"].ToString(),
                            sentTime = reader["SentTime"],
                            isRead = (bool)reader["IsRead"],
                            isFromEmployer = (bool)reader["IsFromEmployer"]
                        };
                    }
                    else
                    {
                        return new
                        {
                            content = "No messages yet",
                            sentTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                            isRead = true,
                            isFromEmployer = false
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log the error but return a default message object
            Console.WriteLine($"Error in GetMostRecentMessagePreview: {ex.Message}");
            return new
            {
                content = "Error retrieving message",
                sentTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                isRead = true,
                isFromEmployer = false
            };
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentBasicInfo(string studentId)
    {
        try
        {
            _logger.LogInformation($"GetStudentBasicInfo called with studentId: {studentId}");
            
            if (string.IsNullOrEmpty(studentId))
            {
                _logger.LogWarning("GetStudentBasicInfo: Student ID is empty");
                return Json(new { 
                    success = true, 
                    student = new { 
                        fullName = "Student",
                        idNumber = "Unknown",
                        course = "Unknown Course",
                        section = "",
                        profilePicturePath = "/images/blank.jpg"
                    }
                });
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // First, try using the same query as GetStudentProfileForEmployer (which works)
                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                string query;
                if (useNewTable)
                {
                    query = @"
                        SELECT 
                            u.FullName,
                            sd.IdNumber,
                            sd.Course,
                            sd.Section,
                            sd.ProfilePicturePath
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE sd.IdNumber = @StudentId";
                }
                else
                {
                    query = @"
                        SELECT 
                            FullName,
                            IdNumber,
                            Course,
                            Section,
                            ProfilePicturePath
                        FROM Students
                        WHERE IdNumber = @StudentId";
                }

                _logger.LogInformation($"Executing primary query: {query}");
                
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var student = new
                            {
                                fullName = reader["FullName"].ToString(),
                                idNumber = reader["IdNumber"].ToString(),
                                course = reader["Course"]?.ToString() ?? "",
                                section = reader["Section"]?.ToString() ?? "",
                                profilePicturePath = reader["ProfilePicturePath"] != DBNull.Value ? 
                                    reader["ProfilePicturePath"].ToString() : "/images/blank.jpg"
                            };

                            _logger.LogInformation($"Found student by IdNumber match: {student.fullName}");
                            return Json(new { success = true, student = student });
                        }
                    }
                }
                
                // If student not found by IdNumber, try UserId lookup
                if (useNewTable)
                {
                    query = @"
                        SELECT 
                            u.FullName,
                            sd.IdNumber,
                            sd.Course,
                            sd.Section,
                            sd.ProfilePicturePath
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE u.UserId = @StudentId";
                    
                    _logger.LogInformation($"Trying secondary query with UserId: {query}");
                    
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@StudentId", studentId);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var student = new
                                {
                                    fullName = reader["FullName"].ToString(),
                                    idNumber = reader["IdNumber"].ToString(),
                                    course = reader["Course"]?.ToString() ?? "",
                                    section = reader["Section"]?.ToString() ?? "",
                                    profilePicturePath = reader["ProfilePicturePath"] != DBNull.Value ? 
                                        reader["ProfilePicturePath"].ToString() : "/images/blank.jpg"
                                };

                                _logger.LogInformation($"Found student by UserId match: {student.fullName}");
                                return Json(new { success = true, student = student });
                            }
                        }
                    }
                    
                    // Try direct lookup in Users table as last resort
                    query = @"
                        SELECT 
                            FullName,
                            UserId,
                            '' AS Course,
                            '' AS Section,
                            ProfilePicturePath
                        FROM Users
                        WHERE UserId = @StudentId";
                    
                    _logger.LogInformation($"Trying final query in Users table: {query}");
                    
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@StudentId", studentId);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var student = new
                                {
                                    fullName = reader["FullName"].ToString(),
                                    idNumber = reader["UserId"].ToString(),
                                    course = "",
                                    section = "",
                                    profilePicturePath = reader["ProfilePicturePath"] != DBNull.Value ? 
                                        reader["ProfilePicturePath"].ToString() : "/images/blank.jpg"
                                };

                                _logger.LogInformation($"Found student in Users table: {student.fullName}");
                                return Json(new { success = true, student = student });
                            }
                        }
                    }
                }
                
                // As a last resort, check if studentId exists in Messages tables
                var messageTableExists = await TableExists(conn, "Messages");
                var newMessageTableExists = await TableExists(conn, "EmployerStudentMessages");
                
                if (messageTableExists || newMessageTableExists)
                {
                    _logger.LogInformation("Checking message tables for student ID references");
                    
                    // Try to get at least a name from Students table if it exists
                    if (await TableExists(conn, "Students"))
                    {
                        query = @"
                            SELECT FullName 
                            FROM Students 
                            WHERE StudentId = @StudentId";
                            
                        using (var cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", studentId);
                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    string fullName = reader["FullName"].ToString();
                                    _logger.LogInformation($"Found student name in Students table: {fullName}");
                                    
                                    return Json(new {
                                        success = true,
                                        student = new {
                                            fullName = fullName,
                                            idNumber = studentId,
                                            course = "",
                                            section = "",
                                            profilePicturePath = "/images/blank.jpg"
                                        }
                                    });
                                }
                            }
                        }
                    }
                    
                    // Try to get a name from Users table as last resort
                    if (await TableExists(conn, "Users"))
                    {
                        query = @"
                            SELECT FullName 
                            FROM Users 
                            WHERE UserId = @StudentId";
                            
                        using (var cmd = new SqlCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@StudentId", studentId);
                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    string fullName = reader["FullName"].ToString();
                                    _logger.LogInformation($"Found student name in Users table: {fullName}");
                                    
                                    return Json(new {
                                        success = true,
                                        student = new {
                                            fullName = fullName,
                                            idNumber = studentId,
                                            course = "",
                                            section = "",
                                            profilePicturePath = "/images/blank.jpg"
                                        }
                                    });
                                }
                            }
                        }
                    }
                }
                
                // If we get here, we couldn't find the student - return dummy data as a fallback
                _logger.LogWarning($"Student not found with ID: {studentId} - returning fallback data");
                return Json(new { 
                    success = true, 
                    student = new { 
                        fullName = "Student " + studentId, 
                        idNumber = studentId,
                        course = "Unknown Course",
                        section = "",
                        profilePicturePath = "/images/blank.jpg"
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetStudentBasicInfo: {ex.Message}");
            _logger.LogError($"Stack trace: {ex.StackTrace}");
            
            // Return fallback data even on error
            return Json(new { 
                success = true, 
                student = new { 
                    fullName = "Student", 
                    idNumber = studentId,
                    course = "Unknown Course",
                    section = "",
                    profilePicturePath = "/images/blank.jpg" 
                }
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentProfileForEmployer(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Student ID is required" });
        }

        try
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                // Check if grade columns exist
                bool gradeColumnsExist = false;
                if (useNewTable)
                {
                    var checkColumnsCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'FirstYearGrade'",
                        conn
                    );
                    gradeColumnsExist = (int)await checkColumnsCmd.ExecuteScalarAsync() > 0;
                }

                string query;
                if (useNewTable)
                {
                    query = @"
                        SELECT 
                            u.FullName,
                            sd.IdNumber,
                            sd.Course,
                            sd.Section,
                            sd.Score,
                            sd.Achievements,
                            sd.Comments,
                            sd.BadgeColor,
                            sd.ProfilePicturePath,
                            sd.IsProfileVisible,
                            sd.IsResumeVisible,
                            sd.ResumeFileName";
                            
                    // Only add grade columns if they exist
                    if (gradeColumnsExist)
                    {
                        query += @",
                            sd.FirstYearGrade,
                            sd.SecondYearGrade,
                            sd.ThirdYearGrade,
                            sd.FourthYearGrade";
                    }
                            
                    query += @"
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE sd.IdNumber = @StudentId";
                }
                else
                {
                    query = @"
                        SELECT 
                            FullName,
                            IdNumber,
                            Course,
                            Section,
                            Score,
                            Achievements,
                            Comments,
                            BadgeColor,
                            ProfilePicturePath,
                            1 as IsProfileVisible,
                            IsResumeVisible,
                            ResumeFileName
                        FROM Students
                        WHERE IdNumber = @StudentId";
                }

                _logger.LogInformation($"Fetching student profile data for employer: {query}");

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Create student object
                            var studentDict = new Dictionary<string, object>
                            {
                                { "FullName", reader["FullName"].ToString() },
                                { "IdNumber", reader["IdNumber"].ToString() },
                                { "Course", reader["Course"].ToString() },
                                { "Section", reader["Section"].ToString() },
                                { "Score", reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0 },
                                { "Achievements", reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "" },
                                { "Comments", reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "" },
                                { "BadgeColor", reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None" },
                                { "ProfilePicture", reader["ProfilePicturePath"] != DBNull.Value ? reader["ProfilePicturePath"].ToString() : "/images/blank.jpg" },
                                { "IsProfileVisible", Convert.ToBoolean(reader["IsProfileVisible"]) },
                                { "IsResumeVisible", Convert.ToBoolean(reader["IsResumeVisible"]) },
                                { "Resume", reader["ResumeFileName"] != DBNull.Value ? reader["ResumeFileName"].ToString() : null }
                            };
                            
                            // Add grade columns if they exist
                            if (gradeColumnsExist && useNewTable)
                            {
                                studentDict["FirstYearGrade"] = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : null;
                                studentDict["SecondYearGrade"] = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : null;
                                studentDict["ThirdYearGrade"] = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : null;
                                studentDict["FourthYearGrade"] = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : null;
                            }
                            else
                            {
                                // Add null values for grades if columns don't exist
                                studentDict["FirstYearGrade"] = null;
                                studentDict["SecondYearGrade"] = null;
                                studentDict["ThirdYearGrade"] = null;
                                studentDict["FourthYearGrade"] = null;
                            }

                            _logger.LogInformation($"Successfully fetched student profile: {studentDict["FullName"]} with Achievements: {(studentDict["Achievements"]?.ToString()?.Length > 0 ? "Yes" : "No")} and Comments: {(studentDict["Comments"]?.ToString()?.Length > 0 ? "Yes" : "No")}");
                            return Json(new { success = true, student = studentDict });
                        }
                        else
                        {
                            _logger.LogWarning($"Student not found or profile not visible: {studentId}");
                            return Json(new { success = false, message = "Student not found or profile not visible" });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting student profile for employer. StudentId: {studentId}");
            return Json(new { success = false, message = "Error loading profile" });
        }
    }
        
    private async Task<bool> TableExists(SqlConnection connection, string tableName)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        
        string query = @"
            SELECT COUNT(1) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_NAME = @TableName";
        
        // Create a command without a transaction
        using (var command = new SqlCommand(query, connection))
        {
            try
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("transaction"))
            {
                // If we get a transaction-related error, we're likely in a transaction
                // Log the error for debugging
                _logger.LogWarning($"Transaction error in TableExists: {ex.Message}. Retrying with explicit transaction.");
                
                // This is a workaround - in a real fix, we'd need to pass the transaction from calling methods
                return await CheckTableExistsWithoutTransaction(tableName);
            }
        }
    }
    
    // Alternative method to check table existence without using transactions
    private async Task<bool> CheckTableExistsWithoutTransaction(string tableName)
    {
        // Create a new connection for this check
        using (var separateConnection = new SqlConnection(_connectionString))
        {
            await separateConnection.OpenAsync();
            
            string query = @"
                SELECT COUNT(1) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
                
            using (var command = new SqlCommand(query, separateConnection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
    }

    private string GetBadgeNameFromScore(int score)
    {
        if (score >= 95) return "Platinum Scholar";
        else if (score >= 85) return "Gold Achiever";
        else if (score >= 75) return "Silver Performer";
        else if (score >= 65) return "Bronze Learner";
        else if (score >= 50) return "Rising Star";
        else return "Needs Improvement";
    }
    
    [HttpPost]
    public async Task<IActionResult> UpdateStudentAchievements([FromBody] StudentAchievementModel model)
    {
        try
        {
            // Log the raw request to help diagnose model binding issues
            string requestBody = "";
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, true, 1024, true))
            {
                Request.Body.Position = 0;  // Reset position to read from start
                requestBody = await reader.ReadToEndAsync();
                _logger.LogInformation($"Raw request body: {requestBody}");
            }
            
            if (model == null)
            {
                _logger.LogError("UpdateStudentAchievements: model is null. Raw request: " + requestBody);
                return Json(new { success = false, message = "Invalid request data." });
            }

            if (string.IsNullOrEmpty(model.StudentId))
            {
                _logger.LogError("UpdateStudentAchievements: StudentId is null or empty");
                return Json(new { success = false, message = "Student ID is required." });
            }
                
            // Only validate score if it's provided
            if (model.Score < 0 || model.Score > 100)
            {
                _logger.LogError($"UpdateStudentAchievements: Invalid score {model.Score} for student {model.StudentId}");
                return Json(new { success = false, message = "Score must be between 0 and 100." });
            }
                
            _logger.LogInformation($"Updating student: {model.StudentId}, Score: {model.Score}, Achievements length: {model.Achievements?.Length ?? 0}, Comments length: {model.Comments?.Length ?? 0}");
                
            using (var connection = new SqlConnection(_connectionString))
            {
                try 
                {
                    await connection.OpenAsync();
                    
                    // Get badge color based on score
                    string badgeColor = model.Score >= 95 ? "platinum" : 
                                       model.Score >= 85 ? "gold" : 
                                       model.Score >= 75 ? "silver" : 
                                       model.Score >= 65 ? "bronze" : 
                                       model.Score >= 50 ? "rising-star" : 
                                       model.Score >= 1 ? "needs" : "none";
                    
                    // Check if we're using the new database structure
                    bool usingNewTables = false;
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'StudentDetails'";
                        
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        usingNewTables = (count > 0);
                    }
                    
                    // First check if the student exists
                    string studentCheckSql = usingNewTables
                        ? "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId"
                        : "SELECT COUNT(*) FROM Students WHERE IdNumber = @StudentId";
                    
                    bool studentExists = false;
                    using (var checkCmd = new SqlCommand(studentCheckSql, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@StudentId", model.StudentId);
                        int studentCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                        studentExists = (studentCount > 0);
                        
                        if (!studentExists)
                        {
                            _logger.LogWarning($"Student not found: {model.StudentId}");
                            return Json(new { success = false, message = "Student not found in database." });
                        }
                    }
                    
                    // Build update SQL safely
                    string updateTable = usingNewTables ? "StudentDetails" : "Students";
                    string updateSql = $"UPDATE {updateTable} SET Score = @Score, BadgeColor = @BadgeColor";
                    
                    if (model.Achievements != null)
                    {
                        updateSql += ", Achievements = @Achievements";
                    }
                    
                    if (model.Comments != null)
                    {
                        updateSql += ", Comments = @Comments";
                    }
                    
                    updateSql += " WHERE IdNumber = @StudentId";
                    
                    // Execute the update query
                    using (var command = new SqlCommand(updateSql, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", model.StudentId);
                        command.Parameters.AddWithValue("@Score", model.Score);
                        command.Parameters.AddWithValue("@BadgeColor", badgeColor);
                        
                        if (model.Achievements != null)
                        {
                            command.Parameters.AddWithValue("@Achievements", model.Achievements);
                        }
                        
                        if (model.Comments != null)
                        {
                            command.Parameters.AddWithValue("@Comments", model.Comments);
                        }
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Updated {rowsAffected} rows for student {model.StudentId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Database error updating student {model.StudentId}: {ex.Message}");
                    throw;
                }
            }
            
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating student {model?.StudentId ?? "unknown"}: {ex.Message}");
            
            // Additional debug information
            if (ex.InnerException != null)
            {
                _logger.LogError($"Inner exception: {ex.InnerException.Message}");
            }
            
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStudentScore(string studentId, int score, string achievements = null, string comments = null)
    {
        try
        {
            _logger.LogInformation($"UpdateStudentScore called with: studentId={studentId}, score={score}, has achievements={achievements != null}, has comments={comments != null}");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }
                
            if (score < 0 || score > 100)
            {
                return Json(new { success = false, message = "Score must be between 0 and 100." });
            }
                
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Get grade level to apply year-based bonus
                int gradeLevel = await GetStudentGradeLevel(connection, studentId);
                
                // Store the original score but calculate display score with bonus
                int displayScore = ApplyYearLevelBonus(score, gradeLevel);
                
                // Log the bonus calculation
                if (displayScore > score)
                {
                    _logger.LogInformation($"Year bonus applied for studentId={studentId}: base score={score}, display score={displayScore}, gradeLevel={gradeLevel}");
                }
                
                // Get badge color based on the display score
                string badgeColor = displayScore >= 95 ? "platinum" : 
                                   displayScore >= 85 ? "gold" : 
                                   displayScore >= 75 ? "silver" : 
                                   displayScore >= 65 ? "bronze" : 
                                   displayScore >= 50 ? "rising-star" : 
                                   displayScore >= 1 ? "needs" : "none";
                
                // Check if we're using the new database structure
                bool usingNewTables = false;
                string checkTableQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'StudentDetails'";
                    
                using (var command = new SqlCommand(checkTableQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    usingNewTables = (count > 0);
                }
                
                // Check if student exists
                string studentCheckSql = usingNewTables
                    ? "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId"
                    : "SELECT COUNT(*) FROM Students WHERE IdNumber = @StudentId";
                
                bool studentExists = false;
                using (var checkCmd = new SqlCommand(studentCheckSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@StudentId", studentId);
                    int studentCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    studentExists = (studentCount > 0);
                    
                    if (!studentExists)
                    {
                        return Json(new { success = false, message = "Student not found in database." });
                    }
                }
                
                // Build update SQL
                string updateTable = usingNewTables ? "StudentDetails" : "Students";
                
                // Basic update always includes score and badge color
                string updateSql = $"UPDATE {updateTable} SET Score = @Score, BadgeColor = @BadgeColor";
                
                // Only include other fields if they are provided
                if (achievements != null)
                {
                    updateSql += ", Achievements = @Achievements";
                }
                
                if (comments != null)
                {
                    updateSql += ", Comments = @Comments";
                }
                
                updateSql += " WHERE IdNumber = @StudentId";
                
                using (var command = new SqlCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@Score", displayScore);
                    command.Parameters.AddWithValue("@BadgeColor", badgeColor);
                    
                    if (achievements != null)
                    {
                        command.Parameters.AddWithValue("@Achievements", achievements);
                    }
                    
                    if (comments != null)
                    {
                        command.Parameters.AddWithValue("@Comments", comments);
                    }
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Updated {rowsAffected} rows for student {studentId}");
                    
                    if (rowsAffected == 0)
                    {
                        return Json(new { success = false, message = "No changes were made. Please try again." });
                    }
                }
                
                // Use the BadgeService to ensure badge color is properly updated
                try
                {
                    await _badgeService.UpdateBadgeColor(studentId);
                    _logger.LogInformation($"Badge color updated via BadgeService for student {studentId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating badge color with BadgeService for student {studentId}, but score update was successful");
                }
            }
            
            return Json(new { success = true, message = "Changes saved successfully!" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating student {studentId}: {ex.Message}");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStudentGrades(string studentId, decimal? firstYearGrade, decimal? secondYearGrade, 
                                                         decimal? thirdYearGrade, decimal? fourthYearGrade,
                                                         string achievements, string comments)
    {
        try
        {
            _logger.LogInformation($"UpdateStudentGrades called with: studentId={studentId}, firstYearGrade={firstYearGrade}, " +
                                    $"secondYearGrade={secondYearGrade}, thirdYearGrade={thirdYearGrade}, " +
                                    $"fourthYearGrade={fourthYearGrade}, " +
                                    $"achievements count: {(string.IsNullOrEmpty(achievements) ? 0 : achievements.Split('|', StringSplitOptions.RemoveEmptyEntries).Length)}, " +
                                    $"comments count: {(string.IsNullOrEmpty(comments) ? 0 : comments.Split('|', StringSplitOptions.RemoveEmptyEntries).Length)}");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }
                
            // Validate all grades are within valid range (0-100)
            if ((firstYearGrade.HasValue && (firstYearGrade < 0 || firstYearGrade > 100)) ||
                (secondYearGrade.HasValue && (secondYearGrade < 0 || secondYearGrade > 100)) ||
                (thirdYearGrade.HasValue && (thirdYearGrade < 0 || thirdYearGrade > 100)) ||
                (fourthYearGrade.HasValue && (fourthYearGrade < 0 || fourthYearGrade > 100)))
            {
                return Json(new { success = false, message = "All grades must be between 0 and 100." });
            }
                
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if StudentDetails table exists
                bool usingNewTables = await TableExists(connection, "StudentDetails");
                
                if (!usingNewTables)
                {
                    return Json(new { success = false, message = "New grade columns not available in old database structure." });
                }
                
                // Check if grade columns exist
                bool hasGradeColumns = await ColumnExists(connection, "StudentDetails", "FirstYearGrade");
                
                if (!hasGradeColumns)
                {
                    return Json(new { success = false, message = "Grade columns not found. Please run the AddGradeColumns.sql script first." });
                }
                
                // Check if student exists
                string studentCheckSql = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                
                bool studentExists = false;
                using (var checkCmd = new SqlCommand(studentCheckSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@StudentId", studentId);
                    int studentCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    studentExists = (studentCount > 0);
                    
                    if (!studentExists)
                    {
                        return Json(new { success = false, message = "Student not found in database." });
                    }
                }
                
                // First retrieve current student data to calculate the new score
                var student = new Student();
                
                string getSql = @"
                    SELECT FirstYearGrade, SecondYearGrade, ThirdYearGrade, FourthYearGrade
                    FROM StudentDetails
                    WHERE IdNumber = @StudentId";
                    
                using (var command = new SqlCommand(getSql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Use provided values or existing values if not provided
                            student.FirstYearGrade = firstYearGrade ?? 
                                (reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : null);
                                
                            student.SecondYearGrade = secondYearGrade ?? 
                                (reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : null);
                                
                            student.ThirdYearGrade = thirdYearGrade ?? 
                                (reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : null);
                                
                            student.FourthYearGrade = fourthYearGrade ?? 
                                (reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : null);
                        }
                    }
                }
                
                // Set achievements for score calculation
                student.Achievements = achievements;
                student.Comments = comments;
                
                // Calculate new overall score based on grades and achievements
                decimal newScore = student.CalculateOverallScore();
                
                // Get badge color using BadgeService for consistency
                string badgeColor = BadgeService.GetBadgeColorForScore(newScore);
                    
                // Get badge name based on new score
                string badgeName = GetBadgeNameFromScore((int)newScore);

                // Update the database with new values
                string updateSql = @"
                    UPDATE StudentDetails
                    SET FirstYearGrade = @FirstYearGrade,
                        SecondYearGrade = @SecondYearGrade,
                        ThirdYearGrade = @ThirdYearGrade,
                        FourthYearGrade = @FourthYearGrade,
                        Achievements = @Achievements,
                        Comments = @Comments,
                        Score = @Score,
                        BadgeColor = @BadgeColor
                    WHERE IdNumber = @StudentId";
                    
                using (var command = new SqlCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@FirstYearGrade", (object)student.FirstYearGrade ?? DBNull.Value);
                    command.Parameters.AddWithValue("@SecondYearGrade", (object)student.SecondYearGrade ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ThirdYearGrade", (object)student.ThirdYearGrade ?? DBNull.Value);
                    command.Parameters.AddWithValue("@FourthYearGrade", (object)student.FourthYearGrade ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Achievements", string.IsNullOrEmpty(achievements) ? DBNull.Value : achievements);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(comments) ? DBNull.Value : comments);
                    command.Parameters.AddWithValue("@Score", newScore);
                    command.Parameters.AddWithValue("@BadgeColor", badgeColor);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Updated {rowsAffected} rows for student {studentId}, new score: {newScore}");
                    
                    if (rowsAffected == 0)
                    {
                        return Json(new { success = false, message = "No changes were made. Please try again." });
                    }
                }
                
                // Badge color is already set correctly using BadgeService.GetBadgeColorForScore
                // No need to call UpdateBadgeColor again which would override our value
                
                // Call the Score API to update the AcademicGradesScore after updating grades
                try
                {
                    _logger.LogInformation($"Calling Score API to update AcademicGradesScore for student {studentId}");
                    using (var httpClient = new HttpClient())
                    {
                        string baseUrl = $"{Request.Scheme}://{Request.Host}";
                        string apiUrl = $"{baseUrl}/Score/CalculateAcademicGradesScore?studentId={studentId}";
                        
                        var response = await httpClient.PostAsync(apiUrl, null);
                        if (response.IsSuccessStatusCode)
                        {
                            var result = await response.Content.ReadAsStringAsync();
                            _logger.LogInformation($"Successfully updated AcademicGradesScore: {result}");
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to update AcademicGradesScore: {response.StatusCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't stop execution if this fails
                    _logger.LogError(ex, $"Error updating AcademicGradesScore for student {studentId}: {ex.Message}");
                }
                
                // Update the student's grade level based on their grades
                await UpdateStudentGradeLevel(connection, studentId);
                
                // Get the updated grade level
                int gradeLevel = await GetStudentGradeLevel(connection, studentId);
                
                // Check if the 4th-year grade was just assigned for the first time
                bool fourthYearGradeAssigned = false;
                if (fourthYearGrade.HasValue)
                {
                    try
                    {
                        // Check if 4th year grade was previously set in StudentDetails
                        string previousGradeSql = "SELECT FourthYearGrade FROM StudentDetails WHERE IdNumber = @StudentId";
                        
                        using (var command = new SqlCommand(previousGradeSql, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            var previousGrade = await command.ExecuteScalarAsync();
                            
                            // The 4th-year grade was just assigned if it was previously null
                            fourthYearGradeAssigned = (previousGrade == null || previousGrade == DBNull.Value);
                            _logger.LogInformation($"4th year grade check for student {studentId}: Previously had grade: {previousGrade != null && previousGrade != DBNull.Value}, Is new assignment: {fourthYearGradeAssigned}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue execution
                        _logger.LogError(ex, $"Error checking previous 4th-year grade for student {studentId}: {ex.Message}");
                        
                        // Default to assuming it's a new assignment to generate certificate
                        fourthYearGradeAssigned = true;
                    }
                    
                    // If 4th-year grade was just assigned, trigger certificate generation
                    if (fourthYearGradeAssigned)
                    {
                        try
                        {
                            _logger.LogInformation($"4th-year grade assigned for student {studentId}, triggering EduBadge certificate generation");
                            
                            // Call the ProgrammingTestController to generate the EduBadge certificate
                            using (var httpClient = new HttpClient())
                            {
                                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                                string apiUrl = $"{baseUrl}/ProgrammingTest/GetEduBadgeCertificate?studentId={studentId}";
                                
                                var response = await httpClient.GetAsync(apiUrl);
                                if (response.IsSuccessStatusCode)
                                {
                                    var result = await response.Content.ReadAsStringAsync();
                                    _logger.LogInformation($"Successfully generated EduBadge certificate for student {studentId}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Failed to generate EduBadge certificate: {response.StatusCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log but don't stop execution if this fails
                            _logger.LogError(ex, $"Error generating EduBadge certificate for student {studentId}: {ex.Message}");
                        }
                    }
                }
                
                // Return the new badge information
                return Json(new { 
                    success = true, 
                    message = "Grades saved successfully!",
                    newScore = newScore,
                    badgeColor = badgeColor,
                    badgeName = badgeName,
                    gradeLevel = gradeLevel,
                    achievements = achievements,
                    comments = comments,
                    fourthYearGradeAssigned = fourthYearGradeAssigned
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating student grades for {studentId}: {ex.Message}");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }
    // Helper method to check if a column exists in a table
    private async Task<bool> CheckColumnExists(SqlConnection conn, string tableName, string columnName)
    {
        var checkColumnCmd = new SqlCommand(
            $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' AND COLUMN_NAME = '{columnName}'",
            conn
        );
        return (int)await checkColumnCmd.ExecuteScalarAsync() > 0;
    }
    
    // PIN Generation and Management Methods
    [HttpPost]
    public async Task<IActionResult> GeneratePINs(int count, int expiryDays)
    {
        if (!IsAdmin())
        {
            return Json(new { success = false, message = "Unauthorized access" });
        }

        if (count < 1 || count > 100)
        {
            return Json(new { success = false, message = "Count must be between 1 and 100" });
        }

        if (expiryDays < 1 || expiryDays > 365)
        {
            return Json(new { success = false, message = "Expiry days must be between 1 and 365" });
        }

        try
        {
            var generatedPins = new List<object>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Check if the VerificationPINs table exists
                if (!await TableExists(connection, "VerificationPINs"))
                {
                    // Execute the SQL script to create the table
                    string sqlScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "CreateVerificationPINs.sql");
                    if (System.IO.File.Exists(sqlScriptPath))
                    {
                        string sqlScript = await System.IO.File.ReadAllTextAsync(sqlScriptPath);
                        using (SqlCommand command = new SqlCommand(sqlScript, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created VerificationPINs table");
                        }
                    }
                    else
                    {
                        _logger.LogError("SQL script for creating VerificationPINs table not found");
                        return Json(new { success = false, message = "Database setup error. Please contact administrator." });
                    }
                }

                // Generate and insert the PINs
                Random random = new Random();
                DateTime now = DateTime.Now;
                DateTime expiryDate = now.AddDays(expiryDays);

                for (int i = 0; i < count; i++)
                {
                    // Generate a 6-digit PIN
                    string pin = random.Next(100000, 999999).ToString();

                    // Insert the PIN into the database
                    string insertQuery = @"
                        INSERT INTO VerificationPINs (PIN, CreatedAt, ExpiryDate, IsUsed, UsedById, UsedAt)
                        VALUES (@PIN, @CreatedAt, @ExpiryDate, 0, NULL, NULL);
                        SELECT SCOPE_IDENTITY();";

                    using (SqlCommand command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@PIN", pin);
                        command.Parameters.AddWithValue("@CreatedAt", now);
                        command.Parameters.AddWithValue("@ExpiryDate", expiryDate);

                        // Get the inserted PIN ID
                        int pinId = Convert.ToInt32(await command.ExecuteScalarAsync());

                        // Add to the result list
                        generatedPins.Add(new
                        {
                            pinId = pinId,
                            pin = pin,
                            createdAt = now.ToString("yyyy-MM-dd HH:mm"),
                            expiryDate = expiryDate.ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                }
            }

            return Json(new { success = true, pins = generatedPins });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PINs");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetExistingPINs()
    {
        if (!IsAdmin())
        {
            return Json(new { success = false, message = "Unauthorized access" });
        }

        try
        {
            var pins = new List<object>();
            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Check if the VerificationPINs table exists
                if (!await TableExists(connection, "VerificationPINs"))
                {
                    return Json(new { success = true, pins = pins });
                }

                string query = @"
                    SELECT vp.PINId, vp.PIN, vp.CreatedAt, vp.ExpiryDate, vp.IsUsed, vp.UsedById, vp.UsedAt,
                           CASE WHEN u.Username IS NOT NULL THEN u.Username ELSE NULL END AS UsedByUsername
                    FROM VerificationPINs vp
                    LEFT JOIN Users u ON CAST(vp.UsedById AS NVARCHAR(50)) = u.UserId
                    ORDER BY vp.CreatedAt DESC";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            pins.Add(new
                            {
                                pinId = reader.GetInt32(0),
                                pin = reader.GetString(1),
                                createdAt = reader.GetDateTime(2).ToString("yyyy-MM-dd HH:mm"),
                                expiryDate = reader.GetDateTime(3).ToString("yyyy-MM-dd HH:mm"),
                                isUsed = reader.GetBoolean(4),
                                usedById = !reader.IsDBNull(5) ? reader.GetValue(5).ToString() : null,
                                usedAt = !reader.IsDBNull(6) ? reader.GetDateTime(6).ToString("yyyy-MM-dd HH:mm") : null,
                                usedByUsername = !reader.IsDBNull(7) ? reader.GetString(7) : null
                            });
                        }
                    }
                }
            }

            return Json(new { success = true, pins = pins });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving existing PINs");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    // Helper method to check if a user is admin (reuse existing logic or add this if needed)
    private bool IsAdmin()
    {
        // Check if the user is authenticated and has admin role
        if (HttpContext.Session.GetString("Role") == "admin")
        {
            return true;
        }
        return false;
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStudent(string IdNumber, string FullName, string Username, string Password, string Course, string Section)
    {
        _logger.LogInformation($"UpdateStudent called with ID: {IdNumber}, Name: {FullName}, Username: {Username}, Course: {Course}, Section: {Section}");
        
        if (string.IsNullOrEmpty(IdNumber))
        {
            return Json(new { success = false, message = "Student ID is required" });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if we're using new table structure
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    connection
                );
                bool usingNewTables = (int)await checkTableCmd.ExecuteScalarAsync() > 0;
                
                if (usingNewTables)
                {
                    // First check if student exists
                    string checkQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @IdNumber";
                    using (var checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@IdNumber", IdNumber);
                        int count = (int)await checkCmd.ExecuteScalarAsync();
                        
                        if (count == 0)
                        {
                            return Json(new { success = false, message = "Student not found" });
                        }
                    }
                    
                    // Get the UserId for the student
                    string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                    string userId = null;
                    using (var userIdCmd = new SqlCommand(getUserIdQuery, connection))
                    {
                        userIdCmd.Parameters.AddWithValue("@IdNumber", IdNumber);
                        userId = (string)await userIdCmd.ExecuteScalarAsync();
                    }
                    
                    // Update Users table
                    string updateUserQuery = @"
                        UPDATE Users 
                        SET FullName = @FullName, Username = @Username";
                        
                    // Only update password if provided
                    if (!string.IsNullOrEmpty(Password))
                    {
                        updateUserQuery += ", Password = @Password";
                    }
                    
                    updateUserQuery += " WHERE UserId = @UserId";
                    
                    using (var updateUserCmd = new SqlCommand(updateUserQuery, connection))
                    {
                        updateUserCmd.Parameters.AddWithValue("@FullName", FullName);
                        updateUserCmd.Parameters.AddWithValue("@Username", Username);
                        updateUserCmd.Parameters.AddWithValue("@UserId", userId);
                        
                        if (!string.IsNullOrEmpty(Password))
                        {
                            updateUserCmd.Parameters.AddWithValue("@Password", Password);
                        }
                        
                        await updateUserCmd.ExecuteNonQueryAsync();
                    }
                    
                    // Update StudentDetails table
                    string updateDetailsQuery = @"
                        UPDATE StudentDetails 
                        SET Course = @Course, Section = @Section
                        WHERE IdNumber = @IdNumber";
                        
                    using (var updateDetailsCmd = new SqlCommand(updateDetailsQuery, connection))
                    {
                        updateDetailsCmd.Parameters.AddWithValue("@Course", Course);
                        updateDetailsCmd.Parameters.AddWithValue("@Section", Section);
                        updateDetailsCmd.Parameters.AddWithValue("@IdNumber", IdNumber);
                        
                        await updateDetailsCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Using old table structure (Students table)
                    string updateQuery = @"
                        UPDATE Students 
                        SET FullName = @FullName, 
                            Username = @Username, 
                            Course = @Course, 
                            Section = @Section";
                    
                    // Only update password if it's provided
                    if (!string.IsNullOrEmpty(Password))
                    {
                        updateQuery += ", Password = @Password";
                    }
                    
                    updateQuery += " WHERE IdNumber = @IdNumber";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", FullName);
                        command.Parameters.AddWithValue("@Username", Username);
                        command.Parameters.AddWithValue("@Course", Course);
                        command.Parameters.AddWithValue("@Section", Section);
                        command.Parameters.AddWithValue("@IdNumber", IdNumber);
                        
                        if (!string.IsNullOrEmpty(Password))
                        {
                            command.Parameters.AddWithValue("@Password", Password);
                        }
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            return Json(new { success = false, message = "Student not found" });
                        }
                    }
                }
                
                return Json(new { success = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating student {IdNumber}: {ex.Message}");
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 10485760)] // 10 MB
    [RequestSizeLimit(10485760)] // 10 MB
    public async Task<IActionResult> UploadPinSpreadsheet([FromForm] PinUploadModel model)
    {
        _logger.LogInformation("PIN Upload: Starting process with model: {ModelNull}", model == null ? "null" : "not null");
        
        if (!IsAdmin())
        {
            return Json(new { success = false, message = "Unauthorized access" });
        }

        // Log UserType and ExpiryDays
        _logger.LogInformation("PIN Upload: UserType: {UserType}, ExpiryDays: {ExpiryDays}", 
            model.UserType, model.ExpiryDays);
            
        // Validate UserType
        if (string.IsNullOrEmpty(model.UserType))
        {
            _logger.LogWarning("PIN Upload: UserType is null or empty");
            return Json(new { success = false, message = "Please select a user type (Student or Teacher)." });
        }

        if (model.SpreadsheetFile == null || model.SpreadsheetFile.Length <= 0)
        {
            _logger.LogWarning("PIN Upload: No file uploaded or file is empty. SpreadsheetFile null: {IsNull}", model.SpreadsheetFile == null);
            return Json(new { success = false, message = "Please select a file to upload." });
        }

        _logger.LogInformation("PIN Upload: File received - Name: {FileName}, Length: {FileLength}", 
            model.SpreadsheetFile.FileName, model.SpreadsheetFile.Length);

        if (!Path.GetExtension(model.SpreadsheetFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PIN Upload: Invalid file format: {Extension}", 
                Path.GetExtension(model.SpreadsheetFile.FileName));
            return Json(new { success = false, message = "Please select an Excel file (.xlsx)." });
        }

        var results = new List<PinGenerationResult>();
        var processedCount = 0;
        var successCount = 0;
        var errorCount = 0;

        try
        {
            using (var stream = new MemoryStream())
            {
                await model.SpreadsheetFile.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    
                    // Validate worksheet has content
                    if (worksheet.Dimension == null)
                    {
                        _logger.LogWarning("PIN Upload: Empty spreadsheet");
                        return Json(new { success = false, message = "The spreadsheet is empty. Please use the template and add user data." });
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var colCount = worksheet.Dimension.Columns;
                    
                    // Require at least 2 rows (header + 1 data row)
                    if (rowCount < 2)
                    {
                        _logger.LogWarning("PIN Upload: No data rows in spreadsheet");
                        return Json(new { success = false, message = "No user data found in the spreadsheet. Please add at least one user." });
                    }
                    
                    // Check for required columns
                    var nameColIndex = -1;
                    var emailColIndex = -1;
                    
                    for (int col = 1; col <= colCount; col++)
                    {
                        var cellValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(cellValue)) continue;
                        
                        // Be more flexible with column name detection
                        if (cellValue.Contains("Name", StringComparison.OrdinalIgnoreCase) || 
                            cellValue.Contains("Full", StringComparison.OrdinalIgnoreCase) ||
                            cellValue.Contains("Student", StringComparison.OrdinalIgnoreCase) ||
                            cellValue.Contains("Teacher", StringComparison.OrdinalIgnoreCase))
                        {
                            nameColIndex = col;
                        }
                        else if (cellValue.Contains("Email", StringComparison.OrdinalIgnoreCase) || 
                                 cellValue.Contains("Mail", StringComparison.OrdinalIgnoreCase) ||
                                 cellValue.Contains("E-mail", StringComparison.OrdinalIgnoreCase))
                        {
                            emailColIndex = col;
                        }
                    }
                    
                    if (nameColIndex == -1 || emailColIndex == -1)
                    {
                        _logger.LogWarning("PIN Upload: Missing required columns. Name column found: {NameFound}, Email column found: {EmailFound}", 
                            nameColIndex != -1, emailColIndex != -1);
                            
                        return Json(new { 
                            success = false, 
                            message = "Spreadsheet must contain columns for Name and Email. The system couldn't identify these columns in your spreadsheet."
                        });
                    }
                    
                    // Process each row (skip header)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var name = worksheet.Cells[row, nameColIndex].Value?.ToString()?.Trim();
                        var email = worksheet.Cells[row, emailColIndex].Value?.ToString()?.Trim();
                        
                        var result = new PinGenerationResult 
                        { 
                            Name = name, 
                            Email = email,
                            EmailSent = false
                        };
                        
                        // Skip rows with empty name or email
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
                        {
                            result.ErrorMessage = "Missing name or email";
                            results.Add(result);
                            errorCount++;
                            continue;
                        }
                        
                        // Validate email format
                        if (!IsValidEmail(email))
                        {
                            result.ErrorMessage = "Invalid email format";
                            results.Add(result);
                            errorCount++;
                            continue;
                        }
                        
                        processedCount++;
                        
                        try
                        {
                            // Generate PIN
                            var pin = await GenerateAndStorePin(model.ExpiryDays);
                            result.Pin = pin;
                            
                            // Send email with PIN
                            _logger.LogInformation("Attempting to send email to {Email} with PIN {Pin}", email, pin);
                            var emailSent = await _emailService.SendPinEmailAsync(email, name, pin);
                            result.EmailSent = emailSent;
                            
                            if (emailSent)
                            {
                                _logger.LogInformation("Email sent successfully to {Email}", email);
                                successCount++;
                            }
                            else
                            {
                                _logger.LogError("Failed to send email to {Email}", email);
                                result.ErrorMessage = "Failed to send email. Check application logs for details.";
                                errorCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "PIN Upload: Error processing user {Name}, {Email}: {ErrorMessage}", name, email, ex.Message);
                            result.ErrorMessage = $"Error: {ex.Message}";
                            errorCount++;
                        }
                        
                        results.Add(result);
                    }
                }
            }
            
            _logger.LogInformation("PIN Upload: Completed. Processed: {Processed}, Success: {Success}, Errors: {Errors}",
                processedCount, successCount, errorCount);
                
            return Json(new
            {
                success = true,
                processed = processedCount,
                successCount = successCount,
                errorCount = errorCount,
                results = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PIN Upload: Error processing spreadsheet");
            return Json(new { success = false, message = $"Error processing spreadsheet: {ex.Message}" });
        }
    }
    
    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GenerateAndStorePin(int expiryDays)
    {
        string connectionString = _configuration.GetConnectionString("DefaultConnection");
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            // Check if the VerificationPINs table exists
            if (!await TableExists(connection, "VerificationPINs"))
            {
                // Execute the SQL script to create the table
                string sqlScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "CreateVerificationPINs.sql");
                if (System.IO.File.Exists(sqlScriptPath))
                {
                    string sqlScript = await System.IO.File.ReadAllTextAsync(sqlScriptPath);
                    using (SqlCommand command = new SqlCommand(sqlScript, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    throw new FileNotFoundException("SQL script for VerificationPINs table not found");
                }
            }

            // Generate a 6-digit PIN
            Random random = new Random();
            string pin = random.Next(100000, 999999).ToString();
            DateTime now = DateTime.Now;
            DateTime expiryDate = now.AddDays(expiryDays);

            // Insert the PIN into the database
            string insertQuery = @"
                INSERT INTO VerificationPINs (PIN, CreatedAt, ExpiryDate, IsUsed, UsedById, UsedAt)
                VALUES (@PIN, @CreatedAt, @ExpiryDate, 0, NULL, NULL);";

            using (SqlCommand command = new SqlCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@PIN", pin);
                command.Parameters.AddWithValue("@CreatedAt", now);
                command.Parameters.AddWithValue("@ExpiryDate", expiryDate);
                await command.ExecuteNonQueryAsync();
            }

            return pin;
        }
    }
    
    public IActionResult DownloadPinSpreadsheetTemplate()
    {
        var stream = new MemoryStream();
        using (var package = new ExcelPackage(stream))
        {
            var worksheet = package.Workbook.Worksheets.Add("Users");
            
            // Add header row
            worksheet.Cells[1, 1].Value = "Name";
            worksheet.Cells[1, 2].Value = "Email";
            
            // Format header row
            using (var range = worksheet.Cells[1, 1, 1, 2])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
            
            // Add sample data for students
            worksheet.Cells[2, 1].Value = "John Doe";
            worksheet.Cells[2, 2].Value = "john.doe@example.com";
            
            worksheet.Cells[3, 1].Value = "Jane Smith";
            worksheet.Cells[3, 2].Value = "jane.smith@example.com";
            
            // Add a note about format flexibility
            worksheet.Cells[5, 1].Value = "Note:";
            worksheet.Cells[5, 2].Value = "This template works for both students and teachers. Just fill in names and emails.";
            worksheet.Cells[5, 1, 5, 2].Style.Font.Italic = true;
            
            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            
            package.Save();
        }
        
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PinImportTemplate.xlsx");
    }

    // Method to update student grade levels automatically based on their grades
    [HttpPost]
    public async Task<IActionResult> UpdateGradeLevels()
    {
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if StudentDetails table exists
                bool usingNewTables = await TableExists(connection, "StudentDetails");
                
                if (!usingNewTables)
                {
                    return Json(new { success = false, message = "Student details table not found." });
                }
                
                // Check if grade columns exist
                bool hasGradeColumns = await ColumnExists(connection, "StudentDetails", "FirstYearGrade");
                
                if (!hasGradeColumns)
                {
                    return Json(new { success = false, message = "Grade columns not found." });
                }
                
                // Check if GradeLevel column exists
                bool hasGradeLevelColumn = await ColumnExists(connection, "StudentDetails", "GradeLevel");
                
                if (!hasGradeLevelColumn)
                {
                    return Json(new { success = false, message = "GradeLevel column not found. Please run the AddGradeLevelColumn.sql script first." });
                }
                
                // Get all students with their grades
                string getSql = @"
                    SELECT IdNumber, FirstYearGrade, SecondYearGrade, ThirdYearGrade, FourthYearGrade
                    FROM StudentDetails";
                    
                var students = new List<Student>();
                
                using (var command = new SqlCommand(getSql, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var student = new Student
                            {
                                IdNumber = reader["IdNumber"]?.ToString() ?? "",
                                FirstYearGrade = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : null,
                                SecondYearGrade = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : null,
                                ThirdYearGrade = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : null,
                                FourthYearGrade = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : null
                            };
                            
                            students.Add(student);
                        }
                    }
                }
                
                // Update grade level for each student
                int updateCount = 0;
                
                foreach (var student in students)
                {
                    // Calculate grade level
                    int gradeLevel = student.CalculateGradeLevel();
                    
                    // Update database
                    string updateSql = @"
                        UPDATE StudentDetails
                        SET GradeLevel = @GradeLevel
                        WHERE IdNumber = @StudentId";
                        
                    using (var command = new SqlCommand(updateSql, connection))
                    {
                        command.Parameters.AddWithValue("@GradeLevel", gradeLevel);
                        command.Parameters.AddWithValue("@StudentId", student.IdNumber);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        if (rowsAffected > 0)
                        {
                            updateCount++;
                        }
                    }
                }
                
                return Json(new { success = true, message = $"Successfully updated grade levels for {updateCount} students." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating grade levels: {ex.Message}");
            return Json(new { success = false, message = $"Error updating grade levels: {ex.Message}" });
        }
    }

    // Add a method to automatically update grade level when grades are updated
    [NonAction]
    private async Task UpdateStudentGradeLevel(SqlConnection connection, string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return;
        }
        
        // Check if GradeLevel column exists
        bool hasGradeLevelColumn = await ColumnExists(connection, "StudentDetails", "GradeLevel");
        
        if (!hasGradeLevelColumn)
        {
            return; // Column doesn't exist yet
        }
        
        // Get student grades
        string getSql = @"
            SELECT FirstYearGrade, SecondYearGrade, ThirdYearGrade, FourthYearGrade
            FROM StudentDetails
            WHERE IdNumber = @StudentId";
            
        var student = new Student();
        
        using (var command = new SqlCommand(getSql, connection))
        {
            command.Parameters.AddWithValue("@StudentId", studentId);
            
            using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    student.FirstYearGrade = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : null;
                    student.SecondYearGrade = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : null;
                    student.ThirdYearGrade = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : null;
                    student.FourthYearGrade = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : null;
                }
                else
                {
                    return; // Student not found
                }
            }
        }
        
        // Calculate grade level
        int gradeLevel = student.CalculateGradeLevel();
        
        // Update database
        string updateSql = @"
            UPDATE StudentDetails
            SET GradeLevel = @GradeLevel
            WHERE IdNumber = @StudentId";
            
        using (var command = new SqlCommand(updateSql, connection))
        {
            command.Parameters.AddWithValue("@GradeLevel", gradeLevel);
            command.Parameters.AddWithValue("@StudentId", studentId);
            
            await command.ExecuteNonQueryAsync();
        }
    }
    
    // Add a method to get a student's current grade level
    [NonAction]
    private async Task<int> GetStudentGradeLevel(SqlConnection connection, string studentId)
    {
        // Check if GradeLevel column exists
        bool hasGradeLevelColumn = await ColumnExists(connection, "StudentDetails", "GradeLevel");
        
        if (!hasGradeLevelColumn)
        {
            return 1; // Default to 1st year if column doesn't exist
        }
        
        // Get the student's grade level
        string getSql = @"
            SELECT GradeLevel 
            FROM StudentDetails
            WHERE IdNumber = @StudentId";
            
        using (var command = new SqlCommand(getSql, connection))
        {
            command.Parameters.AddWithValue("@StudentId", studentId);
            
            var result = await command.ExecuteScalarAsync();
            
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToInt32(result);
            }
            else
            {
                return 1; // Default to 1st year if no grade level set
            }
        }
    }

    // Helper method to apply year-level caps to student scores
    [NonAction]
    private int ApplyYearLevelCap(int score, int gradeLevel)
    {
        return gradeLevel switch
        {
            1 => Math.Min(score, 60),  // 1st years max 60
            2 => Math.Min(score, 75),  // 2nd years max 75
            3 => Math.Min(score, 90),  // 3rd years max 90
            _ => score                 // 4th years and graduates no cap
        };
    }

    // Score-related functionality has been moved to ScoreController

    // Method to ensure ExtraCurricularActivities table exists
    [HttpPost]
    public async Task<IActionResult> CreateExtraCurricularActivitiesTable()
    {
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the table already exists
                bool tableExists = false;
                string checkTableQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'ExtraCurricularActivities'";
                
                using (var command = new SqlCommand(checkTableQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    tableExists = (count > 0);
                }
                
                if (tableExists)
                {
                    return Json(new { success = true, message = "Table already exists" });
                }
                
                // Create the table if it doesn't exist
                string createTableQuery = @"
                CREATE TABLE ExtraCurricularActivities (
                    ActivityId INT IDENTITY(1,1) PRIMARY KEY,
                    StudentId NVARCHAR(128) NOT NULL,
                    TeacherId NVARCHAR(128) NOT NULL,
                    ActivityName NVARCHAR(255) NOT NULL,
                    ActivityDescription NVARCHAR(MAX),
                    ActivityCategory NVARCHAR(100) NOT NULL,
                    ActivityDate DATETIME NOT NULL,
                    RecordedDate DATETIME NOT NULL DEFAULT GETDATE(),
                    Score DECIMAL(5,2) NOT NULL DEFAULT 0,
                    ProofImageData VARBINARY(MAX),
                    ProofImageContentType NVARCHAR(100),
                    CONSTRAINT FK_ExtraCurricularActivities_StudentId FOREIGN KEY (StudentId)
                    REFERENCES Users(UserId) ON DELETE CASCADE,
                    CONSTRAINT FK_ExtraCurricularActivities_TeacherId FOREIGN KEY (TeacherId)
                    REFERENCES Users(UserId)
                );

                CREATE INDEX IX_ExtraCurricularActivities_StudentId ON ExtraCurricularActivities(StudentId);
                CREATE INDEX IX_ExtraCurricularActivities_TeacherId ON ExtraCurricularActivities(TeacherId);";
                
                using (var command = new SqlCommand(createTableQuery, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
                
                return Json(new { success = true, message = "Table created successfully" });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ViewAttendanceProof(int id)
    {
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Get proof image data for the attendance record
                string query = @"
                    SELECT ProofImageData, ProofImageContentType
                    FROM AttendanceRecords
                    WHERE AttendanceId = @AttendanceId";
                    
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@AttendanceId", id);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync() && !reader.IsDBNull(0))
                        {
                            byte[] imageData = (byte[])reader["ProofImageData"];
                            string contentType = reader["ProofImageContentType"]?.ToString() ?? "image/jpeg";
                            
                            // Return the image
                            return File(imageData, contentType);
                        }
                    }
                }
            }
            
            // Return a placeholder image if no image was found
            return NotFound("Image not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving proof image for attendance ID {AttendanceId}", id);
            return NotFound("Error retrieving image");
        }
    }
    
    [HttpGet]
    public async Task<IActionResult> ViewExtraCurricularProofImage(int activityId)
    {
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Get proof image data for the extracurricular activity
                string query = @"
                    SELECT ProofImageData, ProofImageContentType
                    FROM ExtraCurricularActivities
                    WHERE ActivityId = @ActivityId";
                    
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ActivityId", activityId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync() && !reader.IsDBNull(0))
                        {
                            byte[] imageData = (byte[])reader["ProofImageData"];
                            string contentType = reader["ProofImageContentType"]?.ToString() ?? "image/jpeg";
                            
                            // Return the image
                            return File(imageData, contentType);
                        }
                    }
                }
            }
            
            // Return a placeholder image if no image was found
            return NotFound("Image not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving proof image for extracurricular activity ID {ActivityId}", activityId);
            return NotFound("Error retrieving image");
        }
    }

    // Helper method to calculate weighted overall score from category scores
    [NonAction]
    private int CalculateWeightedScore(decimal academicScore, decimal challengesScore, decimal masteryScore, 
                                     decimal seminarsScore, decimal extracurricularScore)
    {
        // Apply the weight percentages to each category
        decimal weightedTotal = (academicScore * 0.30m) +           // Academic - 30%
                               (challengesScore * 0.20m) +          // Coding Challenges/Projects - 20%
                               (masteryScore * 0.20m) +             // Skill Mastery - 20% 
                               (seminarsScore * 0.10m) +            // Webinars/Seminars - 10%
                               (extracurricularScore * 0.20m);      // Extra-Curricular Involvement - 20%
        
        // Round to the nearest integer and ensure it's within 0-100 range
        return Math.Min(100, Math.Max(0, (int)Math.Round(weightedTotal)));
    }
    
    // Score-related functionality has been moved to ScoreController

    // Method to recalculate and update a student's overall score
    // Moved to ScoreController.RecalculateScore
    [HttpPost]
    public async Task<IActionResult> UpdateStudentScoreFromCategories(string studentId)
    {
        // Redirect to ScoreController
        _logger.LogInformation($"Redirecting UpdateStudentScoreFromCategories to ScoreController for studentId={studentId}");
        return RedirectToAction("RecalculateScore", "Score", new { studentId });
    }

    // Helper method to apply year-level bonuses to student scores
    [NonAction]
    private int ApplyYearLevelBonus(int baseScore, int gradeLevel)
    {
        // Apply a percentage multiplier based on year level
        // This ensures 1st years can never outscore higher years
        double multiplier = gradeLevel switch
        {
            1 => 0.25, // 1st years: 25% of actual score
            2 => 0.50, // 2nd years: 50% of actual score
            3 => 0.75, // 3rd years: 75% of actual score
            _ => 1.00  // 4th years and graduates: 100% of actual score
        };
        
        // Apply multiplier to score
        return (int)(baseScore * multiplier);
    }

    /// <summary>
    /// Gets a student's score directly from the database
    /// </summary>
    /// <param name="studentId">The student ID</param>
    /// <returns>JSON result with the student's score</returns>
    [HttpGet]
    public async Task<IActionResult> GetStudentScore(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Student ID is required" });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT Score, BadgeColor, AcademicGradesScore, CompletedChallengesScore, 
                           MasteryScore, SeminarsWebinarsScore, ExtracurricularScore,
                           GradeLevel
                    FROM StudentDetails 
                    WHERE IdNumber = @StudentId";
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Simple extraction of values
                            decimal score = reader["Score"] != DBNull.Value ? Convert.ToDecimal(reader["Score"]) : 0;
                            string badgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "warning";
                            decimal academicScore = reader["AcademicGradesScore"] != DBNull.Value ? Convert.ToDecimal(reader["AcademicGradesScore"]) : 0;
                            decimal challengesScore = reader["CompletedChallengesScore"] != DBNull.Value ? Convert.ToDecimal(reader["CompletedChallengesScore"]) : 0;
                            decimal masteryScore = reader["MasteryScore"] != DBNull.Value ? Convert.ToDecimal(reader["MasteryScore"]) : 0;
                            decimal seminarsScore = reader["SeminarsWebinarsScore"] != DBNull.Value ? Convert.ToDecimal(reader["SeminarsWebinarsScore"]) : 0;
                            decimal extracurricularScore = reader["ExtracurricularScore"] != DBNull.Value ? Convert.ToDecimal(reader["ExtracurricularScore"]) : 0;
                            int gradeLevel = reader["GradeLevel"] != DBNull.Value ? Convert.ToInt32(reader["GradeLevel"]) : 1;
                            
                            // Return the data
                            return Json(new {
                                success = true,
                                score,
                                badgeColor,
                                academicScore,
                                challengesScore,
                                masteryScore,
                                seminarsScore,
                                extracurricularScore,
                                gradeLevel
                            });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student not found" });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching student score: {ex.Message}");
            return Json(new { success = false, message = $"Error fetching student score: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateVideoCallAvailability([FromBody] VideoCallAvailabilityModel model)
    {
        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return Json(new { success = false, message = "User not authenticated." });
        }

        try
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if the IsVideoCallAvailable column exists
                bool hasColumn = false;
                string checkColumnQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'IsVideoCallAvailable'";
                
                using (var checkCommand = new SqlCommand(checkColumnQuery, conn))
                {
                    int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                    hasColumn = (count > 0);
                }
                
                // Add the column if it doesn't exist
                if (!hasColumn)
                {
                    try
                    {
                        string alterTableQuery = @"
                            ALTER TABLE StudentDetails 
                            ADD IsVideoCallAvailable BIT DEFAULT 0";
                                
                        using (var command = new SqlCommand(alterTableQuery, conn))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error adding IsVideoCallAvailable column to StudentDetails table");
                        return Json(new { success = false, message = "Failed to update database schema." });
                    }
                }
                
                // Update the video call availability
                string updateQuery = @"
                    UPDATE StudentDetails
                    SET IsVideoCallAvailable = @IsAvailable
                    WHERE IdNumber = @IdNumber";
                    
                using (var command = new SqlCommand(updateQuery, conn))
                {
                    command.Parameters.AddWithValue("@IsAvailable", model.IsAvailable);
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update video call availability." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating video call availability");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    // Add teacher certificate verification methods
    [HttpGet]
    [Route("PendingCertificates")]
    public async Task<IActionResult> PendingCertificates()
    {
        // Ensure user is a teacher
        if (HttpContext.Session.GetString("Role") != "teacher")
        {
            return RedirectToAction("Login", "Home");
        }

        try
        {
            var pendingCertificates = new List<StudentBadge.Models.StudentCertificateViewModel>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if table exists
                string checkTableSql = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'StudentCertificates'";
                    
                using (var command = new SqlCommand(checkTableSql, connection))
                {
                    int tableCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                    if (tableCount == 0)
                    {
                        // Table doesn't exist yet, so there are no pending certificates
                        return View(pendingCertificates);
                    }
                }
                
                // Get pending certificates with student information
                string sql = @"
                    SELECT c.CertificateId, c.StudentId, c.CertificateType, c.Title, 
                           c.Description, c.IssueDate, c.UploadDate, c.FileName,
                           u.FullName as StudentName
                    FROM StudentCertificates c
                    JOIN StudentDetails s ON c.StudentId = s.IdNumber
                    JOIN Users u ON s.UserId = u.UserId
                    WHERE c.IsVerified = 0
                    ORDER BY c.UploadDate DESC";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            pendingCertificates.Add(new StudentBadge.Models.StudentCertificateViewModel
                            {
                                CertificateId = reader.GetInt32(0),
                                StudentId = reader.GetString(1),
                                CertificateType = reader.GetString(2),
                                Title = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                IssueDate = reader.GetDateTime(5),
                                UploadDate = reader.GetDateTime(6),
                                FileName = reader.GetString(7),
                                StudentName = reader.GetString(8)
                            });
                        }
                    }
                }
            }
            
            return View(pendingCertificates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading pending certificates");
            ViewBag.ErrorMessage = "Error loading pending certificates: " + ex.Message;
            return View(new List<StudentBadge.Models.StudentCertificateViewModel>());
        }
    }
    
    [HttpPost]
    [Route("Dashboard/VerifyCertificate")]
    public async Task<IActionResult> VerifyCertificate(int certificateId, bool isApproved, int? score = null, string category = null, string rank = null)
    {
        // Ensure user is a teacher
        if (HttpContext.Session.GetString("Role") != "teacher")
        {
            return Json(new { success = false, message = "Unauthorized" });
        }
        
        // Get teacherId from session, use "Admin" as fallback if not found
        string teacherId = HttpContext.Session.GetString("TeacherId");
        if (string.IsNullOrEmpty(teacherId))
        {
            teacherId = "Admin"; // Default value if TeacherId is not in session
            _logger.LogWarning("TeacherId not found in session, using default 'Admin'");
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Ensure the CertificatesCount column exists
                await EnsureCertificatesCountColumnExists(connection);
                
                // Ensure the Score column exists in StudentCertificates table
                await EnsureScoreColumnExistsInStudentCertificates(connection);
                
                // Ensure the tables exist before updating
                await EnsureAttendanceRecordsTableExists(connection);
                await EnsureExtraCurricularActivitiesTableExists(connection);
                
                if (isApproved)
                {
                    // First check if this is an extracurricular certificate and score is required
                    string certificateType = "";
                    string studentId = "";
                    string certificateTitle = "";
                    string certificateDescription = "";
                    DateTime issueDate = DateTime.Now;
                    byte[] certificateData = null;
                    string fileName = "";
                    
                    string checkSql = @"SELECT CertificateType, StudentId, Title, Description, 
                                       IssueDate, CertificateData, FileName 
                                       FROM StudentCertificates 
                                       WHERE CertificateId = @CertificateId";
                    using (var checkCommand = new SqlCommand(checkSql, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@CertificateId", certificateId);
                        using (var reader = await checkCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                certificateType = reader.GetString(0);
                                studentId = reader.GetString(1);
                                certificateTitle = reader.GetString(2);
                                certificateDescription = reader.IsDBNull(3) ? "" : reader.GetString(3);
                                issueDate = reader.GetDateTime(4);
                                
                                if (!reader.IsDBNull(5))
                                {
                                    long byteLength = reader.GetBytes(5, 0, null, 0, 0);
                                    certificateData = new byte[byteLength];
                                    reader.GetBytes(5, 0, certificateData, 0, (int)byteLength);
                                }
                                
                                fileName = reader.IsDBNull(6) ? "" : reader.GetString(6);
                            }
                            else
                            {
                                return Json(new { success = false, message = "Certificate not found" });
                            }
                        }
                    }
                    
                    // If this is an extracurricular certificate and no score provided, return error
                    if (certificateType == "extracurricular" && !score.HasValue)
                    {
                        return Json(new { success = false, message = "Score is required for extracurricular certificates" });
                    }
                    
                    // Approve the certificate
                    string sql = @"
                        UPDATE StudentCertificates 
                        SET IsVerified = 1, 
                            VerifiedBy = @TeacherId, 
                            VerificationDate = @VerificationDate";
                    
                    // Add score parameter for extracurricular certificates if score column exists
                    bool scoreColumnExists = await ColumnExists(connection, "StudentCertificates", "Score");
                    if (scoreColumnExists && certificateType == "extracurricular" && score.HasValue)
                    {
                        sql += ", Score = @Score";
                    }
                    
                    sql += " WHERE CertificateId = @CertificateId";
                    
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@CertificateId", certificateId);
                        command.Parameters.AddWithValue("@TeacherId", teacherId);
                        command.Parameters.AddWithValue("@VerificationDate", DateTime.Now);
                        
                        if (scoreColumnExists && certificateType == "extracurricular" && score.HasValue)
                        {
                            command.Parameters.AddWithValue("@Score", score.Value);
                        }
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            // Look up the student's UserId from the Users table
                            string userId = null;
                            try 
                            {
                                string getUserIdSql = @"
                                    SELECT u.UserId 
                                    FROM Users u 
                                    JOIN StudentDetails sd ON u.UserId = sd.UserId 
                                    WHERE sd.IdNumber = @StudentId";
                                    
                                using (var getUserCmd = new SqlCommand(getUserIdSql, connection))
                                {
                                    getUserCmd.Parameters.AddWithValue("@StudentId", studentId);
                                    var result = await getUserCmd.ExecuteScalarAsync();
                                    if (result != null && result != DBNull.Value)
                                    {
                                        userId = result.ToString();
                                        _logger.LogInformation($"Found UserId {userId} for student {studentId}");
                                    }
                                }
                                
                                if (string.IsNullOrEmpty(userId))
                                {
                                    // Check if studentId is directly a UserId
                                    string checkUserIdSql = "SELECT UserId FROM Users WHERE UserId = @UserId";
                                    using (var checkCmd = new SqlCommand(checkUserIdSql, connection))
                                    {
                                        checkCmd.Parameters.AddWithValue("@UserId", studentId);
                                        var result = await checkCmd.ExecuteScalarAsync();
                                        if (result != null && result != DBNull.Value)
                                        {
                                            userId = result.ToString();
                                            _logger.LogInformation($"Student ID {studentId} is a direct UserId");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error looking up UserId for student {StudentId}", studentId);
                            }
                            
                            // If we couldn't find a UserId, use the studentId directly
                            if (string.IsNullOrEmpty(userId))
                            {
                                userId = studentId;
                                _logger.LogWarning($"Using student ID {studentId} as UserId");
                            }
                            
                            // Add the certificate to the appropriate table based on type
                            if (certificateType.ToLower() == "attendance" || 
                                certificateType.ToLower() == "seminar" || 
                                certificateType.ToLower() == "workshop")
                            {
                                // Insert into AttendanceRecords
                                string insertAttendanceSql = @"
                                    INSERT INTO AttendanceRecords (
                                        StudentId, EventName, EventDescription, 
                                        EventDate, ProofImageData, ProofImageContentType, 
                                        IsVerified, RecordedDate, Score, TeacherId, CertificateId
                                    ) VALUES (
                                        @StudentId, @EventName, @EventDescription,
                                        @EventDate, @ProofImageData, @ProofImageContentType,
                                        @IsVerified, @RecordedDate, @Score, @TeacherId, @CertificateId
                                    )";
                                
                                using (var insertCmd = new SqlCommand(insertAttendanceSql, connection))
                                {
                                    insertCmd.Parameters.AddWithValue("@StudentId", userId); // Use UserId instead of StudentId
                                    insertCmd.Parameters.AddWithValue("@EventName", certificateTitle);
                                    insertCmd.Parameters.AddWithValue("@EventDescription", certificateDescription);
                                    insertCmd.Parameters.AddWithValue("@EventDate", issueDate);
                                    insertCmd.Parameters.AddWithValue("@ProofImageData", certificateData ?? (object)DBNull.Value);
                                    insertCmd.Parameters.AddWithValue("@ProofImageContentType", fileName);
                                    insertCmd.Parameters.AddWithValue("@IsVerified", true);
                                    insertCmd.Parameters.AddWithValue("@RecordedDate", DateTime.Now);
                                    insertCmd.Parameters.AddWithValue("@Score", 100);
                                    insertCmd.Parameters.AddWithValue("@TeacherId", teacherId);
                                    insertCmd.Parameters.AddWithValue("@CertificateId", certificateId);
                                    
                                    await insertCmd.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Added certificate {certificateId} to AttendanceRecords table");
                                }
                            }
                            else if (certificateType.ToLower() == "extracurricular")
                            {
                                // Insert into ExtraCurricularActivities
                                string insertExtraSql = @"
                                    INSERT INTO ExtraCurricularActivities (
                                        StudentId, ActivityName, ActivityDescription,
                                        ActivityDate, ProofImageData, ProofImageContentType,
                                        IsVerified, RecordedDate, Score, TeacherId, CertificateId,
                                        ActivityCategory, Rank
                                    ) VALUES (
                                        @StudentId, @ActivityName, @ActivityDescription,
                                        @ActivityDate, @ProofImageData, @ProofImageContentType,
                                        @IsVerified, @RecordedDate, @Score, @TeacherId, @CertificateId,
                                        @ActivityCategory, @Rank
                                    )";
                                
                                using (var insertCmd = new SqlCommand(insertExtraSql, connection))
                                {
                                    insertCmd.Parameters.AddWithValue("@StudentId", userId); // Use UserId instead of StudentId
                                    insertCmd.Parameters.AddWithValue("@ActivityName", certificateTitle);
                                    insertCmd.Parameters.AddWithValue("@ActivityDescription", certificateDescription);
                                    insertCmd.Parameters.AddWithValue("@ActivityDate", issueDate);
                                    insertCmd.Parameters.AddWithValue("@ProofImageData", certificateData ?? (object)DBNull.Value);
                                    insertCmd.Parameters.AddWithValue("@ProofImageContentType", fileName);
                                    insertCmd.Parameters.AddWithValue("@IsVerified", true);
                                    insertCmd.Parameters.AddWithValue("@RecordedDate", DateTime.Now);
                                    insertCmd.Parameters.AddWithValue("@Score", score ?? 0);
                                    insertCmd.Parameters.AddWithValue("@TeacherId", teacherId);
                                    insertCmd.Parameters.AddWithValue("@CertificateId", certificateId);
                                    insertCmd.Parameters.AddWithValue("@ActivityCategory", !string.IsNullOrEmpty(category) ? category : "Other");
                                    insertCmd.Parameters.AddWithValue("@Rank", !string.IsNullOrEmpty(rank) ? rank : DBNull.Value);
                                    
                                    await insertCmd.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Added certificate {certificateId} to ExtraCurricularActivities table");
                                }
                            }
                            
                            // Try to update student credentials directly instead of relying on service
                            try
                            {
                                // Update student's total certificate count
                                string updateCredentialsSql = @"
                                    UPDATE StudentDetails
                                    SET CertificatesCount = (
                                        SELECT COUNT(*) 
                                        FROM StudentCertificates 
                                        WHERE StudentId = @StudentId AND IsVerified = 1
                                    )
                                    WHERE IdNumber = @StudentId";
                                
                                using (var updateCommand = new SqlCommand(updateCredentialsSql, connection))
                                {
                                    updateCommand.Parameters.AddWithValue("@StudentId", studentId);
                                    await updateCommand.ExecuteNonQueryAsync();
                                }
                                
                                _logger.LogInformation($"Certificate {certificateId} approved and added to student {studentId}'s credentials");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error updating student credentials for certificate {CertificateId}", certificateId);
                                // Continue even if this fails, as the certificate is already approved
                            }
                            
                            // Call score calculator service in a fire-and-forget manner
                            try 
                            {
                                using (var httpClient = new HttpClient())
                                {
                                    string baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
                                    httpClient.BaseAddress = new Uri(baseUrl);
                                    
                                    var response = await httpClient.PostAsync(
                                        $"/Score/CalculateAllScoresForStudent?studentId={studentId}", 
                                        new StringContent(string.Empty));
                                        
                                    if (response.IsSuccessStatusCode)
                                    {
                                        _logger.LogInformation($"Successfully called score calculation service for student {studentId}");
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Score calculation service returned status code {response.StatusCode}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error calling score calculation service for student {StudentId}", studentId);
                                // Continue even if this fails
                            }
                            
                            return Json(new { success = true, message = "Certificate approved successfully" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Certificate not found" });
                        }
                    }
                }
                else
                {
                    // Get the student ID before rejecting
                    string studentId = "";
                    string getStudentSql = "SELECT StudentId FROM StudentCertificates WHERE CertificateId = @CertificateId";
                    using (var command = new SqlCommand(getStudentSql, connection))
                    {
                        command.Parameters.AddWithValue("@CertificateId", certificateId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            studentId = result.ToString();
                        }
                    }
                    
                    // Reject the certificate by deleting it
                    string sql = @"DELETE FROM StudentCertificates WHERE CertificateId = @CertificateId";
                    
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@CertificateId", certificateId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            // Update student's certificate count if we have the student ID
                            if (!string.IsNullOrEmpty(studentId))
                            {
                                try
                                {
                                    string updateCredentialsSql = @"
                                        UPDATE StudentDetails
                                        SET CertificatesCount = (
                                            SELECT COUNT(*) 
                                            FROM StudentCertificates 
                                            WHERE StudentId = @StudentId AND IsVerified = 1
                                        )
                                        WHERE IdNumber = @StudentId";
                                    
                                    using (var updateCommand = new SqlCommand(updateCredentialsSql, connection))
                                    {
                                        updateCommand.Parameters.AddWithValue("@StudentId", studentId);
                                        await updateCommand.ExecuteNonQueryAsync();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error updating student certificate count after rejection");
                                    // Continue even if this fails
                                }
                            }
                            
                            return Json(new { success = true, message = "Certificate rejected successfully" });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Certificate not found" });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying certificate");
            return Json(new { success = false, message = "Error processing certificate: " + ex.Message });
        }
    }
    
    // Helper method to ensure AttendanceRecords table exists
    private async Task EnsureAttendanceRecordsTableExists(SqlConnection connection)
    {
        bool tableExists = await TableExists(connection, "AttendanceRecords");
        if (!tableExists)
        {
            string createTableSql = @"
                CREATE TABLE [dbo].[AttendanceRecords](
                    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [StudentId] [nvarchar](50) NOT NULL,
                    [EventName] [nvarchar](255) NOT NULL,
                    [EventDescription] [nvarchar](max) NULL,
                    [EventDate] [datetime] NOT NULL,
                    [ProofImageData] [varbinary](max) NULL,
                    [ProofImageContentType] [nvarchar](255) NULL,
                    [IsVerified] [bit] NOT NULL DEFAULT(0),
                    [RecordedDate] [datetime] NULL,
                    [Score] [int] NULL DEFAULT(0),
                    [TeacherId] [nvarchar](50) NOT NULL,
                    [CertificateId] [int] NULL
                )";
            
            using (var command = new SqlCommand(createTableSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("AttendanceRecords table created");
            }
        }
        
        // Ensure CertificateId column exists
        bool certificateIdExists = await ColumnExists(connection, "AttendanceRecords", "CertificateId");
        if (!certificateIdExists)
        {
            string addColumnSql = @"
                ALTER TABLE AttendanceRecords
                ADD CertificateId int NULL";
            
            using (var command = new SqlCommand(addColumnSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("CertificateId column added to AttendanceRecords table");
            }
        }
        
        // Ensure Score column exists
        bool scoreExists = await ColumnExists(connection, "AttendanceRecords", "Score");
        if (!scoreExists)
        {
            string addColumnSql = @"
                ALTER TABLE AttendanceRecords
                ADD Score int NULL DEFAULT(0)";
            
            using (var command = new SqlCommand(addColumnSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Score column added to AttendanceRecords table");
            }
        }
    }
    
    // Helper method to ensure ExtraCurricularActivities table exists
    private async Task EnsureExtraCurricularActivitiesTableExists(SqlConnection connection)
    {
        bool tableExists = await TableExists(connection, "ExtraCurricularActivities");
        if (!tableExists)
        {
            string createTableSql = @"
                CREATE TABLE [dbo].[ExtraCurricularActivities](
                    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [StudentId] [nvarchar](50) NOT NULL,
                    [ActivityName] [nvarchar](255) NOT NULL,
                    [ActivityDescription] [nvarchar](max) NULL,
                    [ActivityCategory] [nvarchar](100) NULL,
                    [ActivityDate] [datetime] NOT NULL,
                    [ProofImageData] [varbinary](max) NULL,
                    [ProofImageContentType] [nvarchar](255) NULL,
                    [IsVerified] [bit] NOT NULL DEFAULT(0),
                    [RecordedDate] [datetime] NULL,
                    [Score] [int] NOT NULL DEFAULT(0),
                    [TeacherId] [nvarchar](50) NOT NULL,
                    [CertificateId] [int] NULL,
                    [Rank] [nvarchar](100) NULL
                )";
            
            using (var command = new SqlCommand(createTableSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("ExtraCurricularActivities table created");
            }
        }
        
        // Ensure CertificateId column exists
        bool certificateIdExists = await ColumnExists(connection, "ExtraCurricularActivities", "CertificateId");
        if (!certificateIdExists)
        {
            string addColumnSql = @"
                ALTER TABLE ExtraCurricularActivities
                ADD CertificateId int NULL";
            
            using (var command = new SqlCommand(addColumnSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("CertificateId column added to ExtraCurricularActivities table");
            }
        }
        
        // Ensure Rank column exists
        bool rankExists = await ColumnExists(connection, "ExtraCurricularActivities", "Rank");
        if (!rankExists)
        {
            string addColumnSql = @"
                ALTER TABLE ExtraCurricularActivities
                ADD Rank nvarchar(100) NULL";
            
            using (var command = new SqlCommand(addColumnSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Rank column added to ExtraCurricularActivities table");
            }
        }
        
        // Ensure ActivityCategory column exists
        bool activityCategoryExists = await ColumnExists(connection, "ExtraCurricularActivities", "ActivityCategory");
        if (!activityCategoryExists)
        {
            string addColumnSql = @"
                ALTER TABLE ExtraCurricularActivities
                ADD ActivityCategory nvarchar(100) NULL";
            
            using (var command = new SqlCommand(addColumnSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("ActivityCategory column added to ExtraCurricularActivities table");
            }
        }
        
        // Ensure TeacherId column exists
        bool teacherIdExists = await ColumnExists(connection, "ExtraCurricularActivities", "TeacherId");
        if (!teacherIdExists)
        {
            string addColumnSql = @"
                ALTER TABLE ExtraCurricularActivities
                ADD TeacherId nvarchar(50) NOT NULL DEFAULT('Admin')";
            
            using (var command = new SqlCommand(addColumnSql, connection))
            {
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("TeacherId column added to ExtraCurricularActivities table");
            }
        }
    }
    
    // Add this method to check and add the CertificatesCount column if it doesn't exist
    private async Task EnsureCertificatesCountColumnExists(SqlConnection connection)
    {
        try
        {
            // Check if the CertificatesCount column exists in StudentDetails
            string checkColumnSql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentDetails' 
                AND COLUMN_NAME = 'CertificatesCount'";
                
            using (var command = new SqlCommand(checkColumnSql, connection))
            {
                int columnExists = Convert.ToInt32(await command.ExecuteScalarAsync());
                
                if (columnExists == 0)
                {
                    // Column doesn't exist, add it
                    _logger.LogInformation("Adding CertificatesCount column to StudentDetails table");
                    
                    string addColumnSql = @"
                        ALTER TABLE StudentDetails 
                        ADD CertificatesCount INT NOT NULL DEFAULT 0";
                        
                    using (var alterCommand = new SqlCommand(addColumnSql, connection))
                    {
                        await alterCommand.ExecuteNonQueryAsync();
                    }
                    
                    // Initialize the column with correct counts for all students
                    string updateCountsSql = @"
                        UPDATE sd
                        SET sd.CertificatesCount = (
                            SELECT COUNT(*) 
                            FROM StudentCertificates sc 
                            WHERE sc.StudentId = sd.IdNumber AND sc.IsVerified = 1
                        )
                        FROM StudentDetails sd";
                        
                    using (var updateCommand = new SqlCommand(updateCountsSql, connection))
                    {
                        await updateCommand.ExecuteNonQueryAsync();
                    }
                    
                    _logger.LogInformation("CertificatesCount column added and initialized");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring CertificatesCount column exists");
            // Continue even if this fails
        }
    }
    
    private async Task UpdateStudentScoreForCertificate(SqlConnection connection, int certificateId, int? providedScore = null)
    {
        try
        {
            // First get the student ID, certificate type and score (if extracurricular with score)
            string studentId = "";
            string certificateType = "";
            int? certificateScore = null;
            
            string getCertificateSql = @"
                SELECT StudentId, CertificateType
                FROM StudentCertificates 
                WHERE CertificateId = @CertificateId";
                
            // Check if Score column exists
            bool scoreColumnExists = await ColumnExists(connection, "StudentCertificates", "Score");
            if (scoreColumnExists)
            {
                getCertificateSql = @"
                    SELECT StudentId, CertificateType, Score 
                    FROM StudentCertificates 
                    WHERE CertificateId = @CertificateId";
            }
                
            using (var command = new SqlCommand(getCertificateSql, connection))
            {
                command.Parameters.AddWithValue("@CertificateId", certificateId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        studentId = reader.GetString(0);
                        certificateType = reader.GetString(1);
                        
                        // Get score if present and column exists
                        if (scoreColumnExists && !reader.IsDBNull(2))
                        {
                            certificateScore = reader.GetInt32(2);
                        }
                    }
                    else
                    {
                        // Certificate not found
                        return;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(studentId))
            {
                return;
            }
            
            // Count verified certificates of each type for this student
            int seminarCount = 0;
            int extracurricularCount = 0;
            
            string countSql = @"
                SELECT CertificateType, COUNT(*) 
                FROM StudentCertificates 
                WHERE StudentId = @StudentId AND IsVerified = 1
                GROUP BY CertificateType";
                
            using (var command = new SqlCommand(countSql, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string type = reader.GetString(0);
                        int count = reader.GetInt32(1);
                        
                        if (type == "seminar")
                        {
                            seminarCount = count;
                        }
                        else if (type == "extracurricular")
                        {
                            extracurricularCount = count;
                        }
                    }
                }
            }
            
            // Calculate scores
            decimal seminarScore;
            decimal extracurricularScore = 0;
            
            // Each verified seminar certificate adds 5 points up to a maximum of 100
            seminarScore = Math.Min(seminarCount * 5, 100);
            
            // Check if we need to update extracurricular score
            if (certificateType == "extracurricular")
            {
                // Get current total extracurricular score
                string extracurricularScoreSql = @"
                    SELECT COALESCE(SUM(Score), 0)
                    FROM StudentCertificates
                    WHERE StudentId = @StudentId 
                      AND CertificateType = 'extracurricular'
                      AND IsVerified = 1";
                      
                using (var command = new SqlCommand(extracurricularScoreSql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        extracurricularScore = Convert.ToDecimal(result);
                    }
                }
                
                // Add the new certificate score
                if (providedScore.HasValue && providedScore > 0)
                {
                    // Use the score provided by the teacher
                    extracurricularScore += providedScore.Value;
                }
                else if (certificateScore.HasValue && certificateScore > 0)
                {
                    // Use the score stored in the certificate
                    extracurricularScore += certificateScore.Value;
                }
                else
                {
                    // Use default score of 5
                    extracurricularScore += 5;
                }
                
                // Cap at 100
                extracurricularScore = Math.Min(extracurricularScore, 100);
            }
            else
            {
                // Just get the current extracurricular score from StudentDetails
                string getScoreSql = @"
                    SELECT COALESCE(ExtracurricularScore, 0)
                    FROM StudentDetails
                    WHERE IdNumber = @StudentId";
                    
                using (var command = new SqlCommand(getScoreSql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        extracurricularScore = Convert.ToDecimal(result);
                    }
                }
            }
            
            // Update seminar and extracurricular scores in the database
            string updateScoreSql = @"
                UPDATE StudentDetails
                SET SeminarsScore = @SeminarsScore,
                    ExtracurricularScore = @ExtracurricularScore
                WHERE IdNumber = @StudentId";
                
            using (var command = new SqlCommand(updateScoreSql, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@SeminarsScore", seminarScore);
                command.Parameters.AddWithValue("@ExtracurricularScore", extracurricularScore);
                
                await command.ExecuteNonQueryAsync();
            }
            
            // Call the score calculation service to update the overall score
            using (var httpClient = new HttpClient())
            {
                string baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
                httpClient.BaseAddress = new Uri(baseUrl);
                
                await httpClient.PostAsync(
                    $"/Score/CalculateAllScoresForStudent?studentId={studentId}", 
                    new StringContent(string.Empty));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating student score for certificate");
            // Don't rethrow as this is a background operation
        }
    }

    // Helper method to ensure the Score column exists in StudentCertificates table
    private async Task EnsureScoreColumnExistsInStudentCertificates(SqlConnection connection)
    {
        try
        {
            // Check if the Score column exists in StudentCertificates
            string checkColumnSql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentCertificates' 
                AND COLUMN_NAME = 'Score'";
                
            using (var command = new SqlCommand(checkColumnSql, connection))
            {
                int columnExists = Convert.ToInt32(await command.ExecuteScalarAsync());
                
                if (columnExists == 0)
                {
                    // Column doesn't exist, add it
                    _logger.LogInformation("Adding Score column to StudentCertificates table");
                    
                    string addColumnSql = @"
                        ALTER TABLE StudentCertificates 
                        ADD Score INT NULL";
                        
                    using (var alterCommand = new SqlCommand(addColumnSql, connection))
                    {
                        await alterCommand.ExecuteNonQueryAsync();
                    }
                    
                    _logger.LogInformation("Score column added to StudentCertificates table");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring Score column exists in StudentCertificates table");
            // Continue even if this fails
        }
    }

    [HttpGet]
    [Route("GetStudentExtraCurricularRecords")]
    public async Task<IActionResult> GetStudentExtraCurricularRecords(string studentId)
    {
        try
        {
            List<object> records = new List<object>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if ExtraCurricularActivities table exists
                bool tableExists = false;
                using (var command = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_NAME = 'ExtraCurricularActivities'", 
                    connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    tableExists = Convert.ToInt32(result) > 0;
                }
                
                if (!tableExists)
                {
                    // Return empty list if table doesn't exist yet
                    return Json(records);
                }
                
                // Get student UserId
                string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                string studentUserId = null;
                
                using (var command = new SqlCommand(userIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        studentUserId = result.ToString();
                    }
                }
                
                if (string.IsNullOrEmpty(studentUserId))
                {
                    // If not found in StudentDetails, try Users table directly
                    string directUserIdQuery = "SELECT UserId FROM Users WHERE Username = @Username";
                    using (var command = new SqlCommand(directUserIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Username", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            studentUserId = result.ToString();
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(studentUserId))
                {
                    // Get extra-curricular activities for this student
                    string query = @"
                        SELECT ec.ActivityId, ec.TeacherId, ec.ActivityName, ec.ActivityDescription, ec.ActivityCategory, ec.ActivityDate,
                               ec.RecordedDate, ec.Score, ec.Rank, u.FullName as TeacherName, 
                               CASE WHEN ec.ProofImageData IS NOT NULL THEN 1 ELSE 0 END as HasProofImage
                        FROM ExtraCurricularActivities ec
                        LEFT JOIN Users u ON ec.TeacherId = u.UserId
                        WHERE ec.StudentId = @StudentId
                        ORDER BY ec.ActivityDate DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentUserId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                records.Add(new
                                {
                                    activityId = Convert.ToInt32(reader["ActivityId"]),
                                    activityName = reader["ActivityName"],
                                    activityDescription = reader["ActivityDescription"],
                                    activityCategory = reader["ActivityCategory"],
                                    activityDate = Convert.ToDateTime(reader["ActivityDate"]),
                                    recordedDate = Convert.ToDateTime(reader["RecordedDate"]),
                                    score = Convert.ToDecimal(reader["Score"]),
                                    rank = reader["Rank"] != DBNull.Value ? reader["Rank"].ToString() : null,
                                    teacherName = reader["TeacherName"],
                                    hasProofImage = Convert.ToBoolean(reader["HasProofImage"])
                                });
                            }
                        }
                    }
                }
            }
            
            return Json(records);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving extra-curricular activity records for student {StudentId}", studentId);
            return Json(new List<object>());
        }
    }
    
    [HttpGet]
    [Route("GetStudentChallenges")]
    public async Task<IActionResult> GetStudentChallenges(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Student ID is required" });
        }

        try
        {
            var challenges = new List<object>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // First check if the CompletedChallenges table exists
                bool tableExists = false;
                string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CompletedChallenges'";
                
                using (var command = new SqlCommand(checkTableQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    tableExists = (count > 0);
                }

                if (!tableExists)
                {
                    // If the table doesn't exist, return an empty array
                    return Json(new List<object>());
                }

                // Get all completed challenges for the student
                string query = @"
                    SELECT c.*, u.FullName AS StudentName
                    FROM CompletedChallenges c
                    JOIN StudentDetails sd ON c.StudentId = sd.IdNumber
                    JOIN Users u ON sd.UserId = u.UserId
                    WHERE c.StudentId = @StudentId
                    ORDER BY c.SubmissionDate DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            challenges.Add(new
                            {
                                ChallengeId = reader["ChallengeId"] != DBNull.Value ? Convert.ToInt32(reader["ChallengeId"]) : 0,
                                ChallengeName = reader["ChallengeName"]?.ToString() ?? "Unknown Challenge",
                                Description = reader["Description"]?.ToString() ?? "",
                                ProgrammingLanguage = reader["ProgrammingLanguage"]?.ToString() ?? "Unknown",
                                SubmissionDate = reader["SubmissionDate"] != DBNull.Value ? 
                                    ((DateTime)reader["SubmissionDate"]).ToString("MMM dd, yyyy") : "Unknown",
                                PercentageScore = reader["PercentageScore"] != DBNull.Value ? 
                                    Convert.ToInt32(reader["PercentageScore"]) : 0,
                                StudentName = reader["StudentName"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }

            return Json(challenges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student challenges for student ID {StudentId}", studentId);
            return Json(new { success = false, message = "Error retrieving challenge data: " + ex.Message });
        }
    }
    
    [HttpGet]
    [Route("GetStudentAttendanceRecords")]
    public async Task<IActionResult> GetStudentAttendanceRecords(string studentId)
    {
        try
        {
            List<object> records = new List<object>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if AttendanceRecords table exists
                bool tableExists = false;
                using (var command = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_NAME = 'AttendanceRecords'", 
                    connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    tableExists = Convert.ToInt32(result) > 0;
                }
                
                if (!tableExists)
                {
                    // Return empty list if table doesn't exist yet
                    return Json(records);
                }
                
                // Get student UserId
                string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                string studentUserId = null;
                
                using (var command = new SqlCommand(userIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        studentUserId = result.ToString();
                    }
                }
                
                if (string.IsNullOrEmpty(studentUserId))
                {
                    // If not found in StudentDetails, try Users table directly
                    string directUserIdQuery = "SELECT UserId FROM Users WHERE Username = @Username";
                    using (var command = new SqlCommand(directUserIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Username", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            studentUserId = result.ToString();
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(studentUserId))
                {
                    // Get attendance records for this student
                    string query = @"
                        SELECT ar.Id, ar.EventName, ar.EventDescription, ar.EventDate, ar.ProofImageData, ar.ProofImageContentType,
                               ar.IsVerified, ar.RecordedDate, ar.Score, ar.TeacherId, ar.CertificateId,
                               u.FullName as TeacherName
                        FROM AttendanceRecords ar
                        LEFT JOIN Users u ON ar.TeacherId = u.UserId
                        WHERE ar.StudentId = @StudentId
                        ORDER BY ar.EventDate DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentUserId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                records.Add(new
                                {
                                    attendanceId = Convert.ToInt32(reader["Id"]),
                                    eventName = reader["EventName"],
                                    eventDescription = reader["EventDescription"],
                                    eventDate = Convert.ToDateTime(reader["EventDate"]),
                                    proofImageData = reader["ProofImageData"] != DBNull.Value ? 
                                        Convert.ToBase64String((byte[])reader["ProofImageData"]) : null,
                                    proofImageContentType = reader["ProofImageContentType"]?.ToString() ?? "image/jpeg",
                                    isVerified = Convert.ToBoolean(reader["IsVerified"]),
                                    recordedDate = Convert.ToDateTime(reader["RecordedDate"]),
                                    score = Convert.ToInt32(reader["Score"]),
                                    teacherId = reader["TeacherId"]?.ToString() ?? "Unknown",
                                    teacherName = reader["TeacherName"]?.ToString() ?? "Unknown",
                                    certificateId = reader["CertificateId"] != DBNull.Value ? Convert.ToInt32(reader["CertificateId"]) : 0
                                });
                            }
                        }
                    }
                }
            }
            
            return Json(records);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving attendance records for student {StudentId}", studentId);
            return Json(new List<object>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("SendVerificationPin")]
    public async Task<IActionResult> SendVerificationPin(string email, string name)
    {
        try
        {
            // Check if user is admin
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return Json(new { success = false, message = "Unauthorized. You must be an admin to perform this action." });
            }

            if (string.IsNullOrEmpty(email))
            {
                return Json(new { success = false, message = "Email address is required." });
            }

            // Generate a random 6-digit PIN
            Random random = new Random();
            string pin = random.Next(100000, 999999).ToString();
            
            // Set expiry date (default to 30 days)
            DateTime expiryDate = DateTime.Now.AddDays(30);
            
            // Store PIN in database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the VerificationPINs table exists
                if (!await TableExists(connection, "VerificationPINs"))
                {
                    // Create the table if it doesn't exist
                    string sqlScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "CreateVerificationPINs.sql");
                    if (System.IO.File.Exists(sqlScriptPath))
                    {
                        string sqlScript = System.IO.File.ReadAllText(sqlScriptPath);
                        using (var command = new SqlCommand(sqlScript, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                        _logger.LogInformation("Created VerificationPINs table");
                    }
                    else
                    {
                        _logger.LogError("SQL script for creating VerificationPINs table not found");
                        return Json(new { success = false, message = "Server error: Unable to create verification table." });
                    }
                }
                
                // Insert the PIN into the database
                string insertQuery = @"
                    INSERT INTO VerificationPINs (PIN, CreatedAt, ExpiryDate, IsUsed, UsedById, UsedAt)
                    VALUES (@PIN, GETDATE(), @ExpiryDate, 0, NULL, NULL)";
                
                using (var command = new SqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@PIN", pin);
                    command.Parameters.AddWithValue("@ExpiryDate", expiryDate);
                    await command.ExecuteNonQueryAsync();
                }
            }
            
            // Send the PIN to the user's email
            bool emailSent = await SendPinEmail(email, pin, name);
            
            if (emailSent)
            {
                return Json(new { success = true, message = "PIN sent successfully" });
            }
            else
            {
                return Json(new { success = false, message = "Failed to send email. PIN was generated but not delivered." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending verification PIN");
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // Helper method to send a verification PIN email
    private async Task<bool> SendPinEmail(string email, string pin, string name)
    {
        try
        {
            // Get email settings from configuration
            string smtpServer = _configuration["EmailSettings:SmtpServer"];
            int smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"]);
            string smtpUsername = _configuration["EmailSettings:Username"];
            string smtpPassword = _configuration["EmailSettings:Password"];
            string fromEmail = _configuration["EmailSettings:FromEmail"];
            string fromName = _configuration["EmailSettings:FromName"];
            
            // Use recipient name or default to "User"
            string recipientName = !string.IsNullOrEmpty(name) ? name : "User";
            
            // Create email message
            MailMessage mail = new MailMessage();
            mail.From = new MailAddress(fromEmail, fromName);
            mail.To.Add(new MailAddress(email));
            mail.Subject = "Your EduBadge Verification PIN";
            mail.IsBodyHtml = true;
            
            // Create email body with HTML
            string emailBody = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #e74c3c; color: white; padding: 10px 20px; text-align: center; }}
                    .content {{ padding: 20px; background-color: #fff; border: 1px solid #ddd; }}
                    .pin {{ font-size: 24px; font-weight: bold; text-align: center; padding: 15px; 
                             background-color: #f9f9f9; border: 1px dashed #ccc; margin: 20px 0; letter-spacing: 5px; }}
                    .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #777; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h2>EduBadge Verification PIN</h2>
                    </div>
                    <div class='content'>
                        <p>Hello {recipientName},</p>
                        <p>Your verification PIN for EduBadge is:</p>
                        <div class='pin'>{pin}</div>
                        <p>Please use this PIN to verify your account.</p>
                        <p>This PIN is valid for 30 days and can only be used once.</p>
                        <p>If you did not request this PIN, please ignore this email.</p>
                        <p>Thank you,<br>The EduBadge Team</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated message, please do not reply to this email.</p>
                    </div>
                </div>
            </body>
            </html>";
            
            mail.Body = emailBody;
            
            // Setup SMTP client
            using (SmtpClient smtp = new SmtpClient(smtpServer, smtpPort))
            {
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new System.Net.NetworkCredential(smtpUsername, smtpPassword);
                smtp.EnableSsl = true;
                
                // Send email
                await smtp.SendMailAsync(mail);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending PIN email");
            return false;
        }
    }
}

public class PrivacySettingModel
{
    public bool IsVisible { get; set; }
}

public class ResumeVisibilityModel
{
    public bool IsVisible { get; set; }
}

public class ProfilePictureModel
{
    public string Base64Image { get; set; }
}

public class ResumeModel
{
    public string ResumeFile { get; set; }
    public string ResumeFileName { get; set; }
}

public class MessageModel
{
    public string StudentId { get; set; }
    public string Message { get; set; }
}

public class MessageViewModel
{
    public string Content { get; set; }
    public DateTime SentTime { get; set; }
    public bool IsFromEmployer { get; set; }
    public bool IsRead { get; set; }
    public string EmployerName { get; set; }
    public string Company { get; set; }
    public string StudentName { get; set; }
}

public class StudentMessageViewModel
{
    public int MessageId { get; set; }
    public string Content { get; set; }
    public DateTime SentTime { get; set; }
    public bool IsRead { get; set; }
    public string EmployerId { get; set; }
    public string EmployerName { get; set; }
    public string Company { get; set; }
}

public class UpdateEmployerProfileModel
{
    public string FullName { get; set; }
    public string Username { get; set; }
    public string Company { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public string Description { get; set; }
}

public class ChangePasswordModel
{
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }
}

public class StudentAchievementModel
{
    public string StudentId { get; set; }
    public string Achievements { get; set; }
    public string Comments { get; set; }
    public int Score { get; set; }
}

// Add this extension method to help check if a column exists in a SqlDataReader
public static class SqlDataReaderExtensions
{
    public static bool HasColumn(this SqlDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}    // Score-related functionality has been moved to ScoreController

public class VideoCallAvailabilityModel
{
    public bool IsAvailable { get; set; }
}

// No duplicate class needed here since we're using StudentBadge.Models.StudentCertificateViewModel


