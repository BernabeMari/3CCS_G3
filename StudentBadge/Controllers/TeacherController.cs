using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Dynamic;
using StudentBadge.Models;
using StudentBadge.Services;
using System.Net.Http;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class TeacherController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TeacherController> _logger;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly EmailService _emailService;
        private readonly DatabaseUtilityService _dbUtilityService;

        public TeacherController(IConfiguration configuration, 
            ILogger<TeacherController> logger, 
            IWebHostEnvironment hostingEnvironment, 
            EmailService emailService,
            DatabaseUtilityService dbUtilityService)
        {
            _configuration = configuration;
            _logger = logger;
            _hostingEnvironment = hostingEnvironment;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _emailService = emailService;
            _dbUtilityService = dbUtilityService;
        }

        [HttpGet("Dashboard")]
        public async Task<IActionResult> TeacherDashboard()
        {
            // Get current teacher information from session
            string teacherId = HttpContext.Session.GetString("TeacherId");
            string teacherName = HttpContext.Session.GetString("FullName");
            string department = HttpContext.Session.GetString("Department");
            string position = HttpContext.Session.GetString("Position");
            
            ViewBag.TeacherId = teacherId;
            ViewBag.TeacherName = teacherName;
            ViewBag.Department = department;
            ViewBag.Position = position;

            var students = new List<Student>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
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
                
                if (usingNewTables)
                {
                    // Check if grade columns exist
                    bool hasGradeColumns = await ColumnExists(connection, "StudentDetails", "FirstYearGrade");
                    
                    string gradeColumnsQuery = hasGradeColumns ? 
                    @", sd.FirstYearGrade, sd.SecondYearGrade, sd.ThirdYearGrade, sd.FourthYearGrade, sd.AchievementScore" : "";
                    
                    // Check if GradeLevel column exists
                    bool hasGradeLevelColumn = await ColumnExists(connection, "StudentDetails", "GradeLevel");
                    
                    string gradeLevelQuery = hasGradeLevelColumn ? ", sd.GradeLevel" : "";
                    
                    // Get students using new database structure (Users + StudentDetails tables)
                    string sql = $@"
                        SELECT u.UserId, u.FullName, u.Username, 
                               sd.IdNumber, sd.Course, sd.Section, sd.BadgeColor, sd.Score,
                               sd.Achievements, sd.Comments, sd.ProfilePicturePath,
                               sd.IsProfileVisible, sd.IsResumeVisible, sd.IsTransferee, sd.PreviousSchool {gradeColumnsQuery} {gradeLevelQuery}
                        FROM Users u
                        JOIN StudentDetails sd ON u.UserId = sd.UserId
                        WHERE u.Role = 'student'
                        ORDER BY u.FullName";
                        
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var badgeColor = reader["BadgeColor"]?.ToString() ?? "green";
                                var score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0;
                                
                                // Determine badge name based on score
                                string badgeName = GetBadgeNameFromScore(score);
                                
                                var student = new Student
                                {
                                    IdNumber = reader["IdNumber"]?.ToString() ?? "",
                                    FullName = reader["FullName"]?.ToString() ?? "",
                                    Username = reader["Username"]?.ToString() ?? "",
                                    Course = reader["Course"]?.ToString() ?? "",
                                    Section = reader["Section"]?.ToString() ?? "",
                                    BadgeColor = badgeColor,
                                    BadgeName = badgeName,
                                    Score = score,
                                    Achievements = reader["Achievements"]?.ToString(),
                                    Comments = reader["Comments"]?.ToString(),
                                    ProfilePicturePath = reader["ProfilePicturePath"]?.ToString(),
                                    IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]),
                                    IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]),
                                    IsTransferee = reader["IsTransferee"] != DBNull.Value && Convert.ToBoolean(reader["IsTransferee"]),
                                    PreviousSchool = reader["PreviousSchool"]?.ToString(),
                                };
                                
                                // Add grade data if columns exist
                                if (hasGradeColumns)
                                {
                                    student.FirstYearGrade = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : null;
                                    student.SecondYearGrade = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : null;
                                    student.ThirdYearGrade = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : null;
                                    student.FourthYearGrade = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : null;
                                    student.AchievementScore = reader["AchievementScore"] != DBNull.Value ? Convert.ToDecimal(reader["AchievementScore"]) : null;
                                }
                                
                                // Add grade level if column exists
                                if (hasGradeLevelColumn && reader["GradeLevel"] != DBNull.Value)
                                {
                                    student.GradeLevel = Convert.ToInt32(reader["GradeLevel"]);
                                }
                                else
                                {
                                    // Calculate grade level based on grades
                                    student.GradeLevel = student.CalculateGradeLevel();
                                }
                                
                                students.Add(student);
                            }
                        }
                    }
                }
                else
                {
                    // Get students using old table structure
                    string sql = @"
                        SELECT IdNumber, FullName, Username, Password, Course, Section, BadgeColor, Score,
                               Achievements, Comments, ProfilePicturePath, IsProfileVisible, IsResumeVisible
                        FROM Students
                        ORDER BY FullName";
                        
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var badgeColor = reader["BadgeColor"]?.ToString() ?? "green";
                                var score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0;
                                
                                // Determine badge name based on score
                                string badgeName = GetBadgeNameFromScore(score);
                                
                                var student = new Student
                                {
                                    IdNumber = reader["IdNumber"]?.ToString() ?? "",
                                    FullName = reader["FullName"]?.ToString() ?? "",
                                    Username = reader["Username"]?.ToString() ?? "",
                                    Password = reader["Password"]?.ToString() ?? "",
                                    Course = reader["Course"]?.ToString() ?? "",
                                    Section = reader["Section"]?.ToString() ?? "",
                                    BadgeColor = badgeColor,
                                    BadgeName = badgeName,
                                    Score = score,
                                    Achievements = reader["Achievements"]?.ToString(),
                                    Comments = reader["Comments"]?.ToString(),
                                    ProfilePicturePath = reader["ProfilePicturePath"]?.ToString(),
                                    IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]),
                                    IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]),
                                    // Default to 1st year for old structure
                                    GradeLevel = 1
                                };
                                
                                students.Add(student);
                            }
                        }
                    }
                }
            }
            
            // Get the total count of students
            ViewBag.TotalStudentCount = students.Count;
            
            // Load attendance records for this teacher
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Get attendance records for display
                string query = @"
                    SELECT ar.AttendanceId, ar.StudentId, ar.EventName, ar.EventDate, ar.Score, u.FullName as StudentName
                    FROM AttendanceRecords ar
                    JOIN Users u ON ar.StudentId = u.UserId
                    WHERE ar.TeacherId = @TeacherId
                    ORDER BY ar.EventDate DESC";
                    
                List<dynamic> attendanceRecords = new List<dynamic>();
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", teacherId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            attendanceRecords.Add(new
                            {
                                AttendanceId = reader["AttendanceId"],
                                StudentId = reader["StudentId"],
                                StudentName = reader["StudentName"],
                                EventName = reader["EventName"],
                                EventDate = Convert.ToDateTime(reader["EventDate"]),
                                Score = reader["Score"]
                            });
                        }
                    }
                }
                
                ViewBag.AttendanceRecords = attendanceRecords;
            }
            
            return View("~/Views/Dashboard/TeacherDashboard.cshtml", students);
        }

        [HttpPost("AddTeacher")]
        public async Task<IActionResult> AddTeacher(string fullName, string username, string password, string department, string position)
        {
            try
            {
                if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    TempData["Error"] = "Full name, username, and password are required.";
                    return RedirectToAction("AdminDashboard", "Dashboard");
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if we're using the new database structure
                    bool usingNewTables = false;
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'TeacherDetails'";
                    
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        usingNewTables = (count > 0);
                    }
                    
                    if (usingNewTables)
                    {
                        // Check if the username already exists
                        string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                        
                        using (var command = new SqlCommand(checkUsernameQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Username", username);
                            
                            int userCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                            if (userCount > 0)
                            {
                                TempData["Error"] = "Username already exists.";
                                return RedirectToAction("AdminDashboard", "Dashboard");
                            }
                        }
                        
                        // Generate a unique TeacherId
                        string teacherId = $"TCH{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                        
                        // Insert the new teacher in Users table first
                        string insertUserQuery = @"
                            INSERT INTO Users (UserId, FullName, Username, Password, Role)
                            VALUES (@UserId, @FullName, @Username, @Password, 'teacher')";
                            
                        using (var command = new SqlCommand(insertUserQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", teacherId);
                            command.Parameters.AddWithValue("@FullName", fullName);
                            command.Parameters.AddWithValue("@Username", username);
                            command.Parameters.AddWithValue("@Password", password);
                            
                            await command.ExecuteNonQueryAsync();
                        }
                        
                        // Then insert into TeacherDetails table
                        string insertDetailsQuery = @"
                            INSERT INTO TeacherDetails (UserId, Department, Position)
                            VALUES (@UserId, @Department, @Position)";
                            
                        using (var command = new SqlCommand(insertDetailsQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", teacherId);
                            command.Parameters.AddWithValue("@Department", department ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Position", position ?? (object)DBNull.Value);
                            
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Check if the username already exists in old structure
                        string checkUsernameQuery = "SELECT COUNT(*) FROM dbo.Teachers WHERE Username = @Username";
                        
                        using (var command = new SqlCommand(checkUsernameQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Username", username);
                            
                            int userCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                            if (userCount > 0)
                            {
                                TempData["Error"] = "Username already exists.";
                                return RedirectToAction("AdminDashboard", "Dashboard");
                            }
                        }
                        
                        // Generate a unique TeacherId
                        string teacherId = $"TCH{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                        
                        // Check if the ID already exists
                        string checkIdQuery = "SELECT COUNT(*) FROM dbo.Teachers WHERE TeacherId = @TeacherId";
                        bool idExists = true;
                        
                        while (idExists)
                        {
                            using (var command = new SqlCommand(checkIdQuery, connection))
                            {
                                command.Parameters.AddWithValue("@TeacherId", teacherId);
                                
                                int idCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                                idExists = (idCount > 0);
                                
                                if (idExists)
                                {
                                    // Generate a new ID if this one already exists
                                    teacherId = $"TCH{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                                }
                            }
                        }
                        
                        // Insert the new teacher
                        string insertQuery = @"
                            INSERT INTO dbo.Teachers (TeacherId, FullName, Username, Password, Department, Position)
                            VALUES (@TeacherId, @FullName, @Username, @Password, @Department, @Position)";
                            
                        using (var command = new SqlCommand(insertQuery, connection))
                        {
                            command.Parameters.AddWithValue("@TeacherId", teacherId);
                            command.Parameters.AddWithValue("@FullName", fullName);
                            command.Parameters.AddWithValue("@Username", username);
                            command.Parameters.AddWithValue("@Password", password);
                            command.Parameters.AddWithValue("@Department", department ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Position", position ?? (object)DBNull.Value);
                            
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                
                TempData["Success"] = "Teacher added successfully.";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error adding teacher: " + ex.Message;
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
        }

        [HttpDelete("DeleteTeacher")]
        [HttpPost("DeleteTeacher")] // Add POST method support since the AJAX call might use POST
        public async Task<IActionResult> DeleteTeacher(string teacherId)
        {
            try
            {
                _logger.LogInformation($"DeleteTeacher called with ID: {teacherId}");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    _logger.LogWarning("DeleteTeacher called with empty ID");
                    return Json(new { success = false, message = "Teacher ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check what tables we have
                    bool teacherDetailsExists = await TableExists(connection, "TeacherDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    
                    _logger.LogInformation($"Table check: TeacherDetails={teacherDetailsExists}, Users={usersTableExists}");
                    
                    int rowsAffected = 0;
                    
                    // For new schema with Users and TeacherDetails tables
                    if (teacherDetailsExists && usersTableExists)
                    {
                        _logger.LogInformation("Using new table schema (TeacherDetails + Users)");
                        
                        // First, check if teacher exists and get the UserId if using new schema
                        string getUserIdQuery = "SELECT UserId FROM TeacherDetails WHERE UserId = @TeacherId";
                        string userId = teacherId; // Initially assume TeacherId is the UserId
                        
                        using (var command = new SqlCommand(getUserIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@TeacherId", teacherId);
                            var result = await command.ExecuteScalarAsync();
                            
                            if (result != null && result != DBNull.Value)
                            {
                                _logger.LogInformation($"Found teacher with UserId: {userId}");
                            }
                            else
                            {
                                _logger.LogWarning($"Teacher with ID {teacherId} not found in TeacherDetails");
                                
                                return Json(new { success = false, message = "Teacher not found." });
                            }
                        }
                        
                        // Delete from TeacherDetails first (child table)
                        string deleteDetailsQuery = "DELETE FROM TeacherDetails WHERE UserId = @UserId";
                        
                        using (var command = new SqlCommand(deleteDetailsQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            
                            int detailsRowsAffected = await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Deleted {detailsRowsAffected} rows from TeacherDetails");
                        }
                        
                        // Now delete from Users (parent table)
                        string deleteUserQuery = "DELETE FROM Users WHERE UserId = @UserId";
                        
                        using (var command = new SqlCommand(deleteUserQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            
                            rowsAffected = await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Deleted {rowsAffected} rows from Users");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Using old table schema (Teachers table)");
                        
                        // Delete from old Teachers table
                        string deleteQuery = "DELETE FROM Teachers WHERE TeacherId = @TeacherId";
                        
                        using (var command = new SqlCommand(deleteQuery, connection))
                        {
                            command.Parameters.AddWithValue("@TeacherId", teacherId);
                            
                            rowsAffected = await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Deleted {rowsAffected} rows from Teachers");
                        }
                    }
                    
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true, message = "Teacher deleted successfully." });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Teacher not found or could not be deleted." });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting teacher with ID {TeacherId}", teacherId);
                return Json(new { success = false, message = "Error deleting teacher: " + ex.Message });
            }
        }
        
        [HttpPost("UpdateTeacher")]
        public async Task<IActionResult> UpdateTeacher(string TeacherId, string FullName, string Username, string Password, string Department, string Position)
        {
            try
            {
                _logger.LogInformation($"UpdateTeacher called for ID: {TeacherId}");
                
                if (string.IsNullOrEmpty(TeacherId) || string.IsNullOrEmpty(FullName) || string.IsNullOrEmpty(Username))
                {
                    TempData["Error"] = "Teacher ID, Full Name, and Username are required.";
                    return RedirectToAction("AdminDashboard", "Dashboard");
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check what tables we have
                    bool teacherDetailsExists = await TableExists(connection, "TeacherDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    
                    // For new schema with Users and TeacherDetails tables
                    if (teacherDetailsExists && usersTableExists)
                    {
                        // First, check if teacher exists
                        string checkTeacherQuery = "SELECT COUNT(*) FROM Users u JOIN TeacherDetails td ON u.UserId = td.UserId WHERE u.UserId = @UserId";
                        
                        using (var command = new SqlCommand(checkTeacherQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", TeacherId);
                            
                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            if (count == 0)
                            {
                                TempData["Error"] = "Teacher not found.";
                                return RedirectToAction("AdminDashboard", "Dashboard");
                            }
                        }
                        
                        // Update Users table
                        string updateUserQuery = @"
                            UPDATE Users 
                            SET FullName = @FullName, Username = @Username";
                            
                        // Add password update if provided
                        if (!string.IsNullOrEmpty(Password))
                        {
                            updateUserQuery += ", Password = @Password";
                        }
                        
                        updateUserQuery += " WHERE UserId = @UserId";
                        
                        using (var command = new SqlCommand(updateUserQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", TeacherId);
                            command.Parameters.AddWithValue("@FullName", FullName);
                            command.Parameters.AddWithValue("@Username", Username);
                            
                            if (!string.IsNullOrEmpty(Password))
                            {
                                command.Parameters.AddWithValue("@Password", Password);
                            }
                            
                            await command.ExecuteNonQueryAsync();
                        }
                        
                        // Update TeacherDetails table
                        string updateDetailsQuery = @"
                            UPDATE TeacherDetails 
                            SET Department = @Department, Position = @Position
                            WHERE UserId = @UserId";
                            
                        using (var command = new SqlCommand(updateDetailsQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", TeacherId);
                            command.Parameters.AddWithValue("@Department", Department ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Position", Position ?? (object)DBNull.Value);
                            
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Check if teacher exists in old structure
                        string checkTeacherQuery = "SELECT COUNT(*) FROM Teachers WHERE TeacherId = @TeacherId";
                        
                        using (var command = new SqlCommand(checkTeacherQuery, connection))
                        {
                            command.Parameters.AddWithValue("@TeacherId", TeacherId);
                            
                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            if (count == 0)
                            {
                                TempData["Error"] = "Teacher not found.";
                                return RedirectToAction("AdminDashboard", "Dashboard");
                            }
                        }
                        
                        // Update in old structure
                        string updateQuery = @"
                            UPDATE Teachers 
                            SET FullName = @FullName, Username = @Username, 
                                Department = @Department, Position = @Position";
                                
                        // Add password update if provided
                        if (!string.IsNullOrEmpty(Password))
                        {
                            updateQuery += ", Password = @Password";
                        }
                        
                        updateQuery += " WHERE TeacherId = @TeacherId";
                        
                        using (var command = new SqlCommand(updateQuery, connection))
                        {
                            command.Parameters.AddWithValue("@TeacherId", TeacherId);
                            command.Parameters.AddWithValue("@FullName", FullName);
                            command.Parameters.AddWithValue("@Username", Username);
                            command.Parameters.AddWithValue("@Department", Department ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Position", Position ?? (object)DBNull.Value);
                            
                            if (!string.IsNullOrEmpty(Password))
                            {
                                command.Parameters.AddWithValue("@Password", Password);
                            }
                            
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                
                TempData["Success"] = "Teacher updated successfully.";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating teacher with ID {TeacherId}", TeacherId);
                TempData["Error"] = "Error updating teacher: " + ex.Message;
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
        }

        [HttpGet("GetStudentAttendanceRecords")]
        public async Task<IActionResult> GetStudentAttendanceRecords(string studentId)
        {
            try
            {
                _logger.LogInformation("GetStudentAttendanceRecords called with studentId: {StudentId}", studentId);
                
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogWarning("GetStudentAttendanceRecords: studentId is null or empty");
                    return Json(new List<object>());
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if AttendanceRecords table exists
                    bool tableExists = false;
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'AttendanceRecords'";
                    
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        tableExists = (count > 0);
                        _logger.LogInformation("AttendanceRecords table exists: {TableExists}", tableExists);
                    }
                    
                    if (!tableExists)
                    {
                        _logger.LogWarning("GetStudentAttendanceRecords: AttendanceRecords table does not exist");
                        return Json(new List<object>());
                    }
                    
                    // Get the student's UserId from multiple sources
                    _logger.LogInformation("Attempting to resolve UserId for student with ID/Username: {StudentId}", studentId);
                    
                    string studentUserId = null;
                    
                    // First try: Look in StudentDetails by IdNumber
                    string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(userIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            studentUserId = result.ToString();
                            _logger.LogInformation("Found UserId {UserId} for StudentId {StudentId} in StudentDetails table", studentUserId, studentId);
                        }
                    }
                    
                    // Second try: Check Users table by Username
                    if (string.IsNullOrEmpty(studentUserId))
                    {
                        string directUserIdQuery = "SELECT UserId FROM Users WHERE Username = @Username";
                        using (var command = new SqlCommand(directUserIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Username", studentId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                studentUserId = result.ToString();
                                _logger.LogInformation("Found UserId {UserId} for Username {Username} in Users table", studentUserId, studentId);
                            }
                        }
                    }
                    
                    // Final attempt: Check if StudentId itself is a UserId in Users
                    if (string.IsNullOrEmpty(studentUserId))
                    {
                        string validateUserIdQuery = "SELECT COUNT(*) FROM Users WHERE UserId = @UserId";
                        using (var command = new SqlCommand(validateUserIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", studentId);
                            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            if (count > 0)
                            {
                                studentUserId = studentId;
                                _logger.LogInformation("Confirmed that the supplied StudentId {StudentId} is itself a valid UserId", studentId);
                            }
                        }
                    }
                    
                    // Default fallback: use the studentId directly as a last resort
                    if (string.IsNullOrEmpty(studentUserId))
                    {
                        studentUserId = studentId;
                        _logger.LogWarning("Could not find UserId for StudentId {StudentId}, using StudentId as UserId", studentId);
                    }
                    
                    // Try querying with the exact student ID first (for backward compatibility)
                    var directRecords = new List<object>();
                    string directQuery = @"
                        SELECT AttendanceId, TeacherId, EventName, EventDescription, EventDate, 
                               ProofImageData, ProofImageContentType, RecordedDate, Score, IsVerified
                        FROM AttendanceRecords
                        WHERE StudentId = @DirectStudentId
                        ORDER BY EventDate DESC, RecordedDate DESC";
                    
                    using (var command = new SqlCommand(directQuery, connection))
                    {
                        command.Parameters.AddWithValue("@DirectStudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                directRecords.Add(new
                                {
                                    attendanceId = reader["AttendanceId"],
                                    teacherId = reader["TeacherId"] != DBNull.Value ? reader["TeacherId"].ToString() : null,
                                    eventName = reader["EventName"].ToString(),
                                    eventDescription = reader["EventDescription"] != DBNull.Value ? reader["EventDescription"].ToString() : null,
                                    eventDate = Convert.ToDateTime(reader["EventDate"]),
                                    hasProofImage = reader["ProofImageData"] != DBNull.Value,
                                    recordedDate = Convert.ToDateTime(reader["RecordedDate"]),
                                    score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                                    isVerified = reader["IsVerified"] != DBNull.Value && Convert.ToBoolean(reader["IsVerified"])
                                });
                            }
                        }
                    }
                    
                    _logger.LogInformation("Retrieved {Count} attendance records with direct StudentId {StudentId}", 
                        directRecords.Count, studentId);
                    
                    // If we found records directly, return them
                    if (directRecords.Count > 0)
                    {
                        return Json(directRecords);
                    }
                    
                    // Otherwise, try with the resolved UserId
                    var resolvedRecords = new List<object>();
                    string resolvedQuery = @"
                        SELECT AttendanceId, TeacherId, EventName, EventDescription, EventDate, 
                               ProofImageData, ProofImageContentType, RecordedDate, Score, IsVerified
                        FROM AttendanceRecords
                        WHERE StudentId = @ResolvedStudentId
                        ORDER BY EventDate DESC, RecordedDate DESC";
                    
                    using (var command = new SqlCommand(resolvedQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ResolvedStudentId", studentUserId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                resolvedRecords.Add(new
                                {
                                    attendanceId = reader["AttendanceId"],
                                    teacherId = reader["TeacherId"] != DBNull.Value ? reader["TeacherId"].ToString() : null,
                                    eventName = reader["EventName"].ToString(),
                                    eventDescription = reader["EventDescription"] != DBNull.Value ? reader["EventDescription"].ToString() : null,
                                    eventDate = Convert.ToDateTime(reader["EventDate"]),
                                    hasProofImage = reader["ProofImageData"] != DBNull.Value,
                                    recordedDate = Convert.ToDateTime(reader["RecordedDate"]),
                                    score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                                    isVerified = reader["IsVerified"] != DBNull.Value && Convert.ToBoolean(reader["IsVerified"])
                                });
                            }
                        }
                    }
                    
                    _logger.LogInformation("Retrieved {Count} attendance records with resolved UserId {UserId} for StudentId {StudentId}", 
                        resolvedRecords.Count, studentUserId, studentId);
                    
                    // Debug: Query all records in the table
                    int totalRecords = 0;
                    using (var command = new SqlCommand("SELECT COUNT(*) FROM AttendanceRecords", connection))
                    {
                        totalRecords = Convert.ToInt32(await command.ExecuteScalarAsync());
                    }
                    _logger.LogInformation("Total attendance records in database: {TotalRecords}", totalRecords);
                    
                    return Json(resolvedRecords);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving attendance records for student {StudentId}", studentId);
                return Json(new List<object>());
            }
        }

        [HttpPost("DeleteChallengeRecords")]
        public async Task<IActionResult> DeleteChallengeRecords(string studentId, string yearLevel)
        {
            try
            {
                _logger.LogInformation($"DeleteChallengeRecords called for StudentId: {studentId}, YearLevel: {yearLevel}");
                
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogWarning("DeleteChallengeRecords called with empty StudentId");
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                if (string.IsNullOrEmpty(yearLevel))
                {
                    _logger.LogWarning("DeleteChallengeRecords called with empty YearLevel");
                    return Json(new { success = false, message = "Year level is required." });
                }
                
                // First, resolve the studentId to get the actual UUID used in the database
                string resolvedStudentId = studentId;
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(userIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            resolvedStudentId = result.ToString();
                            _logger.LogInformation($"Resolved StudentId {studentId} to UserId {resolvedStudentId}");
                        }
                    }
                }
                
                int recordsDeleted = 0;
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Start a transaction to ensure data consistency
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // First, get all challenge IDs for this year level
                            string challengeIdsQuery = @"
                                SELECT ChallengeId 
                                FROM Challenges
                                WHERE YearLevel = @YearLevel";
                            
                            List<int> challengeIds = new List<int>();
                            
                            using (var command = new SqlCommand(challengeIdsQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@YearLevel", yearLevel);
                                
                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
                                    {
                                        challengeIds.Add(Convert.ToInt32(reader["ChallengeId"]));
                                    }
                                }
                            }
                            
                            _logger.LogInformation($"Found {challengeIds.Count} challenges for year level {yearLevel}: {string.Join(", ", challengeIds)}");
                            
                            if (challengeIds.Count == 0)
                            {
                                // Check if there are any direct submissions that match this year's challenges
                                string directSubmissionsQuery = @"
                                    SELECT COUNT(*) FROM ChallengeSubmissions cs
                                    JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                                    WHERE c.YearLevel = @YearLevel
                                    AND cs.StudentId = @StudentId";
                                
                                int directSubmissions = 0;
                                using (var command = new SqlCommand(directSubmissionsQuery, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                                    command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                    directSubmissions = Convert.ToInt32(await command.ExecuteScalarAsync());
                                }
                                
                                _logger.LogInformation($"Direct query found {directSubmissions} submissions for student {resolvedStudentId} in year level {yearLevel}");
                                
                                if (directSubmissions > 0)
                                {
                                    // If direct submissions exist but we couldn't get challenge IDs, try getting all challenges
                                    string allChallengesQuery = @"
                                        SELECT DISTINCT c.ChallengeId 
                                        FROM Challenges c
                                        JOIN ChallengeSubmissions cs ON c.ChallengeId = cs.ChallengeId
                                        WHERE c.YearLevel = @YearLevel
                                        AND cs.StudentId = @StudentId";
                                    
                                    using (var command = new SqlCommand(allChallengesQuery, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@YearLevel", yearLevel);
                                        command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                        
                                        using (var reader = await command.ExecuteReaderAsync())
                                        {
                                            while (await reader.ReadAsync())
                                            {
                                                challengeIds.Add(Convert.ToInt32(reader["ChallengeId"]));
                                            }
                                        }
                                    }
                                    
                                    _logger.LogInformation($"Retrieved {challengeIds.Count} challenge IDs from direct submissions query");
                            }
                            
                            if (challengeIds.Count == 0)
                            {
                                transaction.Commit();
                                return Json(new { success = true, message = $"No challenges found for year level {yearLevel}. Nothing to delete." });
                                }
                            }
                            
                            // Get all submission IDs that need to be deleted
                            List<int> submissionIds = new List<int>();
                            
                            // Debug: Print total number of submissions in database
                            string totalSubmissionsQuery = "SELECT COUNT(*) FROM ChallengeSubmissions";
                            int totalSubmissions = 0;
                            using (var command = new SqlCommand(totalSubmissionsQuery, connection, transaction))
                            {
                                totalSubmissions = Convert.ToInt32(await command.ExecuteScalarAsync());
                            }
                            _logger.LogInformation($"Total submissions in database: {totalSubmissions}");
                            
                            // First check if there's a formatting issue with the StudentId
                            string checkStudentIdFormatQuery = "SELECT TOP 1 StudentId FROM ChallengeSubmissions";
                            string sampleStudentId = null;
                            using (var command = new SqlCommand(checkStudentIdFormatQuery, connection, transaction))
                            {
                                var result = await command.ExecuteScalarAsync();
                                if (result != null)
                                {
                                    sampleStudentId = result.ToString();
                                }
                            }
                            _logger.LogInformation($"Sample StudentId from database: {sampleStudentId}, Using resolved StudentId: {resolvedStudentId}");
                            
                            // Try to get submissions for each challenge with the resolved StudentId
                            foreach (var challengeId in challengeIds)
                            {
                                string submissionIdsQuery = @"
                                    SELECT SubmissionId 
                                    FROM ChallengeSubmissions
                                    WHERE ChallengeId = @ChallengeId
                                    AND StudentId = @StudentId";
                                
                                using (var command = new SqlCommand(submissionIdsQuery, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                                    command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                    
                                    using (var reader = await command.ExecuteReaderAsync())
                                    {
                                        int count = 0;
                                        while (await reader.ReadAsync())
                                        {
                                            submissionIds.Add(Convert.ToInt32(reader["SubmissionId"]));
                                            count++;
                                        }
                                        _logger.LogInformation($"Found {count} submissions for challenge {challengeId}");
                                }
                            }
                                
                                // If no submissions found with exact match, try a more flexible approach
                                if (submissionIds.Count == 0)
                                {
                                    string resolvedUserIdSubmissionsQuery = @"
                                        SELECT cs.SubmissionId 
                                        FROM ChallengeSubmissions cs
                                        WHERE cs.ChallengeId = @ChallengeId
                                        AND cs.StudentId IN (
                                            SELECT IdNumber FROM StudentDetails WHERE IdNumber = @StudentId
                                            UNION
                                            SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId
                                        )";
                                    
                                    using (var command = new SqlCommand(resolvedUserIdSubmissionsQuery, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@ChallengeId", challengeId);
                                        command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                        
                                        using (var reader = await command.ExecuteReaderAsync())
                                        {
                                            int count = 0;
                                            while (await reader.ReadAsync())
                                            {
                                                submissionIds.Add(Convert.ToInt32(reader["SubmissionId"]));
                                                count++;
                                            }
                                            _logger.LogInformation($"Found {count} submissions with resolved ID for challenge {challengeId}");
                                        }
                                    }
                                }
                                
                                // Try one more approach - direct query for this challenge
                                if (submissionIds.Count == 0)
                                {
                                    string directQuery = $@"
                                        SELECT SubmissionId, StudentId FROM ChallengeSubmissions 
                                        WHERE ChallengeId = {challengeId}";
                                    
                                    List<Tuple<int, string>> allSubmissions = new List<Tuple<int, string>>();
                                    
                                    using (var command = new SqlCommand(directQuery, connection, transaction))
                                    {
                                        using (var reader = await command.ExecuteReaderAsync())
                                        {
                                            while (await reader.ReadAsync())
                                            {
                                                int subId = Convert.ToInt32(reader["SubmissionId"]);
                                                string studId = reader["StudentId"]?.ToString() ?? "null";
                                                allSubmissions.Add(new Tuple<int, string>(subId, studId));
                                            }
                                        }
                                    }
                                    
                                    _logger.LogInformation($"Direct query found {allSubmissions.Count} total submissions for challenge {challengeId}");
                                    if (allSubmissions.Count > 0)
                                    {
                                        _logger.LogInformation($"Sample StudentIds in ChallengeSubmissions: {string.Join(", ", allSubmissions.Take(5).Select(t => t.Item2))}");
                                    }
                                }
                            }
                            
                            // As a fallback, try a direct join approach to find relevant submissions
                            if (submissionIds.Count == 0)
                            {
                                string directJoinQuery = @"
                                    SELECT cs.SubmissionId
                                    FROM ChallengeSubmissions cs
                                    JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                                    WHERE c.YearLevel = @YearLevel
                                    AND cs.StudentId = @StudentId";
                                
                                using (var command = new SqlCommand(directJoinQuery, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                                    command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                    
                                    using (var reader = await command.ExecuteReaderAsync())
                                    {
                                        while (await reader.ReadAsync())
                                        {
                                            submissionIds.Add(Convert.ToInt32(reader["SubmissionId"]));
                                        }
                                    }
                                }
                                
                                _logger.LogInformation($"Direct join query found {submissionIds.Count} submissions");
                            }
                            
                            _logger.LogInformation($"Total submission IDs found to delete: {submissionIds.Count}");
                            
                            // Delete all related challenge answers
                            if (submissionIds.Count > 0)
                            {
                                foreach (var submissionId in submissionIds)
                                {
                                    string deleteAnswersQuery = @"
                                        DELETE FROM ChallengeAnswers
                                        WHERE SubmissionId = @SubmissionId";
                                    
                                    using (var command = new SqlCommand(deleteAnswersQuery, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@SubmissionId", submissionId);
                                        int answersDeleted = await command.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {answersDeleted} answers for submission {submissionId}");
                                    }
                                }
                                
                                // Delete all submissions
                                string deleteSubmissionsQuery = @"
                                    DELETE FROM ChallengeSubmissions
                                    WHERE SubmissionId IN (";
                                
                                deleteSubmissionsQuery += string.Join(",", submissionIds);
                                deleteSubmissionsQuery += ")";
                                
                                using (var command = new SqlCommand(deleteSubmissionsQuery, connection, transaction))
                                {
                                    recordsDeleted = await command.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Deleted {recordsDeleted} challenge submissions");
                                }
                            }
                            else
                            {
                                // If still no submissions, try a direct approach with proper parameterization
                                try
                                {
                                    // First try to get the submissions that need to be deleted
                                    string findSubmissionsQuery = @"
                                        SELECT cs.SubmissionId 
                                        FROM ChallengeSubmissions cs
                                        INNER JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                                        WHERE c.YearLevel = @YearLevel
                                        AND cs.StudentId = @StudentId";

                                    using (var command = new SqlCommand(findSubmissionsQuery, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@YearLevel", yearLevel);
                                        command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                        
                                        using (var reader = await command.ExecuteReaderAsync())
                                        {
                                            while (await reader.ReadAsync())
                                            {
                                                submissionIds.Add(Convert.ToInt32(reader["SubmissionId"]));
                                            }
                                        }
                                    }
                                    
                                    _logger.LogInformation($"Final attempt found {submissionIds.Count} submissions to delete");
                                    
                                    if (submissionIds.Count > 0)
                                    {
                                        // First delete associated answers
                                        foreach (var submissionId in submissionIds)
                                        {
                                            string deleteAnswersQuery = @"
                                                DELETE FROM ChallengeAnswers
                                                WHERE SubmissionId = @SubmissionId";
                                            
                                            using (var command = new SqlCommand(deleteAnswersQuery, connection, transaction))
                                            {
                                                command.Parameters.AddWithValue("@SubmissionId", submissionId);
                                                await command.ExecuteNonQueryAsync();
                                            }
                                        }
                                        
                                        // Then delete the submissions
                                        foreach (var submissionId in submissionIds)
                                        {
                                            string deleteSubmissionQuery = @"
                                                DELETE FROM ChallengeSubmissions
                                                WHERE SubmissionId = @SubmissionId";
                                            
                                            using (var command = new SqlCommand(deleteSubmissionQuery, connection, transaction))
                                            {
                                                command.Parameters.AddWithValue("@SubmissionId", submissionId);
                                                int affected = await command.ExecuteNonQueryAsync();
                                                recordsDeleted += affected;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        _logger.LogWarning("No submissions found to delete after all attempts");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error in final delete attempt");
                                }
                            }
                            
                            transaction.Commit();
                            
                            string message = recordsDeleted > 0 
                                ? $"Successfully deleted {recordsDeleted} challenge records for year level {yearLevel}." 
                                : $"No challenge records found for this student in year level {yearLevel}.";
                                
                            // If records were deleted, recalculate the student's scores
                            if (recordsDeleted > 0)
                            {
                                // Record the deleted challenges in the DeletedChallenges table for future reference
                                foreach (var challengeId in challengeIds)
                                {
                                    try
                                    {
                                        // Check if the table exists first
                                        bool tableExists = await TableExists(connection, "DeletedChallenges");
                                        
                                        if (!tableExists)
                                        {
                                            // Create the table if it doesn't exist
                                            string createTableSql = @"
                                                CREATE TABLE DeletedChallenges (
                                                    Id INT IDENTITY(1,1) PRIMARY KEY,
                                                    StudentId NVARCHAR(128) NOT NULL,
                                                    ChallengeId INT NOT NULL,
                                                    YearLevel NVARCHAR(50) NOT NULL,
                                                    DeletedDate DATETIME NOT NULL DEFAULT GETDATE(),
                                                    CONSTRAINT UC_StudentChallenge UNIQUE (StudentId, ChallengeId)
                                                )";
                                            
                                            using (var createCmd = new SqlCommand(createTableSql, connection, transaction))
                                            {
                                                await createCmd.ExecuteNonQueryAsync();
                                                _logger.LogInformation("DeletedChallenges table created successfully");
                                            }
                                        }
                                        
                                        // Insert the deleted challenge record
                                        string insertDeletedChallengeSql = @"
                                            INSERT INTO DeletedChallenges (StudentId, ChallengeId, YearLevel)
                                            VALUES (@StudentId, @ChallengeId, @YearLevel)";
                                            
                                        using (var insertCmd = new SqlCommand(insertDeletedChallengeSql, connection, transaction))
                                        {
                                            insertCmd.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                                            insertCmd.Parameters.AddWithValue("@ChallengeId", challengeId);
                                            insertCmd.Parameters.AddWithValue("@YearLevel", yearLevel);
                                            
                                            try
                                            {
                                                await insertCmd.ExecuteNonQueryAsync();
                                                _logger.LogInformation($"Recorded deleted challenge {challengeId} for student {resolvedStudentId}");
                                            }
                                            catch (SqlException ex)
                                            {
                                                // Handle unique constraint violation (if this student/challenge combo is already in the table)
                                                if (ex.Number == 2627 || ex.Number == 2601)
                                                {
                                                    _logger.LogInformation($"Deleted challenge {challengeId} already tracked for student {resolvedStudentId}");
                                                }
                                                else
                                                {
                                                    throw;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, $"Error recording deleted challenge {challengeId} for student {resolvedStudentId}");
                                        // Continue with the next challenge even if there's an error
                                    }
                                }
                            
                                await RecalculateStudentScores(studentId);
                            }
                                
                            return Json(new { success = true, message = message, recordsDeleted = recordsDeleted });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error while deleting challenge records for StudentId: {StudentId}, YearLevel: {YearLevel}", studentId, yearLevel);
                            transaction.Rollback();
                            return Json(new { success = false, message = $"Error deleting challenge records: {ex.Message}" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeleteChallengeRecords for StudentId: {StudentId}, YearLevel: {YearLevel}", studentId, yearLevel);
                return Json(new { success = false, message = $"Server error: {ex.Message}" });
            }
        }
        
      // Recalculate student's challenge scores and overall score
private async Task RecalculateStudentScores(string studentId)
{
    try
    {
        _logger.LogInformation($"Recalculating scores for student: {studentId}");
        
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            
            // Get the student's current grade level (year)
            int gradeLevel = await GetStudentGradeLevel(connection, studentId);
            _logger.LogInformation($"Student {studentId} current grade level: {gradeLevel}");
            
            // Then get the resolved StudentId (UUID)
            string resolvedStudentId = await ResolveStudentUserId(connection, studentId);
            
            // Get list of valid year levels for this student based on grade level
            List<string> validYearLevels = await GetValidYearLevelsForGradeLevel(connection, gradeLevel);
            _logger.LogInformation($"Valid year levels for grade {gradeLevel}: {string.Join(", ", validYearLevels)}");
            
            // Get ALL challenge submissions for this student
            decimal totalEarned = 0;
            int submissionCount = 0;
            // Keep track of all challenges the student has submitted to
            HashSet<int> submittedChallengeIds = new HashSet<int>();

            string allChallengesQuery = @"
                SELECT 
                    cs.SubmissionId,
                    cs.ChallengeId,
                    c.ChallengeName,
                    c.YearLevel,
                    cs.PointsEarned,
                    cs.TotalPoints,
                    cs.SubmissionDate
                FROM ChallengeSubmissions cs
                INNER JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                WHERE cs.StudentId = @StudentId";

            _logger.LogInformation($"Retrieving detailed information for all challenge submissions for student ID '{resolvedStudentId}'");

            using (var command = new SqlCommand(allChallengesQuery, connection))
            {
                command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        submissionCount++;
                        int submissionId = Convert.ToInt32(reader["SubmissionId"]);
                        int challengeId = Convert.ToInt32(reader["ChallengeId"]);
                        string challengeName = reader["ChallengeName"].ToString();
                        string yearLevel = reader["YearLevel"].ToString();
                        decimal pointsEarned = Convert.ToDecimal(reader["PointsEarned"]);
                        decimal totalPoints = Convert.ToDecimal(reader["TotalPoints"]);
                        DateTime submissionDate = Convert.ToDateTime(reader["SubmissionDate"]);

                        totalEarned += pointsEarned;
                        submittedChallengeIds.Add(challengeId);

                        _logger.LogInformation($"Submission ID: {submissionId}, Challenge: {challengeName} (ID: {challengeId}, Year: {yearLevel}), Points: {pointsEarned}/{totalPoints}, Date: {submissionDate}");
                    }
                }
            }

            _logger.LogInformation($"Student {studentId} has {submissionCount} total challenge submissions after deletion");
            
            // Get deleted challenges for this student to exclude them from calculations
            HashSet<int> deletedChallengeIds = new HashSet<int>();
            
            string deletedChallengesQuery = @"
                SELECT ChallengeId 
                FROM DeletedChallenges 
                WHERE StudentId = @StudentId";
                
            using (var command = new SqlCommand(deletedChallengesQuery, connection))
            {
                command.Parameters.AddWithValue("@StudentId", resolvedStudentId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        deletedChallengeIds.Add(Convert.ToInt32(reader["ChallengeId"]));
                    }
                }
            }
            
            if (deletedChallengeIds.Count > 0)
            {
                _logger.LogInformation($"Found {deletedChallengeIds.Count} deleted challenges to exclude: {string.Join(", ", deletedChallengeIds)}");
            }
            
            // Get total available points from ALL challenges in valid year levels
            decimal totalAvailablePoints = 0;
            
            // Get all challenges from previous school years
            string totalPointsQuery = @"
                SELECT 
                    c.ChallengeId,
                    c.ChallengeName,
                    c.YearLevel,
                    SUM(q.Points) as ChallengePoints
                FROM Challenges c
                INNER JOIN ChallengeQuestions q ON c.ChallengeId = q.ChallengeId
                WHERE c.IsActive = 1";
                
            // Exclude deleted challenges from total points calculation
            if (deletedChallengeIds.Count > 0)
            {
                totalPointsQuery += $" AND c.ChallengeId NOT IN ({string.Join(",", deletedChallengeIds)})";
            }
            
            totalPointsQuery += " GROUP BY c.ChallengeId, c.ChallengeName, c.YearLevel";

            using (var command = new SqlCommand(totalPointsQuery, connection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        int challengeId = Convert.ToInt32(reader["ChallengeId"]);
                        string challengeName = reader["ChallengeName"].ToString();
                        string yearLevel = reader["YearLevel"].ToString();
                        decimal challengePoints = Convert.ToDecimal(reader["ChallengePoints"]);
                        
                        totalAvailablePoints += challengePoints;
                        
                        _logger.LogInformation($"Challenge {challengeId} ({challengeName}) in year {yearLevel} has {challengePoints} total points");
                    }
                }
            }
            
            _logger.LogInformation($"Total points earned: {totalEarned}, Total available points from non-deleted challenges: {totalAvailablePoints}");
            
            // Get the score weight for CompletedChallenges from the database
            decimal scoreWeight = 0;
            string weightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
            using (var command = new SqlCommand(weightQuery, connection))
            {
                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    scoreWeight = Convert.ToDecimal(result);
                }
            }

            _logger.LogInformation($"Retrieved ScoreWeight for CompletedChallenges from database: {scoreWeight:F2}");
            
            // Calculate the CompletedChallengesScore
            decimal completedChallengesScore;
            if (totalAvailablePoints > 0)
            {
                completedChallengesScore = (totalEarned / totalAvailablePoints) * 100 * (scoreWeight / 100);
                _logger.LogInformation($"Calculated CompletedChallengesScore = ({totalEarned} / {totalAvailablePoints}) * 100 * ({scoreWeight:F2} / 100) = {completedChallengesScore}");
            }
            else
            {
                _logger.LogInformation($"No challenge points possible for student {studentId}, setting CompletedChallengesScore to 0");
                completedChallengesScore = 0;
            }
            
            // Get other score components (attendance, grades, etc.)
            int firstYearGrade = 0, secondYearGrade = 0, thirdYearGrade = 0, fourthYearGrade = 0;
            int seminarsScore = 0, extraCurricularScore = 0, masteryScore = 0, academicGrades = 0;
            
            string getExistingScoreQuery = @"
                SELECT FirstYearGrade, SecondYearGrade, ThirdYearGrade, FourthYearGrade,
                       SeminarsWebinarsScore, ExtraCurricularScore, MasteryScore, AcademicGradesScore
                FROM StudentDetails
                WHERE IdNumber = @StudentId";
            
            using (var command = new SqlCommand(getExistingScoreQuery, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        firstYearGrade = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToInt32(reader["FirstYearGrade"]) : 0;
                        secondYearGrade = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToInt32(reader["SecondYearGrade"]) : 0;
                        thirdYearGrade = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToInt32(reader["ThirdYearGrade"]) : 0;
                        fourthYearGrade = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToInt32(reader["FourthYearGrade"]) : 0;
                        seminarsScore = reader["SeminarsWebinarsScore"] != DBNull.Value ? Convert.ToInt32(reader["SeminarsWebinarsScore"]) : 0;
                        extraCurricularScore = reader["ExtraCurricularScore"] != DBNull.Value ? Convert.ToInt32(reader["ExtraCurricularScore"]) : 0;
                        
                        // Check if MasteryScore column exists before trying to read it
                        if (HasColumn(reader, "MasteryScore"))
                        {
                            masteryScore = reader["MasteryScore"] != DBNull.Value ? Convert.ToInt32(reader["MasteryScore"]) : 0;
                        }
                        
                        // Get AcademicGradesScore directly from the database
                        if (HasColumn(reader, "AcademicGradesScore"))
                        {
                            academicGrades = reader["AcademicGradesScore"] != DBNull.Value ? Convert.ToInt32(reader["AcademicGradesScore"]) : 0;
                        }
                        else
                        {
                            // Fallback to computing it if the column doesn't exist
                            if (gradeLevel >= 1) academicGrades += firstYearGrade;
                            if (gradeLevel >= 2) academicGrades += secondYearGrade;
                            if (gradeLevel >= 3) academicGrades += thirdYearGrade;
                            if (gradeLevel >= 4) academicGrades += fourthYearGrade;
                        }
                    }
                }
            }
            
            _logger.LogInformation($"Student {studentId} grades: 1st={firstYearGrade}, 2nd={secondYearGrade}, 3rd={thirdYearGrade}, 4th={fourthYearGrade}, AcademicGrades={academicGrades}");
            _logger.LogInformation($"Student {studentId} other scores: Seminars={seminarsScore}, ExtraCurricular={extraCurricularScore}, Mastery={masteryScore}");
            
            // Calculate overall score
            int totalScore = 0;
            
            // Add all components using the correct formula:
            // AcademicGrades + completedChallengesScore + masteryScore + seminarsWebinarsScore + extraCurricularScore
            totalScore = academicGrades + (int)completedChallengesScore + masteryScore + seminarsScore + extraCurricularScore;
            
            // Apply any year level bonuses
            totalScore = ApplyYearLevelBonus(totalScore, gradeLevel);
            
            _logger.LogInformation($"Student {studentId} score breakdown: AcademicGrades={academicGrades}, Challenges={completedChallengesScore}, Mastery={masteryScore}, Seminars={seminarsScore}, ExtraCurricular={extraCurricularScore}, Total={totalScore}");
            
            // Determine badge based on score
            string badgeName = GetBadgeNameFromScore(totalScore);
            string badgeColor = GetBadgeColorFromName(badgeName);
            
            _logger.LogInformation($"Student {studentId} new badge: {badgeName} ({badgeColor})");
            
            // Update the student's score in database
            string updateScoreQuery = @"
                UPDATE StudentDetails
                SET CompletedChallengesScore = @ChallengesScore,
                    Score = @TotalScore
                WHERE IdNumber = @StudentId";
            
            using (var command = new SqlCommand(updateScoreQuery, connection))
            {
                command.Parameters.AddWithValue("@ChallengesScore", completedChallengesScore);
                command.Parameters.AddWithValue("@TotalScore", totalScore);
                command.Parameters.AddWithValue("@StudentId", studentId);
                
                int rowsAffected = await command.ExecuteNonQueryAsync();
                _logger.LogInformation($"Updated score for student {studentId}, rows affected: {rowsAffected}");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error recalculating scores for student {StudentId}", studentId);
    }
}
        [HttpPost("RecordExtraCurricular")]
        public async Task<IActionResult> RecordExtraCurricular(string TeacherId, string StudentId, string ActivityName, 
            string ActivityDescription, string ActivityCategory, DateTime ActivityDate, decimal Score, string Rank, IFormFile ProofImage)
        {
            try
            {
                _logger.LogInformation("RecordExtraCurricular called for StudentId: {StudentId}, TeacherId: {TeacherId}, Activity: {ActivityName}", 
                    StudentId, TeacherId, ActivityName);
                    
                if (string.IsNullOrEmpty(TeacherId) || string.IsNullOrEmpty(StudentId) || 
                    string.IsNullOrEmpty(ActivityName) || string.IsNullOrEmpty(ActivityCategory) || 
                    ActivityDate == DateTime.MinValue || ProofImage == null)
                {
                    TempData["Error"] = "All required fields must be provided.";
                    _logger.LogWarning("RecordExtraCurricular validation failed - missing required fields");
                    return RedirectToAction("Dashboard", "Teacher");
                }
                
                // Validate image file
                if (ProofImage.Length > 5 * 1024 * 1024) // 5MB limit
                {
                    TempData["Error"] = "Image file is too large. Maximum size is 5MB.";
                    return RedirectToAction("Dashboard", "Teacher");
                }
                
                string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
                string fileExtension = Path.GetExtension(ProofImage.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    TempData["Error"] = "Only image files (jpg, jpeg, png, gif) are allowed.";
                    return RedirectToAction("Dashboard", "Teacher");
                }
                
                // Read image file into byte array
                byte[] imageData;
                using (var memoryStream = new MemoryStream())
                {
                    await ProofImage.CopyToAsync(memoryStream);
                    imageData = memoryStream.ToArray();
                }
                
                bool activityRecordSaved = false;
                int activityId = 0;
                
                // Insert the extra-curricular activity record
                using (var connection = new SqlConnection(_connectionString))
                {
                    try
                    {
                        await connection.OpenAsync();
                        _logger.LogInformation("Database connection opened for extra-curricular activity record creation");
                        
                        // Get the student's actual UserId from the database
                        string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                        string studentUserId = null;
                        
                        using (var command = new SqlCommand(userIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", StudentId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                studentUserId = result.ToString();
                                _logger.LogInformation("Found UserId {UserId} for StudentId {StudentId}", studentUserId, StudentId);
                            }
                        }
                        
                        // If not found in StudentDetails, try Users table directly
                        if (string.IsNullOrEmpty(studentUserId))
                        {
                            string directUserIdQuery = "SELECT UserId FROM Users WHERE Username = @Username";
                            using (var command = new SqlCommand(directUserIdQuery, connection))
                            {
                                command.Parameters.AddWithValue("@Username", StudentId); // Assuming Username might match StudentId
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    studentUserId = result.ToString();
                                    _logger.LogInformation("Found UserId {UserId} for Username {Username}", studentUserId, StudentId);
                                }
                            }
                        }
                        
                        if (string.IsNullOrEmpty(studentUserId))
                        {
                            _logger.LogError("Could not find UserId for StudentId {StudentId}", StudentId);
                            TempData["Error"] = "Student record not found. Cannot record extra-curricular activity.";
                            return RedirectToAction("Dashboard", "Teacher");
                        }
                        
                        // Get the teacher's UserId
                        string teacherUserIdQuery = "SELECT UserId FROM Users WHERE Username = @Username";
                        string teacherUserId = TeacherId; // Default to the provided TeacherId
                        
                        using (var command = new SqlCommand(teacherUserIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Username", TeacherId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                teacherUserId = result.ToString();
                                _logger.LogInformation("Found UserId {UserId} for TeacherId {TeacherId}", teacherUserId, TeacherId);
                            }
                        }
                        
                        // First check if the ExtraCurricularActivities table exists
                        bool tableExists = false;
                        using (var command = new SqlCommand(
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ExtraCurricularActivities'", 
                            connection))
                        {
                            var result = await command.ExecuteScalarAsync();
                            tableExists = Convert.ToInt32(result) > 0;
                            _logger.LogInformation("ExtraCurricularActivities table exists: {TableExists}", tableExists);
                        }
                        
                        // Create the table if it doesn't exist
                        if (!tableExists)
                        {
                            _logger.LogInformation("Creating ExtraCurricularActivities table");
                            string createTableQuery = @"
                                CREATE TABLE ExtraCurricularActivities (
                                    ActivityId INT IDENTITY(1,1) PRIMARY KEY,
                                    StudentId NVARCHAR(50) NOT NULL,
                                    TeacherId NVARCHAR(50) NOT NULL,
                                    ActivityName NVARCHAR(200) NOT NULL,
                                    ActivityDescription NVARCHAR(MAX) NULL,
                                    ActivityCategory NVARCHAR(100) NULL,
                                    ActivityDate DATETIME NOT NULL,
                                    RecordedDate DATETIME NOT NULL DEFAULT GETDATE(),
                                    Score DECIMAL(5,2) NOT NULL DEFAULT 0,
                                    Rank INT NULL,
                                    ProofImageData VARBINARY(MAX) NULL,
                                    ProofImageContentType NVARCHAR(100) NULL,
                                    IsVerified BIT NOT NULL DEFAULT 0,
                                    CONSTRAINT FK_ExtraCurricularActivities_StudentId FOREIGN KEY (StudentId) REFERENCES Users(UserId),
                                    CONSTRAINT FK_ExtraCurricularActivities_TeacherId FOREIGN KEY (TeacherId) REFERENCES Users(UserId)
                                );
                                
                                CREATE INDEX IX_ExtraCurricularActivities_StudentId ON ExtraCurricularActivities(StudentId);
                                CREATE INDEX IX_ExtraCurricularActivities_TeacherId ON ExtraCurricularActivities(TeacherId);";
                            
                            using (var command = new SqlCommand(createTableQuery, connection))
                            {
                                await command.ExecuteNonQueryAsync();
                                _logger.LogInformation("ExtraCurricularActivities table created successfully");
                            }
                        }
                        
                        // Get student's grade level to apply year-level bonus
                        int gradeLevel = await GetStudentGradeLevel(connection, StudentId);
                        
                        // Use the original score directly without any capping based on year level
                        decimal finalScore = Score;
                        
                        // Log that no capping is applied
                        _logger.LogInformation("Using full score for extracurricular activity for StudentId={StudentId}: score={Score}, gradeLevel={GradeLevel} (no capping applied)", 
                            StudentId, finalScore, gradeLevel);
                        
                        // Insert the record with the final score
                        string insertQuery = @"
                            INSERT INTO ExtraCurricularActivities (
                                StudentId, TeacherId, ActivityName, ActivityDescription, ActivityCategory, ActivityDate, 
                                RecordedDate, Score, Rank, ProofImageData, ProofImageContentType, IsVerified
                            ) VALUES (
                                @StudentId, @TeacherId, @ActivityName, @ActivityDescription, @ActivityCategory, @ActivityDate, 
                                GETDATE(), @Score, @Rank, @ProofImageData, @ProofImageContentType, 1
                            );
                            SELECT SCOPE_IDENTITY();";
                        
                        using (var command = new SqlCommand(insertQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentUserId);
                            command.Parameters.AddWithValue("@TeacherId", teacherUserId);
                            command.Parameters.AddWithValue("@ActivityName", ActivityName);
                            command.Parameters.AddWithValue("@ActivityDescription", 
                                string.IsNullOrEmpty(ActivityDescription) ? DBNull.Value : (object)ActivityDescription);
                            command.Parameters.AddWithValue("@ActivityCategory", ActivityCategory);
                            command.Parameters.AddWithValue("@ActivityDate", ActivityDate);
                            command.Parameters.AddWithValue("@Score", finalScore);
                            command.Parameters.AddWithValue("@Rank", string.IsNullOrEmpty(Rank) ? DBNull.Value : (object)Rank);
                            command.Parameters.AddWithValue("@ProofImageData", imageData);
                            command.Parameters.AddWithValue("@ProofImageContentType", ProofImage.ContentType);
                            
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                activityId = Convert.ToInt32(result);
                                activityRecordSaved = true;
                                _logger.LogInformation("Extra-curricular activity record saved successfully with ID: {ActivityId}", activityId);
                                
                                // Verify record was actually inserted
                                string verifyQuery = "SELECT COUNT(*) FROM ExtraCurricularActivities WHERE ActivityId = @ActivityId";
                                using (var verifyCommand = new SqlCommand(verifyQuery, connection))
                                {
                                    verifyCommand.Parameters.AddWithValue("@ActivityId", activityId);
                                    var verifyResult = await verifyCommand.ExecuteScalarAsync();
                                    int recordCount = Convert.ToInt32(verifyResult);
                                    _logger.LogInformation("Verification: found {RecordCount} records with ID {ActivityId}", recordCount, activityId);
                                    
                                    if (recordCount > 0)
                                    {
                                        TempData["Success"] = "Extra-curricular activity record saved successfully.";
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Verification failed: Record with ID {ActivityId} not found", activityId);
                                        TempData["Error"] = "Failed to save extra-curricular activity record.";
                                        activityRecordSaved = false;
                                    }
                                }
                            }
                        }

                        // After successfully saving the activity, update the student's extracurricular score
                        if (activityRecordSaved)
                        {
                            try
                            {
                                // Instead of creating ScoreController directly, make an HTTP call to the API endpoint
                                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                                _logger.LogInformation($"Making API call to update extracurricular score for student {StudentId}");
                                
                                using (var httpClient = new HttpClient())
                                {
                                    string apiUrl = $"{baseUrl}/Score/UpdateExtracurricularScore?studentId={StudentId}";
                                    _logger.LogInformation($"API URL: {apiUrl}");
                                    
                                    try
                                    {
                                        var response = await httpClient.PostAsync(apiUrl, new StringContent(""));
                                        var content = await response.Content.ReadAsStringAsync();
                                        _logger.LogInformation($"API response: {content}");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, $"Error calling Score API: {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error updating extracurricular score for {StudentId}: {ex.Message}");
                                // Don't return error here, we still want to continue with the main activity saved flow
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Database error in RecordExtraCurricular");
                        TempData["Error"] = "Database error: " + ex.Message;
                    }
                }
                
                if (activityRecordSaved)
                {
                    _logger.LogInformation("RecordExtraCurricular completed successfully");
                    return RedirectToAction("Dashboard", "Teacher");
                }
                else
                {
                    TempData["Error"] = "Failed to save extra-curricular activity record.";
                    _logger.LogError("Extra-curricular activity record was not saved.");
                    return RedirectToAction("Dashboard", "Teacher");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording extra-curricular activity: {Message}", ex.Message);
                TempData["Error"] = $"Error recording extra-curricular activity: {ex.Message}";
                return RedirectToAction("Dashboard", "Teacher");
            }
        }

        [HttpGet("GetStudentExtraCurricularRecords")]
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

        [HttpPost("DeleteExtraCurricular")]
        public async Task<IActionResult> DeleteExtraCurricular(int id)
        {
            try
            {
                // First get the activity details to get the student ID
                string studentId = "";
                string studentUserId = "";
                bool activityExists = false;
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the activity details
                    string getSql = @"
                        SELECT ea.StudentId, sd.IdNumber
                        FROM ExtraCurricularActivities ea 
                        LEFT JOIN StudentDetails sd ON ea.StudentId = sd.UserId
                        WHERE ea.ActivityId = @Id";
                    
                    using (var command = new SqlCommand(getSql, connection))
                    {
                        command.Parameters.AddWithValue("@Id", id);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                activityExists = true;
                                studentUserId = reader["StudentId"]?.ToString() ?? "";
                                studentId = reader["IdNumber"]?.ToString() ?? "";
                                
                                // If IdNumber is not found, use StudentId as fallback
                                if (string.IsNullOrEmpty(studentId))
                                {
                                    studentId = studentUserId;
                                }
                            }
                        }
                    }
                    
                    if (!activityExists)
                    {
                        return Json(new { success = false, message = "Activity not found." });
                    }
                    
                    // Delete the activity
                    string deleteSql = @"
                        DELETE FROM ExtraCurricularActivities
                        WHERE ActivityId = @Id";
                    
                    using (var command = new SqlCommand(deleteSql, connection))
                    {
                        command.Parameters.AddWithValue("@Id", id);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            return Json(new { success = false, message = "Failed to delete activity." });
                        }
                        
                        _logger.LogInformation($"Deleted extra-curricular activity ID {id} for student {studentId}");
                    }
                    
                    // After deleting, recalculate the extracurricular score
                    if (!string.IsNullOrEmpty(studentId))
                    {
                        try
                        {
                            // Instead of creating ScoreController directly, make an HTTP call to the API endpoint
                            string baseUrl = $"{Request.Scheme}://{Request.Host}";
                            _logger.LogInformation($"Making API call to update extracurricular score for student {studentId}");
                            
                            using (var httpClient = new HttpClient())
                            {
                                string apiUrl = $"{baseUrl}/Score/UpdateExtracurricularScore?studentId={studentId}";
                                _logger.LogInformation($"API URL: {apiUrl}");
                                
                                try
                                {
                                    var response = await httpClient.PostAsync(apiUrl, new StringContent(""));
                                    var content = await response.Content.ReadAsStringAsync();
                                    _logger.LogInformation($"API response: {content}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error calling Score API: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error recalculating extracurricular score after deletion: {ex.Message}");
                            // Continue with response, error in score calculation shouldn't prevent deletion confirmation
                        }
                    }
                }
                
                return Json(new { success = true, message = "Activity deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting extra-curricular activity: {Message}", ex.Message);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("ViewExtraCurricularProofImage")]
        public async Task<IActionResult> ViewExtraCurricularProofImage(int activityId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get image data for the activity
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
                                string contentType = reader["ProofImageContentType"]?.ToString();
                                
                                // Ensure content type is valid
                                if (string.IsNullOrEmpty(contentType) || !IsValidMimeType(contentType))
                                {
                                    // Default to a safe MIME type based on common image formats
                                    contentType = "image/jpeg";
                                }
                                
                                // Return the image with a generic filename
                                return File(imageData, contentType, "proof-image.jpg");
                            }
                        }
                    }
                }
                
                // Return a placeholder image if no image was found
                return NotFound("Image not available");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving proof image for activity ID {ActivityId}", activityId);
                return NotFound("Error retrieving image");
            }
        }

        [HttpPost("RecordAttendance")]
        public async Task<IActionResult> RecordAttendance(string StudentId, string TeacherId, string EventName, DateTime EventDate, string EventDescription, IFormFile ProofImage)
        {
            try
            {
                _logger.LogInformation("RecordAttendance called for StudentId: {StudentId}, TeacherId: {TeacherId}, Event: {EventName}", 
                    StudentId, TeacherId, EventName);
                    
                if (string.IsNullOrEmpty(StudentId) || string.IsNullOrEmpty(TeacherId) || 
                    string.IsNullOrEmpty(EventName) || EventDate == DateTime.MinValue)
                {
                    TempData["Error"] = "All required fields must be provided.";
                    _logger.LogWarning("RecordAttendance validation failed - missing required fields");
                    return RedirectToAction("Dashboard", "Teacher");
                }
                
                // Validate image file if provided
                byte[] proofImageData = null;
                string proofImageContentType = null;
                if (ProofImage != null)
                {
                    if (ProofImage.Length > 5 * 1024 * 1024) // 5MB limit
                    {
                        TempData["Error"] = "Image file is too large. Maximum size is 5MB.";
                        return RedirectToAction("Dashboard", "Teacher");
                    }
                    
                    string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
                    string fileExtension = Path.GetExtension(ProofImage.FileName).ToLower();
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        TempData["Error"] = "Only image files (jpg, jpeg, png, gif) are allowed.";
                        return RedirectToAction("Dashboard", "Teacher");
                    }
                    
                    // Read image file into byte array
                    using (var memoryStream = new MemoryStream())
                    {
                        await ProofImage.CopyToAsync(memoryStream);
                        proofImageData = memoryStream.ToArray();
                        proofImageContentType = ProofImage.ContentType;
                    }
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Create the AttendanceRecords table if it doesn't exist
                    await EnsureAttendanceRecordsTableExists(connection);
                    
                    // Get the student's actual UserId from the database
                    string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                    string studentUserId = null;
                    
                    using (var command = new SqlCommand(userIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", StudentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            studentUserId = result.ToString();
                            _logger.LogInformation("Found UserId {UserId} for StudentId {StudentId} in StudentDetails", studentUserId, StudentId);
                        }
                    }
                    
                    // If not found in StudentDetails, try Users table
                    if (string.IsNullOrEmpty(studentUserId))
                    {
                        string userTableQuery = "SELECT UserId FROM Users WHERE Username = @Username";
                        using (var command = new SqlCommand(userTableQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Username", StudentId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                studentUserId = result.ToString();
                                _logger.LogInformation("Found UserId {UserId} for Username {Username} in Users", studentUserId, StudentId);
                            }
                        }
                    }
                    
                    // If still not found, check if StudentId itself is a UserId
                    if (string.IsNullOrEmpty(studentUserId))
                    {
                        string checkUserIdQuery = "SELECT COUNT(*) FROM Users WHERE UserId = @UserId";
                        using (var command = new SqlCommand(checkUserIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", StudentId);
                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            if (count > 0)
                            {
                                studentUserId = StudentId;
                                _logger.LogInformation("StudentId {StudentId} is itself a valid UserId", StudentId);
                            }
                        }
                    }
                    
                    // If still not found, fall back to the raw StudentId
                    if (string.IsNullOrEmpty(studentUserId))
                    {
                        studentUserId = StudentId;
                        _logger.LogWarning("Could not resolve UserId for StudentId {StudentId}, using StudentId as UserId", StudentId);
                    }
                    
                    // Get student's grade level to apply year-based bonus
                    int gradeLevel = await GetStudentGradeLevel(connection, StudentId);
                    
                    // Apply the year-level bonus to the default score
                    int baseScore = 100; // Default score for attendance (changed from 1 to 100)
                    int displayScore = ApplyYearLevelBonus(baseScore, gradeLevel);
                    
                    // Insert the attendance record
                    string insertQuery = @"
                        INSERT INTO AttendanceRecords (StudentId, TeacherId, EventName, EventDate, EventDescription, 
                                                     ProofImageData, ProofImageContentType, RecordedDate, Score, IsVerified)
                        VALUES (@StudentId, @TeacherId, @EventName, @EventDate, @EventDescription, 
                               @ProofImageData, @ProofImageContentType, @RecordedDate, @Score, @IsVerified);
                        SELECT SCOPE_IDENTITY();";
                        
                    using (var command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentUserId);
                        command.Parameters.AddWithValue("@TeacherId", TeacherId);
                        command.Parameters.AddWithValue("@EventName", EventName);
                        command.Parameters.AddWithValue("@EventDate", EventDate);
                        command.Parameters.AddWithValue("@EventDescription", EventDescription ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ProofImageData", proofImageData != null ? (object)proofImageData : DBNull.Value);
                        command.Parameters.AddWithValue("@ProofImageContentType", proofImageContentType ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@RecordedDate", DateTime.Now);
                        command.Parameters.AddWithValue("@Score", displayScore); // Use score of 100
                        command.Parameters.AddWithValue("@IsVerified", true); // Set as verified since teacher is adding it
                        
                        var newId = await command.ExecuteScalarAsync();
                        
                        TempData["Success"] = "Attendance recorded successfully.";
                        return RedirectToAction("Dashboard", "Teacher");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording attendance");
                TempData["Error"] = "Error recording attendance: " + ex.Message;
                return RedirectToAction("Dashboard", "Teacher");
            }
        }

        [HttpGet("ViewAttendanceProof")]
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
                                string contentType = reader["ProofImageContentType"]?.ToString();
                                
                                // Ensure content type is valid
                                if (string.IsNullOrEmpty(contentType) || !IsValidMimeType(contentType))
                                {
                                    // Default to a safe MIME type based on common image formats
                                    contentType = "image/jpeg";
                                }
                                
                                // Return the image with a generic filename
                                return File(imageData, contentType, "attendance-proof.jpg");
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

        [HttpPost("DeleteAttendance")]
        public async Task<IActionResult> DeleteAttendance(int id)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Deleting attendance record with ID: {AttendanceId}", id);
                    
                    // First, get the image path to delete the file if it exists
                    string imagePath = null;
                    string getImageQuery = "SELECT ProofImagePath FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";
                    using (var command = new SqlCommand(getImageQuery, connection))
                    {
                        command.Parameters.AddWithValue("@AttendanceId", id);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            imagePath = result.ToString();
                        }
                    }
                    
                    // Verify the record exists before deleting
                    bool recordExists = false;
                    string verifyQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";
                    using (var command = new SqlCommand(verifyQuery, connection))
                    {
                        command.Parameters.AddWithValue("@AttendanceId", id);
                        var result = await command.ExecuteScalarAsync();
                        recordExists = Convert.ToInt32(result) > 0;
                    }
                    
                    if (!recordExists)
                    {
                        _logger.LogWarning("Attempted to delete non-existent attendance record with ID: {AttendanceId}", id);
                        return Json(new { success = false, message = "Record not found." });
                    }
                    
                    // Delete the record
                    string query = "DELETE FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@AttendanceId", id);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Deleted attendance record with ID: {AttendanceId}, Rows affected: {RowsAffected}", id, rowsAffected);
                        
                        // Delete the image file if it exists
                        if (!string.IsNullOrEmpty(imagePath) && rowsAffected > 0)
                        {
                            string fullPath = Path.Combine(_hostingEnvironment.WebRootPath, imagePath.TrimStart('/'));
                            if (System.IO.File.Exists(fullPath))
                            {
                                try
                                {
                                    System.IO.File.Delete(fullPath);
                                    _logger.LogInformation("Deleted image file: {FilePath}", fullPath);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error deleting image file: {FilePath}", fullPath);
                                }
                            }
                        }
                        
                        return Json(new { success = true });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting attendance record with ID: {AttendanceId}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper methods
        private async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string query = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
        
        private async Task<bool> ColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            string query = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
        
        // Helper method to get a student's grade level
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
        
        // Helper method to apply year-level bonuses to student scores
        private int ApplyYearLevelBonus(int baseScore, int gradeLevel)
        {
            // Previously we applied a percentage multiplier based on year level
            // Now we return the full score for all grade levels
            // This removes the year-level capping completely
            
            // No adjustment - return the original score
            _logger.LogInformation($"ApplyYearLevelBonus called with gradeLevel={gradeLevel} - returning full score (no capping)");
            return baseScore;
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

        private string GetBadgeColorFromName(string badgeName)
        {
            return badgeName.ToLower() switch
            {
                "bronze" => "#CD7F32",
                "silver" => "#C0C0C0",
                "gold" => "#FFD700",
                "platinum" => "#E5E4E2",
                "diamond" => "#B9F2FF",
                _ => "#808080" // Default gray color
            };
        }

        private async Task EnsureAttendanceRecordsTableExists(SqlConnection connection)
        {
            // Check if AttendanceRecords table exists
            bool tableExists = await TableExists(connection, "AttendanceRecords");
            if (!tableExists)
            {
                _logger.LogInformation("Creating AttendanceRecords table");
                string createTableQuery = @"
                    CREATE TABLE AttendanceRecords (
                        AttendanceId INT IDENTITY(1,1) PRIMARY KEY,
                        StudentId NVARCHAR(100) NOT NULL,
                        TeacherId NVARCHAR(100) NOT NULL,
                        EventName NVARCHAR(200) NOT NULL,
                        EventDescription NVARCHAR(MAX) NULL,
                        EventDate DATETIME NOT NULL,
                        ProofImageData VARBINARY(MAX) NULL,
                        ProofImageContentType NVARCHAR(100) NULL,
                        RecordedDate DATETIME NOT NULL DEFAULT GETDATE(),
                        Score DECIMAL(18,2) NULL DEFAULT 100,
                        IsVerified BIT NOT NULL DEFAULT 1
                    );
                    
                    CREATE INDEX IX_AttendanceRecords_StudentId ON AttendanceRecords(StudentId);
                    CREATE INDEX IX_AttendanceRecords_TeacherId ON AttendanceRecords(TeacherId);";
                    
                using (var command = new SqlCommand(createTableQuery, connection))
                {
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("AttendanceRecords table created successfully");
                }
            }
            else
            {
                // Check if the ProofImageData column exists
                bool hasProofImageDataColumn = await ColumnExists(connection, "AttendanceRecords", "ProofImageData");
                if (!hasProofImageDataColumn)
                {
                    _logger.LogInformation("Adding ProofImageData column to AttendanceRecords table");
                    string addProofImageDataQuery = @"
                        ALTER TABLE AttendanceRecords 
                        ADD ProofImageData VARBINARY(MAX) NULL";
                    
                    using (var command = new SqlCommand(addProofImageDataQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("ProofImageData column added successfully");
                    }
                }
                
                // Check if the ProofImageContentType column exists
                bool hasProofImageContentTypeColumn = await ColumnExists(connection, "AttendanceRecords", "ProofImageContentType");
                if (!hasProofImageContentTypeColumn)
                {
                    _logger.LogInformation("Adding ProofImageContentType column to AttendanceRecords table");
                    string addProofImageContentTypeQuery = @"
                        ALTER TABLE AttendanceRecords 
                        ADD ProofImageContentType NVARCHAR(100) NULL";
                    
                    using (var command = new SqlCommand(addProofImageContentTypeQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("ProofImageContentType column added successfully");
                    }
                }
                
                // Check if the RecordedDate column exists
                bool hasRecordedDateColumn = await ColumnExists(connection, "AttendanceRecords", "RecordedDate");
                if (!hasRecordedDateColumn)
                {
                    _logger.LogInformation("Adding RecordedDate column to AttendanceRecords table");
                    string addRecordedDateQuery = @"
                        ALTER TABLE AttendanceRecords 
                        ADD RecordedDate DATETIME NOT NULL DEFAULT GETDATE()";
                    
                    using (var command = new SqlCommand(addRecordedDateQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("RecordedDate column added successfully");
                    }
                }
                
                // Check if the Score column exists
                bool hasScoreColumn = await ColumnExists(connection, "AttendanceRecords", "Score");
                if (!hasScoreColumn)
                {
                    _logger.LogInformation("Adding Score column to AttendanceRecords table");
                    string addScoreQuery = @"
                        ALTER TABLE AttendanceRecords 
                        ADD Score DECIMAL(18,2) NULL DEFAULT 100";
                    
                    using (var command = new SqlCommand(addScoreQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Score column added successfully");
                    }
                }
                else
                {
                    // If Score column exists but is the wrong type, fix it
                    string checkScoreTypeQuery = @"
                        SELECT DATA_TYPE 
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'AttendanceRecords' 
                        AND COLUMN_NAME = 'Score'";
                        
                    string dataType = "";
                    using (var command = new SqlCommand(checkScoreTypeQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        dataType = result?.ToString() ?? "";
                    }
                    
                    if (dataType.Equals("int", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Converting Score column from INT to DECIMAL(18,2)");
                        
                        // Create a transaction for this multi-step operation
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                // Create a temporary column
                                using (var command = new SqlCommand("ALTER TABLE AttendanceRecords ADD ScoreTemp DECIMAL(18,2) NULL", connection, transaction))
                                {
                                    await command.ExecuteNonQueryAsync();
                                }
                                
                                // Copy and convert the data
                                using (var command = new SqlCommand("UPDATE AttendanceRecords SET ScoreTemp = CONVERT(DECIMAL(18,2), Score)", connection, transaction))
                                {
                                    await command.ExecuteNonQueryAsync();
                                }
                                
                                // Update any old scores that were 1 to be 100
                                using (var command = new SqlCommand("UPDATE AttendanceRecords SET ScoreTemp = 100 WHERE Score = 1 OR Score IS NULL", connection, transaction))
                                {
                                    await command.ExecuteNonQueryAsync();
                                }
                                
                                // Drop the old column
                                using (var command = new SqlCommand("ALTER TABLE AttendanceRecords DROP COLUMN Score", connection, transaction))
                                {
                                    await command.ExecuteNonQueryAsync();
                                }
                                
                                // Rename the new column
                                using (var command = new SqlCommand("EXEC sp_rename 'AttendanceRecords.ScoreTemp', 'Score', 'COLUMN'", connection, transaction))
                                {
                                    await command.ExecuteNonQueryAsync();
                                }
                                
                                // Set the default constraint
                                using (var command = new SqlCommand("ALTER TABLE AttendanceRecords ADD CONSTRAINT DF_AttendanceRecords_Score DEFAULT 100 FOR Score", connection, transaction))
                                {
                                    await command.ExecuteNonQueryAsync();
                                }
                                
                                // Commit the transaction
                                transaction.Commit();
                                _logger.LogInformation("Successfully converted Score column to DECIMAL(18,2) with default 100");
                            }
                            catch (Exception ex)
                            {
                                // Rollback on error
                                transaction.Rollback();
                                _logger.LogError(ex, "Error converting Score column type: {Message}", ex.Message);
                            }
                        }
                    }
                }
                
                // Check if the IsVerified column exists
                bool hasIsVerifiedColumn = await ColumnExists(connection, "AttendanceRecords", "IsVerified");
                if (!hasIsVerifiedColumn)
                {
                    _logger.LogInformation("Adding IsVerified column to AttendanceRecords table");
                    string addIsVerifiedQuery = @"
                        ALTER TABLE AttendanceRecords 
                        ADD IsVerified BIT NOT NULL DEFAULT 1";
                    
                    using (var command = new SqlCommand(addIsVerifiedQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("IsVerified column added successfully");
                    }
                }
            }
        }

        // Helper method to check if a reader has a column
        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        // Endpoint to reset all attendance scores to 100
        [HttpGet("ResetAttendanceScores")]
        public async Task<IActionResult> ResetAttendanceScores()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First ensure the AttendanceRecords table exists with correct schema
                    await EnsureAttendanceRecordsTableExists(connection);
                    
                    _logger.LogInformation("ResetAttendanceScores called - updating all attendance scores to 100");
                    
                    // Count records before update
                    int totalRecords = 0;
                    using (var command = new SqlCommand("SELECT COUNT(*) FROM AttendanceRecords", connection))
                    {
                        totalRecords = Convert.ToInt32(await command.ExecuteScalarAsync());
                    }
                    
                    // Get distribution of scores before update for logging
                    var beforeDistribution = new List<dynamic>();
                    using (var command = new SqlCommand("SELECT Score, COUNT(*) AS Count FROM AttendanceRecords GROUP BY Score ORDER BY Score", connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                beforeDistribution.Add(new
                                {
                                    score = reader.IsDBNull(0) ? "NULL" : reader["Score"].ToString(),
                                    count = reader.GetInt32(1)
                                });
                            }
                        }
                    }
                    
                    // Update all scores to 100 in a transaction
                    int updatedRecords = 0;
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string updateQuery = "UPDATE AttendanceRecords SET Score = 100";
                            using (var command = new SqlCommand(updateQuery, connection, transaction))
                            {
                                updatedRecords = await command.ExecuteNonQueryAsync();
                            }
                            
                            transaction.Commit();
                            _logger.LogInformation($"Successfully updated {updatedRecords} attendance records with a score of 100");
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            _logger.LogError(ex, "Error updating attendance scores: {Message}", ex.Message);
                            throw;
                        }
                    }
                    
                    // Get list of unique student IDs affected by the update
                    var affectedStudents = new List<string>();
                    using (var command = new SqlCommand("SELECT DISTINCT StudentId FROM AttendanceRecords", connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                affectedStudents.Add(reader.GetString(0));
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {affectedStudents.Count} unique students affected by the attendance score update");
                    
                    // Update seminar scores for all affected students
                    int studentsUpdated = 0;
                    foreach (var studentId in affectedStudents)
                    {
                        try
                        {
                            await UpdateSeminarsWebinarsScoreInternal(connection, studentId);
                            studentsUpdated++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating seminar score for student {studentId}: {ex.Message}");
                        }
                    }
                    
                    // Return success response with details
                    return Json(new 
                    {
                        success = true,
                        message = $"Successfully reset {updatedRecords} attendance scores to 100 and updated {studentsUpdated} student seminar scores",
                        details = new
                        {
                            totalRecords,
                            updatedRecords,
                            affectedStudentCount = affectedStudents.Count,
                            studentsWithUpdatedSeminarScores = studentsUpdated,
                            beforeDistribution
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting attendance scores: {Message}", ex.Message);
                return Json(new { success = false, message = $"Error resetting attendance scores: {ex.Message}" });
            }
        }
        
        // HTTP endpoint to update seminar/webinar scores for a student
        [HttpGet("UpdateSeminarsWebinarsScore")]
        public async Task<IActionResult> UpdateSeminarsWebinarsScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    return Json(new { success = false, message = "Student ID is required" });
                }
                
                _logger.LogInformation($"UpdateSeminarsWebinarsScore called for student: {studentId}");
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    var result = await UpdateSeminarsWebinarsScoreInternal(connection, studentId);
                    
                    return Json(new
                    {
                        success = true,
                        message = $"Successfully updated seminars/webinars score for student {studentId}",
                        details = result
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating seminars/webinars score for student {studentId}: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
        
        // Internal method to update seminars/webinars score
        private async Task<object> UpdateSeminarsWebinarsScoreInternal(SqlConnection connection, string studentId)
        {
            _logger.LogInformation($"Updating seminars/webinars score for student: {studentId}");
            
            // First ensure the SeminarsWebinarsScore column exists
            await EnsureSeminarsWebinarsScoreColumnExists(connection);
            
            // Get the student's actual UserId by trying different lookup methods
            string studentUserId = await ResolveStudentUserId(connection, studentId);
            
            if (string.IsNullOrEmpty(studentUserId))
            {
                throw new Exception($"Could not resolve UserId for student: {studentId}");
            }
            
            // Sum up all attendance record scores for this student
            decimal totalAttendancePoints = 0;
            int recordCount = 0;
            
            string scoreQuery = @"
                SELECT SUM(Score) AS TotalScore, COUNT(*) AS RecordCount
                FROM AttendanceRecords
                WHERE StudentId = @StudentId AND IsVerified = 1";
                
            using (var command = new SqlCommand(scoreQuery, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentUserId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        totalAttendancePoints = !reader.IsDBNull(0) ? reader.GetDecimal(0) : 0;
                        recordCount = reader.GetInt32(1);
                    }
                }
            }
            
            // Calculate seminar score: every 100 attendance points = 1 seminar point, max 10
            decimal rawSeminarScore = Math.Floor(totalAttendancePoints / 100);
            decimal seminarScore = Math.Min(rawSeminarScore, 10); // Cap at 10 points
            
            _logger.LogInformation($"Student {studentId}: Total attendance points={totalAttendancePoints}, " +
                                  $"Records={recordCount}, Calculated seminar score={seminarScore}");
            
            // Update the StudentDetails table
            int rowsAffected = 0;
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    string updateQuery = @"
                        UPDATE StudentDetails 
                        SET SeminarsWebinarsScore = @SeminarScore
                        WHERE UserId = @StudentId";
                        
                    using (var command = new SqlCommand(updateQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentUserId);
                        command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                        
                        rowsAffected = await command.ExecuteNonQueryAsync();
                    }
                    
                    // If no rows affected, try using IdNumber instead of UserId
                    if (rowsAffected == 0)
                    {
                        string updateByIdNumberQuery = @"
                            UPDATE StudentDetails 
                            SET SeminarsWebinarsScore = @SeminarScore
                            WHERE IdNumber = @StudentId";
                            
                        using (var command = new SqlCommand(updateByIdNumberQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                            
                            rowsAffected = await command.ExecuteNonQueryAsync();
                        }
                    }
                    
                    transaction.Commit();
                    _logger.LogInformation($"Updated SeminarsWebinarsScore for student {studentId}: {seminarScore} (affected rows: {rowsAffected})");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, $"Error updating SeminarsWebinarsScore for student {studentId}: {ex.Message}");
                    throw;
                }
            }
            
            return new
            {
                studentId,
                studentUserId,
                totalAttendancePoints,
                attendanceRecords = recordCount,
                calculatedSeminarScore = rawSeminarScore,
                cappedSeminarScore = seminarScore,
                rowsUpdated = rowsAffected
            };
        }
        
        // Helper method to ensure SeminarsWebinarsScore column exists
        private async Task EnsureSeminarsWebinarsScoreColumnExists(SqlConnection connection)
        {
            bool hasColumn = await ColumnExists(connection, "StudentDetails", "SeminarsWebinarsScore");
            
            if (!hasColumn)
            {
                _logger.LogInformation("Adding SeminarsWebinarsScore column to StudentDetails table");
                
                string addColumnQuery = @"
                    ALTER TABLE StudentDetails 
                    ADD SeminarsWebinarsScore DECIMAL(5,2) NULL DEFAULT 0";
                    
                using (var command = new SqlCommand(addColumnQuery, connection))
                {
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("SeminarsWebinarsScore column added successfully");
                }
            }
        }
        
        // Helper method to resolve a student ID to a User ID through various means
        private async Task<string> ResolveStudentUserId(SqlConnection connection, string studentId)
        {
            _logger.LogInformation($"Resolving UserId for student identifier: {studentId}");
            
            // First try: Look in StudentDetails by IdNumber
            string userIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
            using (var command = new SqlCommand(userIdQuery, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    _logger.LogInformation($"Found UserId by IdNumber: {result}");
                    return result.ToString();
                }
            }
            
            // Second try: Check Users table by Username
            string directUserIdQuery = "SELECT UserId FROM Users WHERE Username = @Username";
            using (var command = new SqlCommand(directUserIdQuery, connection))
            {
                command.Parameters.AddWithValue("@Username", studentId);
                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    _logger.LogInformation($"Found UserId by Username: {result}");
                    return result.ToString();
                }
            }
            
            // Third try: Check if StudentId itself is a UserId in Users
            string validateUserIdQuery = "SELECT COUNT(*) FROM Users WHERE UserId = @UserId";
            using (var command = new SqlCommand(validateUserIdQuery, connection))
            {
                command.Parameters.AddWithValue("@UserId", studentId);
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (count > 0)
                {
                    _logger.LogInformation($"Confirmed that the supplied StudentId is itself a valid UserId: {studentId}");
                    return studentId;
                }
            }
            
            // Default fallback: use the studentId directly as a last resort
            _logger.LogWarning($"Could not resolve UserId for StudentId {studentId}, using StudentId as UserId");
            return studentId;
        }

        // Helper method to check if a MIME type is valid
        private bool IsValidMimeType(string mimeType)
        {
            // List of common image MIME types
            string[] validImageTypes = new string[] {
                "image/jpeg", "image/jpg", "image/png", "image/gif", 
                "image/bmp", "image/tiff", "image/webp"
            };
            
            return !string.IsNullOrEmpty(mimeType) && validImageTypes.Contains(mimeType.ToLower());
        }

        [HttpPost]
        [Route("UpdateTransfereeStatus")]
        public async Task<IActionResult> UpdateTransfereeStatus(string studentId, string previousSchool, List<string> accessibleYears)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }

            try
            {
                _logger.LogInformation($"UpdateTransfereeStatus called for student: {studentId}");
                
                if (accessibleYears != null && accessibleYears.Any())
                {
                    _logger.LogInformation($"Selected years: {string.Join(", ", accessibleYears)}");
                }
                else
                {
                    _logger.LogInformation("No years selected");
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First, verify this student is actually a transferee in the database
                    string checkQuery = "SELECT IsTransferee FROM StudentDetails WHERE IdNumber = @StudentId";
                    bool isTransferee = false;
                    
                    using (var command = new SqlCommand(checkQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            isTransferee = Convert.ToBoolean(result);
                        }
                    }
                    
                    if (!isTransferee)
                    {
                        return Json(new { success = false, message = "This student is not marked as a transferee in the database." });
                    }
                    
                    // Ensure the TransfereeStudentAccess table exists
                    await EnsureTransfereeAccessTableExists(connection);
                    
                    // Update just the previous school in StudentDetails
                    string updateQuery = @"
                        UPDATE StudentDetails 
                        SET PreviousSchool = @PreviousSchool
                        WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        command.Parameters.AddWithValue("@PreviousSchool", previousSchool ?? (object)DBNull.Value);
                        
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    // First delete existing access entries for year levels
                    string deleteYearQuery = "DELETE FROM TransfereeStudentAccess WHERE StudentId = @StudentId";
                    using (var command = new SqlCommand(deleteYearQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rows = await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Deleted {rows} existing year access entries");
                    }
                    
                    // Then insert new access entries for years if any were selected
                    if (accessibleYears != null && accessibleYears.Any())
                    {
                        string insertQuery = @"
                            INSERT INTO TransfereeStudentAccess (StudentId, SchoolYear, CreatedDate)
                            VALUES (@StudentId, @SchoolYear, GETDATE())";
                        
                        foreach (var year in accessibleYears)
                        {
                            using (var command = new SqlCommand(insertQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                command.Parameters.AddWithValue("@SchoolYear", year);
                                
                                try
                                {
                                    await command.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Added access for year: {year}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"Error adding year {year}: {ex.Message}");
                                }
                            }
                        }
                    }
                    
                    return Json(new { 
                        success = true, 
                        message = "Transferee access settings updated successfully." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in UpdateTransfereeStatus: {ex.Message}");
                return Json(new { success = false, message = $"Error updating transferee settings: {ex.Message}" });
            }
        }

        [HttpGet]
        [Route("GetTransfereeAccessYears")]
        public async Task<IActionResult> GetTransfereeAccessYears(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }

            try
            {
                List<string> accessYears = new List<string>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if table exists, if not, no years to return
                    bool tableExists = await TableExists(connection, "TransfereeStudentAccess");
                    if (!tableExists)
                    {
                        return Json(new { success = true, accessYears });
                    }
                    
                    // Check which column exists
                    bool hasYearLevelColumn = await ColumnExists(connection, "TransfereeStudentAccess", "YearLevel");
                    bool hasSchoolYearColumn = await ColumnExists(connection, "TransfereeStudentAccess", "SchoolYear");
                    
                    _logger.LogInformation($"GetTransfereeAccessYears - Table exists. HasYearLevelColumn: {hasYearLevelColumn}, HasSchoolYearColumn: {hasSchoolYearColumn}");
                    
                    string columnName = hasSchoolYearColumn ? "SchoolYear" : "YearLevel";
                    
                    // Get all school years the transferee has access to
                    string query = $@"
                        SELECT {columnName} 
                        FROM TransfereeStudentAccess 
                        WHERE StudentId = @StudentId";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string yearValue = reader.GetString(0);
                                accessYears.Add(yearValue);
                                _logger.LogInformation($"Found access year: {yearValue}");
                            }
                        }
                    }
                }
                
                return Json(new { success = true, accessYears });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in GetTransfereeAccessYears: {ex.Message}");
                return Json(new { success = false, message = $"Error retrieving transferee access years: {ex.Message}" });
            }
        }
        
        private async Task EnsureTransfereeAccessTableExists(SqlConnection connection)
        {
            // Check if table exists
            bool tableExists = await TableExists(connection, "TransfereeStudentAccess");
            
            if (!tableExists)
            {
                // Create the table if it doesn't exist
                string createTableQuery = @"
                    CREATE TABLE TransfereeStudentAccess (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        StudentId NVARCHAR(50) NOT NULL,
                        SchoolYear NVARCHAR(20) NOT NULL,
                        CreatedDate DATETIME NOT NULL,
                        CONSTRAINT UQ_StudentYearAccess UNIQUE (StudentId, SchoolYear)
                    )";
                
                using (var command = new SqlCommand(createTableQuery, connection))
                {
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Created TransfereeStudentAccess table");
                }
            }
            else
            {
                // Check if there's an inconsistency with the column name
                bool hasYearLevelColumn = await ColumnExists(connection, "TransfereeStudentAccess", "YearLevel");
                bool hasSchoolYearColumn = await ColumnExists(connection, "TransfereeStudentAccess", "SchoolYear");
                
                _logger.LogInformation($"TransfereeStudentAccess table exists. HasYearLevelColumn: {hasYearLevelColumn}, HasSchoolYearColumn: {hasSchoolYearColumn}");
            }
        }
        
        private async Task EnsureTransfereeChallengeAccessTableExists(SqlConnection connection)
        {
            // Check if table exists
            bool tableExists = await TableExists(connection, "TransfereeChallengeAccess");
            
            if (!tableExists)
            {
                // Create the table if it doesn't exist
                string createTableQuery = @"
                    CREATE TABLE TransfereeChallengeAccess (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        StudentId NVARCHAR(50) NOT NULL,
                        ChallengeId INT NOT NULL,
                        CreatedDate DATETIME NOT NULL,
                        CONSTRAINT UQ_StudentChallengeAccess UNIQUE (StudentId, ChallengeId)
                    )";
                
                using (var command = new SqlCommand(createTableQuery, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        
        [HttpGet]
        [Route("GetAvailableSchoolYears")]
        public async Task<IActionResult> GetAvailableSchoolYears()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Query to get all distinct school years from challenges
                    string query = "SELECT DISTINCT YearLevel FROM Challenges ORDER BY YearLevel";
                    
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            List<string> years = new List<string>();
                            
                            while (await reader.ReadAsync())
                            {
                                years.Add(reader["YearLevel"].ToString());
                            }
                            
                            return Json(new { success = true, years = years });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetChallengesByYearLevel")]
        public async Task<IActionResult> GetChallengesByYearLevel()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First get all distinct year levels - changed from YearLevel to match actual column case
                    List<string> yearLevels = new List<string>();
                    string yearQuery = "SELECT DISTINCT YearLevel FROM Challenges ORDER BY YearLevel";
                    
                    using (SqlCommand command = new SqlCommand(yearQuery, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                yearLevels.Add(reader["YearLevel"].ToString());
                            }
                        }
                    }
                    
                    // Now get all challenges for each year level
                    var yearChallenges = new List<object>();
                    
                    foreach (var yearLevel in yearLevels)
                    {
                        string challengesQuery = @"
                            SELECT ChallengeId, ChallengeName, ProgrammingLanguage, 
                                   Description, CreatedDate, ExpirationDate, IsActive
                            FROM Challenges 
                            WHERE YearLevel = @YearLevel
                            ORDER BY ChallengeName";
                        
                        var challenges = new List<object>();
                        
                        using (SqlCommand command = new SqlCommand(challengesQuery, connection))
                        {
                            command.Parameters.AddWithValue("@YearLevel", yearLevel);
                            
                            using (SqlDataReader reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    challenges.Add(new
                                    {
                                        id = reader["ChallengeId"],
                                        name = reader["ChallengeName"].ToString(),
                                        language = reader["ProgrammingLanguage"].ToString(),
                                        description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null,
                                        createdDate = Convert.ToDateTime(reader["CreatedDate"]),
                                        expirationDate = reader["ExpirationDate"] != DBNull.Value ? 
                                            Convert.ToDateTime(reader["ExpirationDate"]) : (DateTime?)null,
                                        isActive = Convert.ToBoolean(reader["IsActive"])
                                    });
                                }
                            }
                        }
                        
                        yearChallenges.Add(new
                        {
                            yearLevel,
                            challenges
                        });
                    }
                    
                    return Json(new { success = true, yearChallenges });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get valid year levels for a given grade level
        private async Task<List<string>> GetValidYearLevelsForGradeLevel(SqlConnection connection, int gradeLevel)
        {
            List<string> validYearLevels = new List<string>();
            
            try
            {
                // For a student in grade level N, include year levels 1 to N-1
                string query = "SELECT DISTINCT YearLevel FROM Challenges ORDER BY YearLevel";
                
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        List<string> allYearLevels = new List<string>();
                        
                        while (await reader.ReadAsync())
                        {
                            string yearLevel = reader["YearLevel"].ToString();
                            if (!string.IsNullOrEmpty(yearLevel))
                            {
                                allYearLevels.Add(yearLevel);
                            }
                        }
                        
                        _logger.LogInformation($"All available year levels in database: {string.Join(", ", allYearLevels)}");
                        
                        // Sort the year levels - this should work for both numeric and academic year formats
                        allYearLevels.Sort();
                        
                        // For grade level N, include up to the first N-1 year levels
                        // This ensures we get ALL previous school years combined, not just the current
                        int count = Math.Min(gradeLevel - 1, allYearLevels.Count);
                        validYearLevels = allYearLevels.Take(count).ToList();
                        
                        // If we didn't get any valid years but we should have (grade > 1), log a warning
                        if (validYearLevels.Count == 0 && gradeLevel > 1)
                        {
                            _logger.LogWarning($"No valid year levels found for grade {gradeLevel} despite grade > 1. Check data consistency.");
                        }
                        
                        _logger.LogInformation($"For grade level {gradeLevel}, found valid previous school years: {string.Join(", ", validYearLevels)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting valid year levels for grade level {GradeLevel}", gradeLevel);
            }
            
            return validYearLevels;
        }
    }
} 
