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
        ViewBag.IdNumber = HttpContext.Session.GetString("IdNumber");
        ViewBag.Course = HttpContext.Session.GetString("Course");
        ViewBag.Section = HttpContext.Session.GetString("Section");

        // Get the current student's score and profile picture
        await GetCurrentStudentScoreAndPicture();

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
                command.ExecuteNonQuery();
            }
        }

        return Ok(new { message = "Resume visibility updated successfully." });
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
                string query = @"
                    SELECT 
                        sd.IsProfileVisible, 
                        sd.IsResumeVisible, 
                        sd.ProfilePicturePath,
                        sd.ResumeFileName,
                        sd.Score, 
                        sd.Achievements, 
                        sd.Comments, 
                        sd.BadgeColor
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

        // Add the basic student info
        string fullName = HttpContext.Session.GetString("FullName");
        string course = HttpContext.Session.GetString("Course");
        string section = HttpContext.Session.GetString("Section");

        ViewBag.FullName = fullName;
        ViewBag.IdNumber = idNumber;
        ViewBag.Course = course;
        ViewBag.Section = section;
        
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
                               ec.RecordedDate, ec.Score, u.FullName as TeacherName, 
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

}