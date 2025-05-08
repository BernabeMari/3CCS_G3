using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StudentBadge.Models;
using StudentBadge.Services;
using System.IO;
using System.Linq;

[Route("Dashboard")]
public class StudentDashboardController : Controller
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StudentDashboardController> _logger;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly EmailService _emailService;
    private readonly DatabaseUtilityService _dbUtilityService; // Create this service for DB utilities

    public StudentDashboardController(IConfiguration configuration, 
        ILogger<StudentDashboardController> logger, 
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

  [HttpGet("StudentDashboard")]
  public async Task<IActionResult> StudentDashboard()
    {
        // Get current student information from session
        ViewBag.FullName = HttpContext.Session.GetString("FullName");
        var studentId = HttpContext.Session.GetString("IdNumber");
        ViewBag.IdNumber = studentId;
        ViewBag.Course = HttpContext.Session.GetString("Course");
        ViewBag.Section = HttpContext.Session.GetString("Section");

        // Get the current student's score and profile picture
        await GetCurrentStudentScoreAndPicture();
        
        // Get the student's year level
        if (!string.IsNullOrEmpty(studentId))
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if GradeLevel column exists in StudentDetails
                bool hasGradeLevelColumn = false;
                string checkColumnQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'GradeLevel'";
                
                using (var command = new SqlCommand(checkColumnQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    hasGradeLevelColumn = (count > 0);
                }
                
                if (hasGradeLevelColumn)
                {
                    // Get the year level from StudentDetails table
                    string query = "SELECT GradeLevel FROM StudentDetails WHERE IdNumber = @IdNumber";
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@IdNumber", studentId);
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            ViewBag.StudentYearLevel = Convert.ToInt32(result);
                        }
                        else
                        {
                            ViewBag.StudentYearLevel = 0; // Default to unknown
                        }
                    }
                }
                else
                {
                    ViewBag.StudentYearLevel = 0; // Default to unknown if column doesn't exist
                }
            }
        }
        else
        {
            ViewBag.StudentYearLevel = 0; // Default to unknown if no student ID
        }

        // We no longer need to load all students
        // var allStudents = await GetAllStudentsWithDetails();
        // ViewBag.AllStudents = allStudents;

        return View("~/Views/Dashboard/StudentDashboard.cshtml");
    }

    private async Task GetCurrentStudentScoreAndPicture()
    {
        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
            return;

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            
            // Initialize default values
            ViewBag.Score = 0;
            ViewBag.ProfilePicturePath = null;
            ViewBag.ResumePath = null;
            ViewBag.IsProfileVisible = true;
            ViewBag.IsResumeVisible = true;
            
            // Check if binary columns exist
            bool binaryColumnsExist = false;
            string checkBinaryColumnsQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ProfilePictureData'";
            
            using (var command = new SqlCommand(checkBinaryColumnsQuery, connection))
            {
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                binaryColumnsExist = (count > 0);
            }
            
            // Check if StudentDetails table exists
            bool newTableExists = false;
            string checkTableQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = 'StudentDetails'";
            
            using (var command = new SqlCommand(checkTableQuery, connection))
            {
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                newTableExists = (count > 0);
            }
            
            if (newTableExists)
            {
                // Use new table structure
                string query = @"
                    SELECT sd.Score, sd.ProfilePicturePath, sd.ResumeFileName, sd.IsProfileVisible, sd.IsResumeVisible,
                           CASE WHEN sd.ProfilePictureData IS NOT NULL THEN 1 ELSE 0 END AS HasBinaryProfilePicture
                    FROM StudentDetails sd
                    WHERE sd.IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", idNumber);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Get score
                            if (reader["Score"] != DBNull.Value)
                            {
                                ViewBag.Score = Convert.ToInt32(reader["Score"]);
                            }

                            // Check if we have binary profile picture data
                            bool hasBinaryProfilePicture = Convert.ToBoolean(reader["HasBinaryProfilePicture"]);
                            
                            if (hasBinaryProfilePicture)
                            {
                                // Use the GetProfilePicture action URL with a cache-busting timestamp
                                ViewBag.ProfilePicturePath = Url.Action("GetProfilePicture", "FileHandler", 
                                    new { studentId = idNumber, t = DateTime.Now.Ticks });
                            }
                            else
                            {
                                // Use the old path-based approach
                                string profilePicturePath = reader["ProfilePicturePath"] as string;
                                ViewBag.ProfilePicturePath = !string.IsNullOrEmpty(profilePicturePath) ? profilePicturePath : null;
                            }
                            
                            // Get resume file path
                            string resumeFileName = reader["ResumeFileName"] as string;
                            ViewBag.ResumePath = !string.IsNullOrEmpty(resumeFileName) ? 
                                Url.Action("GetResume", "FileHandler", new { studentId = idNumber, t = DateTime.Now.Ticks }) : null;
                            
                            // Get visibility settings
                            ViewBag.IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                            ViewBag.IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]);
                        }
                    }
                }
            }
            else
            {
                // Check if Students table exists (old structure)
                bool oldTableExists = false;
                string checkOldTableQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'Students'";
                
                using (var command = new SqlCommand(checkOldTableQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    oldTableExists = (count > 0);
                }
                
                if (oldTableExists)
                {
                    // Use old table structure
                    string query = @"
                        SELECT Score, ProfilePicturePath, ResumeFileName, IsProfileVisible, IsResumeVisible 
                        FROM Students 
                        WHERE IdNumber = @IdNumber";
    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
    
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Get score
                                if (reader["Score"] != DBNull.Value)
                                {
                                    ViewBag.Score = Convert.ToInt32(reader["Score"]);
                                }
    
                                // Get profile picture path
                                string profilePicturePath = reader["ProfilePicturePath"] as string;
                                ViewBag.ProfilePicturePath = !string.IsNullOrEmpty(profilePicturePath) ? profilePicturePath : null;
                                
                                // Get resume file path
                                string resumeFileName = reader["ResumeFileName"] as string;
                                ViewBag.ResumePath = !string.IsNullOrEmpty(resumeFileName) ? 
                                    Url.Action("GetResume", "FileHandler", new { studentId = idNumber }) : null;
                                
                                // Get visibility settings
                                ViewBag.IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                                ViewBag.IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]);
                            }
                        }
                    }
                }
            }
        }
    }
   [HttpPost]
    public IActionResult UpdatePrivacySetting([FromBody] PrivacySettingModel model)
    {
        if (model == null)
        {
            return BadRequest(new { message = "Invalid request data" });
        }

        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "UPDATE StudentDetails SET IsProfileVisible = @IsVisible WHERE IdNumber = @IdNumber";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IsVisible", model.IsVisible);
                command.Parameters.AddWithValue("@IdNumber", idNumber);
                command.ExecuteNonQuery();
            }
        }

        return Ok(new { message = "Privacy setting updated successfully." });
    }

    [HttpPost]
    [Route("UpdateProfileVisibility")]
    public IActionResult UpdateProfileVisibility([FromBody] ProfileVisibilityModel model)
    {
        if (model == null)
        {
            return BadRequest(new { success = false, message = "Invalid request data" });
        }

        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return Unauthorized(new { success = false, message = "User not authenticated" });
        }

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "UPDATE StudentDetails SET IsProfileVisible = @IsVisible WHERE IdNumber = @IdNumber";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IsVisible", model.IsVisible);
                command.Parameters.AddWithValue("@IdNumber", idNumber);
                int rowsAffected = command.ExecuteNonQuery();
                
                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Profile visibility updated successfully." });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to update profile visibility." });
                }
            }
        }
    }

    [HttpPost]
    [Route("UpdateResumeVisibility")]
    public IActionResult UpdateResumeVisibility([FromBody] ResumeVisibilityModel model)
    {
        if (model == null)
        {
            return BadRequest(new { message = "Invalid request data" });
        }

        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "UPDATE StudentDetails SET IsResumeVisible = @IsVisible WHERE IdNumber = @IdNumber";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IsVisible", model.IsVisible);
                command.Parameters.AddWithValue("@IdNumber", idNumber);
                int rowsAffected = command.ExecuteNonQuery();
                
                if (rowsAffected > 0)
                {
                    return Json(new { success = true, message = "Resume visibility updated successfully." });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to update resume visibility." });
                }
            }
        }
    }


    [HttpGet("StudentProfile")]
    public IActionResult StudentProfile()
    {
        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return RedirectToAction("Login");
        }

        bool isProfileVisible = false;
        bool isResumeVisible = false;
        bool hasProfilePicture = false;
        bool hasResume = false;
        string profilePicturePath = null;
        string resumeFileName = null;
        int score = 0;
        string achievements = "";
        string comments = "";
        string badgeColor = "";
        int yearLevel = 0; // Default year level

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            
            // Check if the StudentDetails table exists
            bool tableExists = false;
            string checkTableQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = 'StudentDetails'";
            
            using (var command = new SqlCommand(checkTableQuery, connection))
            {
                int count = Convert.ToInt32(command.ExecuteScalar());
                tableExists = (count > 0);
            }
            
            if (tableExists)
            {
                // Check if GradeLevel column exists
                bool hasGradeLevelColumn = false;
                string checkColumnQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'GradeLevel'";
                    
                using (var command = new SqlCommand(checkColumnQuery, connection))
                {
                    int count = Convert.ToInt32(command.ExecuteScalar());
                    hasGradeLevelColumn = (count > 0);
                }
                
                // Query base fields plus GradeLevel if it exists
                string gradeLevelSelect = hasGradeLevelColumn ? ", sd.GradeLevel" : "";
                string query = $@"
                    SELECT 
                        sd.IsProfileVisible, 
                        sd.IsResumeVisible, 
                        sd.ProfilePicturePath,
                        sd.ResumeFileName,
                        sd.Score, 
                        sd.Achievements, 
                        sd.Comments, 
                        sd.BadgeColor{gradeLevelSelect}
                    FROM StudentDetails sd
                    WHERE sd.IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", idNumber);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            isProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                            isResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]);
                            score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0;
                            achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "";
                            comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "";
                            badgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None";
                            
                            // Get profile picture path directly
                            profilePicturePath = reader["ProfilePicturePath"] as string;
                            hasProfilePicture = !string.IsNullOrEmpty(profilePicturePath);
                            
                            // Get resume filename directly
                            resumeFileName = reader["ResumeFileName"] as string;
                            hasResume = !string.IsNullOrEmpty(resumeFileName);
                            
                            // Get year level if column exists
                            if (hasGradeLevelColumn && reader["GradeLevel"] != DBNull.Value)
                            {
                                yearLevel = Convert.ToInt32(reader["GradeLevel"]);
                            }
                        }
                    }
                }
            }
        }

        // Set ViewBag values
        ViewBag.IsProfileVisible = isProfileVisible;
        ViewBag.IsResumeVisible = isResumeVisible;
        ViewBag.ProfilePicturePath = profilePicturePath;
        ViewBag.HasProfilePicture = hasProfilePicture;
        ViewBag.HasResume = hasResume;
        ViewBag.Score = score;
        ViewBag.Achievements = achievements;
        ViewBag.Comments = comments;
        ViewBag.BadgeColor = badgeColor;
        ViewBag.StudentYearLevel = yearLevel; // Set year level in ViewBag

        // Add the basic student info
        string fullName = HttpContext.Session.GetString("FullName");
        string course = HttpContext.Session.GetString("Course");
        string section = HttpContext.Session.GetString("Section");

        ViewBag.FullName = fullName;
        ViewBag.IdNumber = idNumber;
        ViewBag.Course = course;
        ViewBag.Section = section;
        
        // Get the chat availability settings
        bool isChatAvailable = false;
        string chatStartTime = "09:00";
        string chatEndTime = "17:00";
        
        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            
            // Check if the ChatAvailability column exists in StudentDetails
            bool hasAvailabilityColumn = false;
            string checkColumnQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'IsChatAvailable'";
                
            using (var command = new SqlCommand(checkColumnQuery, connection))
            {
                int count = Convert.ToInt32(command.ExecuteScalar());
                hasAvailabilityColumn = (count > 0);
            }
            
            // Add columns if they don't exist
            if (!hasAvailabilityColumn)
            {
                try
                {
                    string alterTableQuery = @"
                        ALTER TABLE StudentDetails 
                        ADD IsChatAvailable BIT DEFAULT 0,
                            ChatStartTime NVARCHAR(8) DEFAULT '09:00',
                            ChatEndTime NVARCHAR(8) DEFAULT '17:00'";
                            
                    using (var command = new SqlCommand(alterTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding chat availability columns to StudentDetails table");
                }
            }
            
            // Check if VideoCallAvailability column exists
            bool hasVideoCallColumn = false;
            string checkVideoCallColumnQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'IsVideoCallAvailable'";
                
            using (var command = new SqlCommand(checkVideoCallColumnQuery, connection))
            {
                int count = Convert.ToInt32(command.ExecuteScalar());
                hasVideoCallColumn = (count > 0);
            }
            
            // Add VideoCallAvailability column if it doesn't exist
            if (!hasVideoCallColumn)
            {
                try
                {
                    string alterTableQuery = @"
                        ALTER TABLE StudentDetails 
                        ADD IsVideoCallAvailable BIT DEFAULT 0";
                            
                    using (var command = new SqlCommand(alterTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding video call availability column to StudentDetails table");
                }
            }
            
            // Get the chat availability settings
            string query = @"
                SELECT 
                    ISNULL(IsChatAvailable, 0) AS IsChatAvailable,
                    ISNULL(ChatStartTime, '09:00') AS ChatStartTime,
                    ISNULL(ChatEndTime, '17:00') AS ChatEndTime,
                    ISNULL(IsVideoCallAvailable, 0) AS IsVideoCallAvailable
                FROM StudentDetails
                WHERE IdNumber = @IdNumber";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdNumber", idNumber);
                
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        isChatAvailable = Convert.ToBoolean(reader["IsChatAvailable"]);
                        chatStartTime = reader["ChatStartTime"].ToString();
                        chatEndTime = reader["ChatEndTime"].ToString();
                        
                        // Set IsVideoCallAvailable to match IsChatAvailable initially if it's the default value
                        bool isVideoCallAvailable = Convert.ToBoolean(reader["IsVideoCallAvailable"]);
                        ViewBag.IsVideoCallAvailable = isVideoCallAvailable;
                    }
                }
            }
        }
        
        ViewBag.IsChatAvailable = isChatAvailable;
        ViewBag.ChatStartTime = chatStartTime;
        ViewBag.ChatEndTime = chatEndTime;
        
        return View("~/Views/Dashboard/StudentProfile.cshtml");
    }

  [HttpPost]
  [Route("ChangeStudentPassword")]
  [Route("/StudentDashboard/ChangeStudentPassword")]
    public async Task<IActionResult> ChangeStudentPassword([FromBody] ChangePasswordModel model)
    {
        try
        {
            if (string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(model.NewPassword))
            {
                return Json(new { success = false, message = "Current password and new password are required." });
            }

            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the new database structure is being used
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
                    // First verify current password
                    string checkPasswordQuery = @"
                        SELECT u.Password
                        FROM Users u
                        JOIN StudentDetails sd ON u.UserId = sd.UserId
                        WHERE sd.IdNumber = @IdNumber";
                        
                    string currentPasswordInDb = null;
                    
                    using (var command = new SqlCommand(checkPasswordQuery, connection))
                    {
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        currentPasswordInDb = (string)await command.ExecuteScalarAsync();
                    }
                    
                    if (currentPasswordInDb == null)
                    {
                        return Json(new { success = false, message = "Student not found." });
                    }
                    
                    if (currentPasswordInDb != model.CurrentPassword)
                    {
                        return Json(new { success = false, message = "Current password is incorrect." });
                    }
                    
                    // Update the password
                    string updatePasswordQuery = @"
                        UPDATE Users
                        SET Password = @NewPassword
                        FROM Users u
                        JOIN StudentDetails sd ON u.UserId = sd.UserId
                        WHERE sd.IdNumber = @IdNumber";
                        
                    using (var command = new SqlCommand(updatePasswordQuery, connection))
                    {
                        command.Parameters.AddWithValue("@NewPassword", model.NewPassword);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Password changed successfully." });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to update password." });
                        }
                    }
                }
                else
                {
                    // Use the old Students table
                    // First verify current password
                    string checkPasswordQuery = "SELECT Password FROM Students WHERE IdNumber = @IdNumber";
                    string currentPasswordInDb = null;
                    
                    using (var command = new SqlCommand(checkPasswordQuery, connection))
                    {
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        currentPasswordInDb = (string)await command.ExecuteScalarAsync();
                    }
                    
                    if (currentPasswordInDb == null)
                    {
                        return Json(new { success = false, message = "Student not found." });
                    }
                    
                    if (currentPasswordInDb != model.CurrentPassword)
                    {
                        return Json(new { success = false, message = "Current password is incorrect." });
                    }
                    
                    // Update the password
                    string updatePasswordQuery = "UPDATE Students SET Password = @NewPassword WHERE IdNumber = @IdNumber";
                    
                    using (var command = new SqlCommand(updatePasswordQuery, connection))
                    {
                        command.Parameters.AddWithValue("@NewPassword", model.NewPassword);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Password changed successfully." });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to update password." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error changing password: " + ex.Message });
        }
    }

   [HttpGet]
   [Route("GetStudentChats")]
    public async Task<IActionResult> GetStudentChats()
    {
        try
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student session not found. Please log in again." });
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

                string query;
                if (useNewTable)
                {
                    // Check if ProfilePicturePath column exists in EmployerDetails
                    bool hasProfilePic = await _dbUtilityService.ColumnExists(conn, "EmployerDetails", "ProfilePicturePath");
                    
                    query = @"
                        SELECT DISTINCT 
                            m.EmployerId,
                            u.FullName as EmployerName,
                            ed.Company,
                            m.Message as LastMessage,
                            m.SentTime as LastMessageTime,
                            m.IsRead";
                            
                    if (hasProfilePic) {
                        query += ", ed.ProfilePicturePath";
                    }
                    
                    query += @"
                        FROM EmployerStudentMessages m
                        JOIN Users u ON m.EmployerId = u.UserId
                        JOIN EmployerDetails ed ON u.UserId = ed.UserId
                        WHERE m.StudentId = @StudentId
                        AND m.SentTime IN (
                            SELECT MAX(SentTime)
                            FROM EmployerStudentMessages
                            WHERE StudentId = @StudentId
                            GROUP BY EmployerId
                        )
                        ORDER BY m.SentTime DESC";
                }
                else
                {
                    // Check if ProfilePicturePath column exists in Employers
                    bool hasProfilePic = await _dbUtilityService.ColumnExists(conn, "Employers", "ProfilePicturePath");
                    
                    query = @"
                        SELECT DISTINCT 
                            m.EmployerId,
                            e.FullName as EmployerName,
                            e.Company,
                            m.MessageContent as LastMessage,
                            m.SentDateTime as LastMessageTime,
                            m.IsRead";
                            
                    if (hasProfilePic) {
                        query += ", e.ProfilePicturePath";
                    }
                    
                    query += @"
                        FROM Messages m
                        JOIN Employers e ON m.EmployerId = e.EmployerId
                        WHERE m.StudentId = @StudentId
                        AND m.SentDateTime IN (
                            SELECT MAX(SentDateTime)
                            FROM Messages
                            WHERE StudentId = @StudentId
                            GROUP BY EmployerId
                        )
                        ORDER BY m.SentDateTime DESC";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    var chats = new List<object>();

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Check if ProfilePicturePath column exists in the result
                            bool hasProfilePicture = reader.HasColumn("ProfilePicturePath");
                            
                            chats.Add(new
                            {
                                employerId = reader["EmployerId"].ToString(),
                                employerName = reader["EmployerName"].ToString(),
                                company = reader["Company"].ToString(),
                                lastMessage = reader["LastMessage"].ToString(),
                                lastMessageTime = Convert.ToDateTime(reader["LastMessageTime"]),
                                isRead = Convert.ToBoolean(reader["IsRead"]),
                                profilePicture = hasProfilePicture && reader["ProfilePicturePath"] != DBNull.Value
                                    ? reader["ProfilePicturePath"].ToString()
                                    : null
                            });
                        }
                    }

                    _logger.LogInformation($"Retrieved {chats.Count} chats for student {studentId}");
                    return Json(new { success = true, chats = chats });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetStudentChats: {ex.Message}");
            return Json(new { success = false, message = "Error loading chats. Please try again." });
        }
    }

     [HttpGet]
     [Route("GetStudentMessageHistory")]
    public async Task<IActionResult> GetStudentMessageHistory(string employerId)
    {
        try
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student session not found. Please log in again." });
            }

            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer ID is required." });
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
            _logger.LogError($"Error in GetStudentMessageHistory: {ex.Message}");
            return Json(new { success = false, message = "Error loading message history. Please try again." });
        }
    }

   [HttpPost]
   [Route("SendStudentMessage")]
    public async Task<IActionResult> SendStudentMessage([FromBody] MessageModel model)
    {
        try
        {
            if (string.IsNullOrEmpty(model.Message))
            {
                return Json(new { success = false, message = "Message content is required." });
            }

            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student session not found. Please log in again." });
            }

            // Get employer ID from the model
            string employerId = model.StudentId; // In this case, StudentId field is used for EmployerId
            if (string.IsNullOrEmpty(employerId))
            {
                _logger.LogError("SendStudentMessage: Employer ID is missing from the request");
                return Json(new { success = false, message = "Employer ID is required. Please select an employer to send the message to." });
            }

            // Verify employer exists
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                // Verify employer exists
                string verifyQuery = useNewTable
                    ? "SELECT COUNT(*) FROM Users u JOIN EmployerDetails ed ON u.UserId = ed.UserId WHERE u.UserId = @EmployerId"
                    : "SELECT COUNT(*) FROM Employers WHERE EmployerId = @EmployerId";

                using (var verifyCmd = new SqlCommand(verifyQuery, conn))
                {
                    verifyCmd.Parameters.AddWithValue("@EmployerId", employerId);
                    int count = Convert.ToInt32(await verifyCmd.ExecuteScalarAsync());
                    
                    if (count == 0)
                    {
                        return Json(new { success = false, message = "Selected employer not found. Please try again." });
                    }
                }

                // Insert message
                string insertQuery;
                if (useNewTable)
                {
                    insertQuery = @"
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
                            0,
                            0
                        )";
                }
                else
                {
                    insertQuery = @"
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
                            0,
                            0
                        )";
                }

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    cmd.Parameters.AddWithValue("@Message", model.Message);
                    
                    DateTime sentTime = DateTime.Now;
                    cmd.Parameters.AddWithValue("@SentTime", sentTime);

                    int rowsAffected = await cmd.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true, message = "Message sent successfully.", sentTime = sentTime });
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
            _logger.LogError($"Error in SendStudentMessage: {ex.Message}");
            return Json(new { success = false, message = "Error sending message. Please try again." });
        }
    }

    [HttpGet]
    [Route("GetStudentAttendanceRecords")]
    public async Task<IActionResult> GetStudentAttendanceRecords(string studentId)
    {
        try
        {
            if (string.IsNullOrEmpty(studentId))
            {
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
                }
                
                if (!tableExists)
                {
                    return Json(new List<object>());
                }
                
                // Get the student's UserId from multiple sources
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
                        }
                    }
                }
                
                // Default fallback: use the studentId directly as a last resort
                if (string.IsNullOrEmpty(studentUserId))
                {
                    studentUserId = studentId;
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
                
                return Json(resolvedRecords);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving attendance records for student {StudentId}", studentId);
            return Json(new List<object>());
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

    [HttpPost]
    [Route("UpdateChatAvailability")]
    public async Task<IActionResult> UpdateChatAvailability([FromBody] ChatAvailabilityModel model)
    {
        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return Json(new { success = false, message = "User not authenticated." });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string updateQuery = @"
                    UPDATE StudentDetails
                    SET IsChatAvailable = @IsAvailable
                    WHERE IdNumber = @IdNumber";
                    
                using (var command = new SqlCommand(updateQuery, connection))
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
                        return Json(new { success = false, message = "Failed to update chat availability." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating chat availability");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    [HttpPost]
    [Route("UpdateChatTimes")]
    public async Task<IActionResult> UpdateChatTimes([FromBody] ChatTimesModel model)
    {
        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
        {
            return Json(new { success = false, message = "User not authenticated." });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string updateQuery = @"
                    UPDATE StudentDetails
                    SET ChatStartTime = @StartTime,
                        ChatEndTime = @EndTime
                    WHERE IdNumber = @IdNumber";
                    
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@StartTime", model.StartTime);
                    command.Parameters.AddWithValue("@EndTime", model.EndTime);
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update chat times." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating chat times");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    // Add new endpoints for student certificate management
    [HttpPost]
    [Route("UploadCertificate")]
    public async Task<IActionResult> UploadCertificate(IFormFile file, string certificateType, string title, string description, string issueDate)
    {
        // Get current student information from session
        string studentId = HttpContext.Session.GetString("IdNumber");
        
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Not logged in" });
        }

        if (file == null || file.Length == 0)
        {
            return Json(new { success = false, message = "No file uploaded" });
        }

        try
        {
            // Read file into byte array
            byte[] fileData;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileData = memoryStream.ToArray();
            }

            // Make sure certificateType is valid
            if (certificateType != "seminar" && certificateType != "extracurricular")
            {
                return Json(new { success = false, message = "Invalid certificate type" });
            }

            // Parse issue date
            DateTime parsedIssueDate;
            if (!DateTime.TryParse(issueDate, out parsedIssueDate))
            {
                parsedIssueDate = DateTime.Now;
            }

            // Save to database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if table exists, create if not
                await EnsureStudentCertificatesTableExists(connection);
                
                string insertSql = @"
                    INSERT INTO StudentCertificates 
                    (StudentId, CertificateType, Title, Description, IssueDate, UploadDate, CertificateData, FileName, IsVerified) 
                    VALUES 
                    (@StudentId, @CertificateType, @Title, @Description, @IssueDate, @UploadDate, @CertificateData, @FileName, 0)";
                
                using (var command = new SqlCommand(insertSql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@CertificateType", certificateType);
                    command.Parameters.AddWithValue("@Title", title);
                    command.Parameters.AddWithValue("@Description", description ?? "");
                    command.Parameters.AddWithValue("@IssueDate", parsedIssueDate);
                    command.Parameters.AddWithValue("@UploadDate", DateTime.Now);
                    command.Parameters.AddWithValue("@CertificateData", fileData);
                    command.Parameters.AddWithValue("@FileName", file.FileName);
                    
                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true, message = "Certificate uploaded successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading certificate");
            return Json(new { success = false, message = "Error uploading certificate: " + ex.Message });
        }
    }

    [HttpGet]
    [Route("GetStudentCertificates")]
    public async Task<IActionResult> GetStudentCertificates(string certificateType = null)
    {
        // Get current student information from session
        string studentId = HttpContext.Session.GetString("IdNumber");
        
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Not logged in" });
        }

        try
        {
            var certificates = new List<StudentCertificate>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if table exists, create if not
                bool tableExists = await EnsureStudentCertificatesTableExists(connection);
                
                if (!tableExists)
                {
                    return Json(new { success = true, certificates = certificates });
                }
                
                string sql = @"
                    SELECT CertificateId, StudentId, CertificateType, Title, Description, 
                           IssueDate, UploadDate, FileName, IsVerified 
                    FROM StudentCertificates 
                    WHERE StudentId = @StudentId";
                
                // Add filter by certificate type if specified
                if (!string.IsNullOrEmpty(certificateType))
                {
                    sql += " AND CertificateType = @CertificateType";
                }
                
                sql += " ORDER BY UploadDate DESC";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    if (!string.IsNullOrEmpty(certificateType))
                    {
                        command.Parameters.AddWithValue("@CertificateType", certificateType);
                    }
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            certificates.Add(new StudentCertificate
                            {
                                CertificateId = reader.GetInt32(0),
                                StudentId = reader.GetString(1),
                                CertificateType = reader.GetString(2),
                                Title = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                IssueDate = reader.GetDateTime(5),
                                UploadDate = reader.GetDateTime(6),
                                FileName = reader.GetString(7),
                                IsVerified = reader.GetBoolean(8)
                            });
                        }
                    }
                }
            }

            return Json(new { success = true, certificates = certificates });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving student certificates");
            return Json(new { success = false, message = "Error retrieving certificates: " + ex.Message });
        }
    }

    [HttpGet]
    [Route("ViewCertificate/{id}")]
    public async Task<IActionResult> ViewCertificate(int id)
    {
        // Get current student information from session
        string studentId = HttpContext.Session.GetString("IdNumber");
        string role = HttpContext.Session.GetString("Role");
        
        if (string.IsNullOrEmpty(studentId) && role != "teacher")
        {
            return Unauthorized();
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT CertificateData, FileName 
                    FROM StudentCertificates 
                    WHERE CertificateId = @CertificateId";
                
                // If user is a student, ensure they only see their own certificates
                if (role == "student")
                {
                    sql += " AND StudentId = @StudentId";
                }
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@CertificateId", id);
                    
                    if (role == "student")
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                    }
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            byte[] certificateData = (byte[])reader["CertificateData"];
                            string fileName = reader["FileName"].ToString();
                            
                            // Determine the content type based on file extension
                            string contentType = "application/octet-stream";
                            string ext = Path.GetExtension(fileName).ToLower();
                            
                            if (ext == ".pdf")
                                contentType = "application/pdf";
                            else if (ext == ".jpg" || ext == ".jpeg")
                                contentType = "image/jpeg";
                            else if (ext == ".png")
                                contentType = "image/png";
                            
                            return File(certificateData, contentType, fileName);
                        }
                    }
                }
            }

            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving certificate");
            return StatusCode(500);
        }
    }

    [HttpPost]
    [Route("DeleteCertificate")]
    public async Task<IActionResult> DeleteCertificate(int certificateId)
    {
        // Get current student information from session
        string studentId = HttpContext.Session.GetString("IdNumber");
        
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Not logged in" });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    DELETE FROM StudentCertificates 
                    WHERE CertificateId = @CertificateId AND StudentId = @StudentId";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@CertificateId", certificateId);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true, message = "Certificate deleted successfully" });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Certificate not found or you don't have permission to delete it" });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting certificate");
            return Json(new { success = false, message = "Error deleting certificate: " + ex.Message });
        }
    }

    private async Task<bool> EnsureStudentCertificatesTableExists(SqlConnection connection)
    {
        // Check if table exists
        string checkTableSql = @"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_NAME = 'StudentCertificates'";
            
        using (var command = new SqlCommand(checkTableSql, connection))
        {
            int tableCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            
            if (tableCount == 0)
            {
                // Create the table
                string createTableSql = @"
                    CREATE TABLE StudentCertificates (
                        CertificateId INT IDENTITY(1,1) PRIMARY KEY,
                        StudentId NVARCHAR(50) NOT NULL,
                        CertificateType NVARCHAR(20) NOT NULL,
                        Title NVARCHAR(255) NOT NULL,
                        Description NVARCHAR(MAX) NULL,
                        IssueDate DATETIME NOT NULL,
                        UploadDate DATETIME NOT NULL,
                        CertificateData VARBINARY(MAX) NOT NULL,
                        FileName NVARCHAR(255) NOT NULL,
                        IsVerified BIT NOT NULL DEFAULT 0,
                        VerifiedBy NVARCHAR(50) NULL,
                        VerificationDate DATETIME NULL,
                        Score INT NULL
                    )";
                
                using (var createCommand = new SqlCommand(createTableSql, connection))
                {
                    await createCommand.ExecuteNonQueryAsync();
                }
                
                return false;
            }
            else
            {
                // Check if Score column exists and add it if it doesn't
                string checkScoreColumnSql = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentCertificates' AND COLUMN_NAME = 'Score'";
                
                using (var checkCommand = new SqlCommand(checkScoreColumnSql, connection))
                {
                    int columnExists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                    
                    if (columnExists == 0)
                    {
                        // Add Score column
                        string addColumnSql = @"ALTER TABLE StudentCertificates ADD Score INT NULL";
                        
                        using (var alterCommand = new SqlCommand(addColumnSql, connection))
                        {
                            await alterCommand.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            
            return true;
        }
    }

}

public class ChatAvailabilityModel
{
    public bool IsAvailable { get; set; }
}

public class ChatTimesModel
{
    public string StartTime { get; set; }
    public string EndTime { get; set; }
}

public class ProfileVisibilityModel
{
    public bool IsVisible { get; set; }
}

// Add the StudentCertificate model class
public class StudentCertificate
{
    public int CertificateId { get; set; }
    public string StudentId { get; set; }
    public string CertificateType { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime UploadDate { get; set; }
    public string FileName { get; set; }
    public bool IsVerified { get; set; }
    public string VerifiedBy { get; set; }
    public DateTime? VerificationDate { get; set; }
}