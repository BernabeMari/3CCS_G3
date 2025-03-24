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

public class DashboardController : Controller
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;

    public DashboardController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("YourConnectionString");
        _configuration = configuration;
    }

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

        return View();
    }

    private async Task GetCurrentStudentScoreAndPicture()
    {
        string idNumber = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(idNumber))
            return;

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT Score, ProfilePicturePath, ResumeFileName, IsProfileVisible, IsResumeVisible FROM Students WHERE IdNumber = @IdNumber";

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
                        else
                        {
                            ViewBag.Score = 0;
                        }

                        // Get profile picture path
                        string profilePicturePath = reader["ProfilePicturePath"] as string;
                        ViewBag.ProfilePicturePath = !string.IsNullOrEmpty(profilePicturePath) ? profilePicturePath : null;
                        
                        // Get resume file path
                        string resumeFileName = reader["ResumeFileName"] as string;
                        ViewBag.ResumePath = !string.IsNullOrEmpty(resumeFileName) ? 
                            Url.Action("GetResume", "Dashboard", new { studentId = idNumber }) : null;
                        
                        // Get visibility settings
                        ViewBag.IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                        ViewBag.IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]);
                    }
                }
            }
        }
    }

    private async Task<List<Student>> GetAllStudentsWithDetails()
    {
        var allStudents = new List<Student>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT IdNumber, FullName, Course, Section, IsProfileVisible, ProfilePicturePath, ResumeFileName, Score, Achievements, Comments, BadgeColor, IsResumeVisible FROM Students ORDER BY Score DESC";

            using (var command = new SqlCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    allStudents.Add(new Student
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
                    });
                }
            }
        }

        return allStudents;
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
            string query = "UPDATE Students SET IsProfileVisible = @IsVisible WHERE IdNumber = @IdNumber";
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
            string query = "UPDATE Students SET IsResumeVisible = @IsVisible WHERE IdNumber = @IdNumber";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IsVisible", model.IsVisible);
                command.Parameters.AddWithValue("@IdNumber", idNumber);
                command.ExecuteNonQuery();
            }
        }

        return Ok(new { message = "Resume visibility updated successfully." });
    }

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
            string query = @"SELECT 
                            IsProfileVisible, 
                            IsResumeVisible, 
                            ProfilePicturePath,
                            ResumeFileName,
                            Score, 
                            Achievements, 
                            Comments, 
                            BadgeColor
                        FROM Students 
                        WHERE IdNumber = @IdNumber";

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
                        hasResume = !string.IsNullOrEmpty(resumeFileName) && 
                                   (resumeFileName.StartsWith("data:") || 
                                    resumeFileName.StartsWith("http") || 
                                    resumeFileName.StartsWith("/"));
                    }
                }
            }
        }

        ViewBag.IsProfileVisible = isProfileVisible;
        ViewBag.IsResumeVisible = isResumeVisible;
        ViewBag.ProfilePicturePath = hasProfilePicture ? profilePicturePath : "/images/blank.jpg";
        ViewBag.ResumePath = hasResume ? Url.Action("GetResume", "Dashboard") : "";
        ViewBag.ResumeFileName = resumeFileName != null && resumeFileName.StartsWith("data:") ? "Resume" : resumeFileName;
        ViewBag.Score = score;
        ViewBag.Achievements = achievements;
        ViewBag.Comments = comments;
        ViewBag.BadgeColor = badgeColor;
        ViewBag.HasProfilePicture = hasProfilePicture;
        ViewBag.HasResume = hasResume;

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SaveProfilePicture([FromBody] ProfilePictureModel model)
    {
        if (model == null || string.IsNullOrEmpty(model.Base64Image))
        {
            return Json(new { success = false, message = "No image data received." });
        }

        try
        {
            string base64Image = model.Base64Image;

            // Handle the case when the full data URL is sent
            if (base64Image.Contains(","))
            {
                base64Image = base64Image.Split(',')[1];
            }

            // Validate that the string is valid base64
            if (!IsValidBase64(base64Image))
            {
                return Json(new { success = false, message = "Invalid image format." });
            }

            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            string contentType = "image/jpeg"; // Default value
            string dataUrl = "";
            
            // If the original input includes the content type, use it
            if (model.Base64Image.Contains("data:"))
            {
                string[] parts = model.Base64Image.Split(',');
                dataUrl = model.Base64Image; // Keep the full data URL
            }
            else
            {
                // Create a data URL
                dataUrl = $"data:{contentType};base64,{base64Image}";
            }

            // Store the data URL directly in the ProfilePicturePath column
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    UPDATE Students 
                    SET ProfilePicturePath = @ProfilePicturePath
                    WHERE IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProfilePicturePath", dataUrl);
                    command.Parameters.AddWithValue("@IdNumber", idNumber);

                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        return Json(new
                        {
                            success = true,
                            message = "Profile picture saved successfully.",
                            imageUrl = dataUrl
                        });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update profile picture. User not found." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error saving profile picture: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveResume([FromBody] ResumeModel model)
    {
        if (model == null || string.IsNullOrEmpty(model.ResumeFile))
        {
            return Json(new { success = false, message = "No resume file received." });
        }

        try
        {
            string base64File = model.ResumeFile;
            string fileName = model.ResumeFileName;

            // Handle the case when the full data URL is sent
            if (base64File.Contains(","))
            {
                base64File = base64File.Split(',')[1];
            }

            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            // Determine content type based on file extension
            string contentType = "application/octet-stream"; // Default
            string extension = Path.GetExtension(fileName).ToLower();
            if (extension == ".pdf")
                contentType = "application/pdf";
            else if (extension == ".doc")
                contentType = "application/msword";
            else if (extension == ".docx")
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

            // Create a data URL for the resume
            string dataUrl = $"data:{contentType};base64,{base64File}";

            // Store the original filename in session for later use
            HttpContext.Session.SetString("OriginalResumeFileName", fileName);

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Store the data URL and original filename in the database
                string query = @"
                    UPDATE Students 
                    SET ResumeFileName = @ResumeData,
                        OriginalResumeFileName = @OriginalFileName
                    WHERE IdNumber = @IdNumber";

                // Check if OriginalResumeFileName column exists, if not then only update ResumeFileName
                bool columnExists = false;
                string checkColumnQuery = @"
                    SELECT COLUMN_NAME 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'OriginalResumeFileName'";
                
                using (var checkCmd = new SqlCommand(checkColumnQuery, connection))
                {
                    var result = await checkCmd.ExecuteScalarAsync();
                    columnExists = (result != null);
                }

                if (!columnExists)
                {
                    query = @"UPDATE Students SET ResumeFileName = @ResumeData WHERE IdNumber = @IdNumber";
                }

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ResumeData", dataUrl);
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    
                    if (columnExists)
                    {
                        command.Parameters.AddWithValue("@OriginalFileName", fileName);
                    }
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        string resumeUrl = Url.Action("GetResume", "Dashboard");
                        
                        return Json(new
                        {
                            success = true,
                            message = "Resume uploaded successfully.",
                            resumeUrl = resumeUrl
                        });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update resume. User not found." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error saving resume: " + ex.Message });
        }
    }

    private bool IsValidBase64(string base64String)
    {
        if (string.IsNullOrEmpty(base64String))
            return false;

        // Check that the string contains only valid base64 characters
        Span<char> buffer = stackalloc char[base64String.Length];
        base64String.CopyTo(buffer);

        // Try to convert from base64
        try
        {
            Convert.TryFromBase64Chars(buffer, new Span<byte>(), out int _);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IActionResult Login()
    {
        return View();
    }

    public ActionResult EmployerDashboard()
    {
        try
        {
            // Get the employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            
            if (string.IsNullOrEmpty(employerId))
            {
                // If not available in session, try to get from claims
                var claimsIdentity = User.Identity as ClaimsIdentity;
                if (claimsIdentity != null)
                {
                    var userIdClaim = claimsIdentity.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);
                    if (userIdClaim != null)
                    {
                        employerId = userIdClaim.Value;
                    }
                }
                
                // If still not available, redirect to login
                if (string.IsNullOrEmpty(employerId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Store in session for future use
                HttpContext.Session.SetString("EmployerId", employerId);
            }
            
            string query = "SELECT FullName, Company FROM Employers WHERE EmployerId = @EmployerId";
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            ViewBag.EmployerName = reader["FullName"].ToString();
                            ViewBag.CompanyName = reader["Company"].ToString();
                        }
                        else
                        {
                            return RedirectToAction("Login", "Home");
                        }
                    }
                }
                
                ViewBag.EmployerId = employerId; // Make sure this is set
                
                // Get all students for display
                var students = new List<Student>();
                string studentsQuery = "SELECT * FROM Students WHERE IsProfileVisible = 1";
                
                using (SqlCommand command = new SqlCommand(studentsQuery, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            students.Add(new Student
                            {
                                IdNumber = reader["IdNumber"]?.ToString() ?? string.Empty,
                                FullName = reader["FullName"]?.ToString() ?? string.Empty,
                                Course = reader["Course"]?.ToString() ?? string.Empty,
                                Section = reader["Section"]?.ToString() ?? string.Empty,
                                BadgeColor = reader.IsDBNull(reader.GetOrdinal("BadgeColor")) ? "green" : reader["BadgeColor"].ToString(),
                                Score = reader.IsDBNull(reader.GetOrdinal("Score")) ? 0 : Convert.ToInt32(reader["Score"])
                            });
                        }
                    }
                }
                
                ViewBag.AllStudents = students;
            }
            
            Console.WriteLine($"EmployerId in ViewBag: {ViewBag.EmployerId}"); // Debugging output
            
            return View();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in EmployerDashboard: {ex.Message}");
            return RedirectToAction("Error", "Home");
        }
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] MessageModel model)
    {
        if (model == null || string.IsNullOrEmpty(model.Message))
        {
            return Json(new { success = false, message = "No message content received." });
        }

        try
        {
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer not authenticated." });
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    INSERT INTO EmployerStudentMessages (EmployerId, StudentId, Message, IsFromEmployer, SentTime, IsRead) 
                    VALUES (@EmployerId, @StudentId, @Message, 1, @SentTime, 0)";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    command.Parameters.AddWithValue("@StudentId", model.StudentId);
                    command.Parameters.AddWithValue("@Message", model.Message);
                    command.Parameters.AddWithValue("@SentTime", DateTime.UtcNow);

                    await command.ExecuteNonQueryAsync();

                    return Json(new { 
                        success = true, 
                        message = "Message sent successfully.",
                        sentTime = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error sending message: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMessageHistory(string studentId)
    {
        try
        {
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer not authenticated." });
            }

            var messages = new List<MessageViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT m.Message as MessageContent, m.SentTime, m.IsFromEmployer,
                           e.FullName as EmployerName, e.Company,
                           s.FullName as StudentName
                    FROM EmployerStudentMessages m
                    JOIN Employers e ON m.EmployerId = e.EmployerId
                    JOIN Students s ON m.StudentId = s.IdNumber
                    WHERE (m.EmployerId = @EmployerId AND m.StudentId = @StudentId)
                    ORDER BY m.SentTime ASC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            bool isFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"]);
                            messages.Add(new MessageViewModel
                            {
                                Content = reader["MessageContent"].ToString(),
                                SentTime = Convert.ToDateTime(reader["SentTime"]),
                                IsFromEmployer = isFromEmployer,
                                EmployerName = isFromEmployer ? reader["EmployerName"].ToString() : "",
                                Company = isFromEmployer ? reader["Company"].ToString() : "",
                                StudentName = !isFromEmployer ? reader["StudentName"].ToString() : ""
                            });
                        }
                    }
                }
            }

            return Json(new { success = true, messages = messages });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentProfile(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Student ID not provided." });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT FullName, Course, Section, Score, Achievements, Comments, 
                           BadgeColor, ProfilePicturePath, ResumeFileName, IsResumeVisible
                    FROM Students 
                    WHERE IdNumber = @StudentId AND IsProfileVisible = 1";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Handle profile picture using existing path
                            string profilePicture = null;
                            if (reader["ProfilePicturePath"] != DBNull.Value && !string.IsNullOrEmpty(reader["ProfilePicturePath"].ToString()))
                            {
                                profilePicture = reader["ProfilePicturePath"].ToString();
                            }
                            
                            // Handle resume path
                            string resume = null;
                            bool isResumeVisible = reader.GetBoolean(reader.GetOrdinal("IsResumeVisible"));
                            
                            if (isResumeVisible && reader["ResumeFileName"] != DBNull.Value && !string.IsNullOrEmpty(reader["ResumeFileName"].ToString()))
                            {
                                string resumeFileName = reader["ResumeFileName"].ToString();
                                
                                // Check if it's already a data URL
                                if (resumeFileName.StartsWith("data:"))
                                {
                                    resume = resumeFileName;
                                }
                                else
                                {
                                    // Create a URL to the GetResume action
                                    resume = Url.Action("GetResume", "Dashboard", new { studentId });
                                }
                            }

                            return Json(new
                            {
                                success = true,
                                fullName = reader["FullName"].ToString(),
                                course = reader["Course"].ToString(),
                                section = reader["Section"].ToString(),
                                score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                                achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "",
                                comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "",
                                badgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "white",
                                profilePicture = profilePicture,
                                isResumeVisible = isResumeVisible,
                                resume = resume
                            });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student profile not found or not visible." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentMessages(string studentId)
    {
        try
        {
            var messages = new List<StudentMessageViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT m.MessageId, m.Message as MessageContent, m.SentTime, m.IsRead,
                           e.EmployerId, e.FullName as EmployerName, e.Company
                    FROM EmployerStudentMessages m
                    JOIN Employers e ON m.EmployerId = e.EmployerId
                    WHERE m.StudentId = @StudentId AND m.IsFromEmployer = 1
                    ORDER BY m.SentTime DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            messages.Add(new StudentMessageViewModel
                            {
                                MessageId = Convert.ToInt32(reader["MessageId"]),
                                Content = reader["MessageContent"].ToString(),
                                SentTime = Convert.ToDateTime(reader["SentTime"]),
                                IsRead = Convert.ToBoolean(reader["IsRead"]),
                                EmployerId = reader["EmployerId"].ToString(),
                                EmployerName = reader["EmployerName"].ToString(),
                                Company = reader["Company"].ToString()
                            });
                        }
                    }
                }
            }

            return Json(new { success = true, messages = messages });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> MarkMessageAsRead(int messageId)
    {
        try
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student not authenticated." });
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "UPDATE EmployerStudentMessages SET IsRead = 1 WHERE MessageId = @MessageId AND StudentId = @StudentId";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@MessageId", messageId);
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error marking message as read: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllMessagesAsRead(string studentId)
    {
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "UPDATE EmployerStudentMessages SET IsRead = 1 WHERE StudentId = @StudentId AND IsFromEmployer = 1";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error marking all messages as read: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetStudentMessageHistory(string employerId)
    {
        try
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student not authenticated." });
            }

            var messages = new List<MessageViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT m.Message as MessageContent, m.SentTime, m.IsFromEmployer,
                           e.FullName as EmployerName, e.Company,
                           s.FullName as StudentName
                    FROM EmployerStudentMessages m
                    JOIN Employers e ON m.EmployerId = e.EmployerId
                    JOIN Students s ON m.StudentId = s.IdNumber
                    WHERE (m.EmployerId = @EmployerId AND m.StudentId = @StudentId)
                    ORDER BY m.SentTime ASC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            bool isFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"]);
                            messages.Add(new MessageViewModel
                            {
                                Content = reader["MessageContent"].ToString(),
                                SentTime = Convert.ToDateTime(reader["SentTime"]),
                                IsFromEmployer = isFromEmployer,
                                EmployerName = isFromEmployer ? reader["EmployerName"].ToString() : "",
                                Company = isFromEmployer ? reader["Company"].ToString() : "",
                                StudentName = !isFromEmployer ? reader["StudentName"].ToString() : ""
                            });
                        }
                    }
                }
            }

            return Json(new { success = true, messages = messages });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SendStudentMessage([FromBody] MessageModel model)
    {
        if (model == null || string.IsNullOrEmpty(model.Message))
        {
            return Json(new { success = false, message = "No message content received." });
        }

        try
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student not authenticated." });
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    INSERT INTO EmployerStudentMessages (EmployerId, StudentId, Message, IsFromEmployer, SentTime, IsRead) 
                    VALUES (@EmployerId, @StudentId, @Message, 0, @SentTime, 0)";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", model.EmployerId);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@Message", model.Message);
                    command.Parameters.AddWithValue("@SentTime", DateTime.UtcNow);

                    await command.ExecuteNonQueryAsync();

                    return Json(new { 
                        success = true, 
                        message = "Message sent successfully.",
                        sentTime = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error sending message: " + ex.Message });
        }
    }

    // Add these methods to make the chat functionality work properly

    // This method provides basic student info for the chat header in employer dashboard
    public async Task<IActionResult> GetStudentBasicInfo(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Student ID is required." });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT FullName, Course, Section, ProfilePicturePath
                    FROM Students 
                    WHERE IdNumber = @StudentId AND IsProfileVisible = 1";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Handle profile picture
                            string profilePicture = null;
                            if (reader["ProfilePicturePath"] != DBNull.Value && !string.IsNullOrEmpty(reader["ProfilePicturePath"].ToString()))
                            {
                                profilePicture = reader["ProfilePicturePath"].ToString();
                            }

                            return Json(new
                            {
                                success = true,
                                fullName = reader["FullName"].ToString(),
                                course = $"{reader["Course"]} - {reader["Section"]}",
                                profilePicture = profilePicture
                            });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student not found or profile is not visible." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // This method gets all students an employer has chatted with for the Recent Conversations panel
    [HttpGet]
    public async Task<IActionResult> GetEmployerChats(string employerId)
    {
        if (string.IsNullOrEmpty(employerId))
        {
            employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer ID not provided." });
            }
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT DISTINCT m.StudentId, s.FullName as StudentName, s.ProfilePicturePath,
                           (SELECT TOP 1 Message FROM EmployerStudentMessages 
                            WHERE EmployerId = m.EmployerId AND StudentId = m.StudentId 
                            ORDER BY SentTime DESC) as LastMessage,
                           (SELECT TOP 1 SentTime FROM EmployerStudentMessages 
                            WHERE EmployerId = m.EmployerId AND StudentId = m.StudentId 
                            ORDER BY SentTime DESC) as LastMessageTime
                    FROM EmployerStudentMessages m
                    JOIN Students s ON m.StudentId = s.IdNumber
                    WHERE m.EmployerId = @EmployerId
                    ORDER BY LastMessageTime DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    var chats = new List<object>();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Handle profile picture
                            string profilePicture = null;
                            if (reader["ProfilePicturePath"] != DBNull.Value && !string.IsNullOrEmpty(reader["ProfilePicturePath"].ToString()))
                            {
                                profilePicture = reader["ProfilePicturePath"].ToString();
                            }

                            chats.Add(new
                            {
                                studentId = reader["StudentId"].ToString(),
                                studentName = reader["StudentName"].ToString(),
                                profilePicture = profilePicture,
                                lastMessage = reader["LastMessage"] != DBNull.Value ? reader["LastMessage"].ToString() : null,
                                lastMessageTime = reader["LastMessageTime"] != DBNull.Value ? Convert.ToDateTime(reader["LastMessageTime"]) : DateTime.MinValue
                            });
                        }
                    }

                    return Json(new { success = true, chats = chats });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving chats: " + ex.Message });
        }
    }

    // This method improves the existing GetMessageHistory method to properly handle the employer dashboard chat
    [HttpGet]
    public async Task<IActionResult> GetEmployerMessageHistory(string studentId)
    {
        try
        {
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer not authenticated." });
            }

            var messages = new List<MessageViewModel>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT m.Message as MessageContent, m.SentTime, m.IsFromEmployer,
                           e.FullName as EmployerName, e.Company,
                           s.FullName as StudentName
                    FROM EmployerStudentMessages m
                    JOIN Employers e ON m.EmployerId = e.EmployerId
                    JOIN Students s ON m.StudentId = s.IdNumber
                    WHERE (m.EmployerId = @EmployerId AND m.StudentId = @StudentId)
                    ORDER BY m.SentTime ASC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    command.Parameters.AddWithValue("@StudentId", studentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            bool isFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"]);
                            messages.Add(new MessageViewModel
                            {
                                Content = reader["MessageContent"].ToString(),
                                SentTime = Convert.ToDateTime(reader["SentTime"]),
                                IsFromEmployer = isFromEmployer,
                                EmployerName = isFromEmployer ? reader["EmployerName"].ToString() : "",
                                Company = isFromEmployer ? reader["Company"].ToString() : "",
                                StudentName = !isFromEmployer ? reader["StudentName"].ToString() : ""
                            });
                        }
                    }
                }
            }

            return Json(new { success = true, messages = messages });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
        }
    }

    // Add new API endpoints to retrieve images and documents from the database

    [HttpGet]
    public async Task<IActionResult> GetProfilePicture(string studentId = null)
    {
        string idNumber = studentId ?? HttpContext.Session.GetString("IdNumber");
        
        if (string.IsNullOrEmpty(idNumber))
        {
            return NotFound();
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "SELECT ProfilePicturePath FROM Students WHERE IdNumber = @IdNumber";
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync() && reader["ProfilePicturePath"] != DBNull.Value)
                        {
                            string profilePicturePath = reader["ProfilePicturePath"].ToString();
                            
                            // If the profile picture is a data URL
                            if (profilePicturePath.StartsWith("data:"))
                            {
                                string[] parts = profilePicturePath.Split(new[] { ':', ';', ',' }, 4);
                                if (parts.Length == 4)
                                {
                                    string contentType = parts[1];
                                    string base64Data = parts[3];
                                    
                                    byte[] imageData = Convert.FromBase64String(base64Data);
                                    return File(imageData, contentType);
                                }
                            }
                            
                            // If it's a regular URL, redirect to it
                            if (profilePicturePath.StartsWith("http") || profilePicturePath.StartsWith("/"))
                            {
                                return Redirect(profilePicturePath);
                            }
                        }
                    }
                }
            }
            
            // If no image found or image is null, return a default image
            return File("~/images/blank.jpg", "image/jpeg");
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error retrieving profile picture: " + ex.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetResume(string studentId = null)
    {
        string idNumber = studentId ?? HttpContext.Session.GetString("IdNumber");
        
        if (string.IsNullOrEmpty(idNumber))
        {
            return NotFound();
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if OriginalResumeFileName column exists
                bool originalFileNameColumnExists = false;
                string checkColumnQuery = @"
                    SELECT COLUMN_NAME 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'OriginalResumeFileName'";
                
                using (var checkCmd = new SqlCommand(checkColumnQuery, connection))
                {
                    var result = await checkCmd.ExecuteScalarAsync();
                    originalFileNameColumnExists = (result != null);
                }
                
                // Get resume data from ResumeFileName column and original filename if available
                string query = originalFileNameColumnExists
                    ? "SELECT ResumeFileName, OriginalResumeFileName, IsResumeVisible FROM Students WHERE IdNumber = @IdNumber"
                    : "SELECT ResumeFileName, IsResumeVisible FROM Students WHERE IdNumber = @IdNumber";
                
                string resumeData = null;
                string originalFileName = null;
                bool isResumeVisible = false;
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync() && reader["ResumeFileName"] != DBNull.Value)
                        {
                            resumeData = reader["ResumeFileName"].ToString();
                            isResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]);
                            
                            if (originalFileNameColumnExists && reader["OriginalResumeFileName"] != DBNull.Value)
                            {
                                originalFileName = reader["OriginalResumeFileName"].ToString();
                            }
                        }
                        else
                        {
                            return NotFound("Resume not found.");
                        }
                    }
                }
                
                // Check visibility for other students' resumes
                if (studentId != null && studentId != HttpContext.Session.GetString("IdNumber") && !isResumeVisible)
                {
                    return Forbid("This resume is not shared publicly.");
                }
                
                // Try to get original filename from session if not found in database
                if (string.IsNullOrEmpty(originalFileName))
                {
                    originalFileName = HttpContext.Session.GetString("OriginalResumeFileName");
                }
                
                // If the resume is stored as a data URL
                if (resumeData != null && resumeData.StartsWith("data:"))
                {
                    string[] parts = resumeData.Split(new[] { ':', ';', ',' }, 4);
                    if (parts.Length == 4)
                    {
                        string contentType = parts[1];
                        string base64Data = parts[3];
                        
                        byte[] fileData = Convert.FromBase64String(base64Data);
                        string fileName;
                        
                        // Use original filename if available
                        if (!string.IsNullOrEmpty(originalFileName))
                        {
                            fileName = originalFileName;
                        }
                        else
                        {
                            // Try to extract a content type-based filename
                            if (contentType.Contains("pdf"))
                                fileName = "resume.pdf";
                            else if (contentType.Contains("msword"))
                                fileName = "resume.doc";
                            else if (contentType.Contains("openxmlformats"))
                                fileName = "resume.docx";
                            else
                                fileName = "resume";
                        }
                        
                        return File(fileData, contentType, fileName);
                    }
                }
                
                // If it's a regular URL, redirect to it
                if (resumeData != null && (resumeData.StartsWith("http") || resumeData.StartsWith("/")))
                {
                    return Redirect(resumeData);
                }
                
                return NotFound("Resume not found or format not recognized.");
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error retrieving resume: " + ex.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployerMessages(string employerId)
    {
        if (string.IsNullOrEmpty(employerId))
        {
            employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer ID not provided." });
            }
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT m.StudentId, s.FullName as StudentName, m.Message, m.SentTime, m.IsRead, m.IsFromEmployer
                    FROM EmployerStudentMessages m
                    JOIN Students s ON m.StudentId = s.IdNumber
                    WHERE m.EmployerId = @EmployerId AND m.IsFromEmployer = 0
                    ORDER BY m.SentTime DESC";

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
            return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
        }
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
            string sql = "SELECT * FROM dbo.Students";
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
                            Course = reader["Course"]?.ToString() ?? "",
                            Section = reader["Section"]?.ToString() ?? "",
                            BadgeColor = reader["BadgeColor"]?.ToString() ?? "",
                            Score = Convert.ToInt32(reader["Score"])
                        });
                    }
                }
            }
        }

        // Set total student count
        ViewBag.TotalStudentCount = students.Count;

        return View(students);
    }

    // Add this method for importing students
    [HttpPost]
    public async Task<IActionResult> ImportStudents(IFormFile file)
    {
        if (file == null || file.Length <= 0)
        {
            TempData["Error"] = "Please select a file to upload.";
            return RedirectToAction("AdminDashboard");
        }

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Please select an Excel file (.xlsx).";
            return RedirectToAction("AdminDashboard");
        }

        var successCount = 0;
        var errorCount = 0;
        var errors = new List<string>();

        try
        {
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;

                    // Skip header row (row 1)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        try
                        {
                            var fullName = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                            var username = worksheet.Cells[row, 2].Value?.ToString()?.Trim();
                            var password = worksheet.Cells[row, 3].Value?.ToString()?.Trim();
                            var idNumber = worksheet.Cells[row, 4].Value?.ToString()?.Trim();
                            var course = worksheet.Cells[row, 5].Value?.ToString()?.Trim();
                            var section = worksheet.Cells[row, 6].Value?.ToString()?.Trim();

                            // Validate ALL fields are required (including Section)
                            if (string.IsNullOrEmpty(fullName) || 
                                string.IsNullOrEmpty(username) || 
                                string.IsNullOrEmpty(password) ||
                                string.IsNullOrEmpty(idNumber) || 
                                string.IsNullOrEmpty(course) ||
                                string.IsNullOrEmpty(section))
                            {
                                errors.Add($"Row {row}: All fields are required (Full Name, Username, Password, ID Number, Course, Section)");
                                errorCount++;
                                continue;
                            }

                            // Validate course values
                            string[] validCourses = { "CAS", "CBM", "CCJ", "COE", "CET", "CHTM", "CICT" };
                            if (!validCourses.Contains(course))
                            {
                                errors.Add($"Row {row}: Invalid course value '{course}'. Valid values are: CAS, CBM, CCJ, COE, CET, CHTM, CICT");
                                errorCount++;
                                continue;
                            }

                            // Check if student with this ID already exists
                            using (var connection = new SqlConnection(_connectionString))
                            {
                                await connection.OpenAsync();
                                string checkIdQuery = "SELECT COUNT(*) FROM Students WHERE IdNumber = @IdNumber";
                                using (var command = new SqlCommand(checkIdQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                                    var result = await command.ExecuteScalarAsync();
                                    int idExists = result != null ? Convert.ToInt32(result) : 0;
                                    if (idExists > 0)
                                    {
                                        errors.Add($"Row {row}: Student with ID {idNumber} already exists");
                                        errorCount++;
                                        continue;
                                    }
                                }

                                // Check if username already exists
                                string checkUsernameQuery = "SELECT COUNT(*) FROM Students WHERE Username = @Username";
                                using (var command = new SqlCommand(checkUsernameQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@Username", username);
                                    var result = await command.ExecuteScalarAsync();
                                    int usernameExists = result != null ? Convert.ToInt32(result) : 0;
                                    if (usernameExists > 0)
                                    {
                                        errors.Add($"Row {row}: Username {username} already exists");
                                        errorCount++;
                                        continue;
                                    }
                                }

                                // Insert the new student
                                string insertQuery = @"
                                    INSERT INTO Students (FullName, Username, Password, IdNumber, Course, Section, IsProfileVisible, IsResumeVisible, Score, BadgeColor)
                                    VALUES (@FullName, @Username, @Password, @IdNumber, @Course, @Section, 1, 1, 0, 'green')";

                                using (var command = new SqlCommand(insertQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@FullName", fullName);
                                    command.Parameters.AddWithValue("@Username", username);
                                    command.Parameters.AddWithValue("@Password", password);
                                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                                    command.Parameters.AddWithValue("@Course", course);
                                    command.Parameters.AddWithValue("@Section", section);
                                    
                                    await command.ExecuteNonQueryAsync();
                                    successCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Row {row}: {ex.Message}");
                            errorCount++;
                        }
                    }
                }
            }

            TempData["Success"] = $"Successfully imported {successCount} student records.";
            if (errorCount > 0)
            {
                TempData["ErrorList"] = string.Join("<br/>", errors);
            }

            return RedirectToAction("AdminDashboard");
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error: {ex.Message}";
            return RedirectToAction("AdminDashboard");
        }
    }

    // Add this method for downloading the template
    public IActionResult DownloadTemplate()
    {
        // Make sure to register the EPPlus license context at application startup
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        
        var stream = new MemoryStream();
        using (var package = new ExcelPackage(stream))
        {
            var worksheet = package.Workbook.Worksheets.Add("Students");
            
            // Add header row with all required columns
            worksheet.Cells[1, 1].Value = "Full Name*";
            worksheet.Cells[1, 2].Value = "Username*";
            worksheet.Cells[1, 3].Value = "Password*";
            worksheet.Cells[1, 4].Value = "ID Number*";
            worksheet.Cells[1, 5].Value = "Course*";
            worksheet.Cells[1, 6].Value = "Section*";
            
            // Format header row with bold font and background color
            using (var range = worksheet.Cells[1, 1, 1, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
            
            // Add sample data
            worksheet.Cells[2, 1].Value = "John Doe";
            worksheet.Cells[2, 2].Value = "john.doe";
            worksheet.Cells[2, 3].Value = "password123";
            worksheet.Cells[2, 4].Value = "2023001";
            worksheet.Cells[2, 5].Value = "CET";
            worksheet.Cells[2, 6].Value = "A";
            
            worksheet.Cells[3, 1].Value = "Jane Smith";
            worksheet.Cells[3, 2].Value = "jane.smith";
            worksheet.Cells[3, 3].Value = "password456";
            worksheet.Cells[3, 4].Value = "2023002";
            worksheet.Cells[3, 5].Value = "CICT";
            worksheet.Cells[3, 6].Value = "B";
            
            // Add a note about all fields being required
            worksheet.Cells[5, 1].Value = "* All fields are required";
            worksheet.Cells[5, 1, 5, 6].Merge = true;
            worksheet.Cells[5, 1].Style.Font.Bold = true;
            worksheet.Cells[5, 1].Style.Font.Italic = true;
            
            // Add a note about valid course values
            worksheet.Cells[6, 1].Value = "Valid Course values: CAS, CBM, CCJ, COE, CET, CHTM, CICT";
            worksheet.Cells[6, 1, 6, 6].Merge = true;
            worksheet.Cells[6, 1].Style.Font.Italic = true;
            
            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            
            package.Save();
        }
        
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "StudentImportTemplate.xlsx");
    }

    // Add this method to update a student
    [HttpPost]
    public async Task<IActionResult> UpdateStudent(string IdNumber, string FullName, string Username, string Password, string Course, string Section, string BadgeColor)
    {
        try
        {
            // Only Username is strictly required
            if (string.IsNullOrEmpty(IdNumber) || string.IsNullOrEmpty(Username))
            {
                return Json(new { success = false, message = "Student ID and Username are required." });
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if username already exists for another student
                string checkQuery = "SELECT COUNT(*) FROM Students WHERE Username = @Username AND IdNumber != @IdNumber";
                using (var command = new SqlCommand(checkQuery, connection))
                {
                    command.Parameters.AddWithValue("@Username", Username);
                    command.Parameters.AddWithValue("@IdNumber", IdNumber);
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    if (count > 0)
                    {
                        return Json(new { success = false, message = "Username already exists for another student." });
                    }
                }
                
                // First, get the current student data to only update what's provided
                string getStudentQuery = "SELECT * FROM Students WHERE IdNumber = @IdNumber";
                Student currentStudent = null;
                
                using (var command = new SqlCommand(getStudentQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", IdNumber);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            currentStudent = new Student
                            {
                                FullName = reader["FullName"]?.ToString(),
                                Username = reader["Username"]?.ToString(),
                                Course = reader["Course"]?.ToString(),
                                Section = reader["Section"]?.ToString(),
                                BadgeColor = reader["BadgeColor"]?.ToString()
                            };
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student not found." });
                        }
                    }
                }
                
                // Build the update query based on which fields are provided
                List<string> updateFields = new List<string>();
                
                if (!string.IsNullOrEmpty(FullName))
                    updateFields.Add("FullName = @FullName");
                
                // Username is always updated since it's required
                updateFields.Add("Username = @Username");
                
                if (!string.IsNullOrEmpty(Password))
                    updateFields.Add("Password = @Password");
                
                if (!string.IsNullOrEmpty(Course))
                {
                    // Validate course only if provided
                    string[] validCourses = { "CAS", "CBM", "CCJ", "COE", "CET", "CHTM", "CICT" };
                    if (!validCourses.Contains(Course))
                    {
                        return Json(new { success = false, message = $"Invalid course value '{Course}'. Valid values are: CAS, CBM, CCJ, COE, CET, CHTM, CICT" });
                    }
                    
                    updateFields.Add("Course = @Course");
                }
                
                if (!string.IsNullOrEmpty(Section))
                    updateFields.Add("Section = @Section");
                
                if (!string.IsNullOrEmpty(BadgeColor))
                    updateFields.Add("BadgeColor = @BadgeColor");
                
                // Only proceed if there are fields to update
                if (updateFields.Count == 0)
                {
                    return Json(new { success = false, message = "No changes were provided." });
                }
                
                // Create the SQL query with only the fields that need updating
                string query = $"UPDATE Students SET {string.Join(", ", updateFields)} WHERE IdNumber = @IdNumber";
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", IdNumber);
                    
                    if (!string.IsNullOrEmpty(FullName))
                        command.Parameters.AddWithValue("@FullName", FullName);
                    
                    command.Parameters.AddWithValue("@Username", Username);
                    
                    if (!string.IsNullOrEmpty(Password))
                        command.Parameters.AddWithValue("@Password", Password);
                    
                    if (!string.IsNullOrEmpty(Course))
                        command.Parameters.AddWithValue("@Course", Course);
                    
                    if (!string.IsNullOrEmpty(Section))
                        command.Parameters.AddWithValue("@Section", Section);
                    
                    if (!string.IsNullOrEmpty(BadgeColor))
                        command.Parameters.AddWithValue("@BadgeColor", BadgeColor);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true, message = "Student updated successfully." });
                    }
                    else
                    {
                        return Json(new { success = false, message = "No changes were made." });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
        }
    }

    // Add this method to delete a student
    [HttpPost]
    public async Task<IActionResult> DeleteStudent(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return Json(new { success = false, message = "Student ID is required" });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "DELETE FROM Students WHERE IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", id);
                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Student not found" });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> MarkEmployerMessagesAsRead(string employerId)
    {
        if (string.IsNullOrEmpty(employerId))
        {
            return Json(new { success = false, message = "Employer ID is required" });
        }

        string studentId = HttpContext.Session.GetString("IdNumber");
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "You must be logged in" });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // First, check how many unread messages we have
                string checkQuery = "SELECT COUNT(*) FROM EmployerStudentMessages WHERE StudentId = @StudentId AND EmployerId = @EmployerId AND IsFromEmployer = 1 AND IsRead = 0";
                int unreadCount = 0;
                
                using (var checkCommand = new SqlCommand(checkQuery, connection))
                {
                    checkCommand.Parameters.AddWithValue("@StudentId", studentId);
                    checkCommand.Parameters.AddWithValue("@EmployerId", employerId);
                    unreadCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                }
                
                // Now update the messages
                string updateQuery = "UPDATE EmployerStudentMessages SET IsRead = 1 WHERE StudentId = @StudentId AND EmployerId = @EmployerId AND IsFromEmployer = 1 AND IsRead = 0";

                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return Json(new { 
                        success = true, 
                        messagesRead = rowsAffected,
                        previousUnreadCount = unreadCount,
                        studentId = studentId,
                        employerId = employerId
                    });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public async Task<IActionResult> EmployerProfile()
    {
        try
        {
            // Get the employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            
            if (string.IsNullOrEmpty(employerId))
            {
                // If not available in session, try to get from claims
                var claimsIdentity = User.Identity as ClaimsIdentity;
                if (claimsIdentity != null)
                {
                    var userIdClaim = claimsIdentity.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);
                    if (userIdClaim != null)
                    {
                        employerId = userIdClaim.Value;
                    }
                }
                
                // If still not available, redirect to login
                if (string.IsNullOrEmpty(employerId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Store in session for future use
                HttpContext.Session.SetString("EmployerId", employerId);
            }
            
            string query = "SELECT EmployerId, FullName, Username, Company, Email, PhoneNumber, Description, ProfilePicturePath FROM Employers WHERE EmployerId = @EmployerId";
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            ViewBag.EmployerId = reader["EmployerId"].ToString();
                            ViewBag.FullName = reader["FullName"].ToString();
                            ViewBag.Username = reader["Username"].ToString();
                            ViewBag.Company = reader["Company"].ToString();
                            ViewBag.Email = reader["Email"]?.ToString() ?? string.Empty;
                            ViewBag.PhoneNumber = reader["PhoneNumber"]?.ToString() ?? string.Empty;
                            ViewBag.Description = reader["Description"]?.ToString() ?? string.Empty;
                            ViewBag.ProfilePicturePath = reader["ProfilePicturePath"]?.ToString() ?? string.Empty;
                        }
                        else
                        {
                            return RedirectToAction("Login", "Home");
                        }
                    }
                }
            }
            
            return View();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in EmployerProfile: {ex.Message}");
            return RedirectToAction("Error", "Home");
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> SaveEmployerProfilePicture([FromBody] ProfilePictureModel model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.Base64Image))
            {
                return Json(new { success = false, message = "No image data received." });
            }

            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            // Convert base64 string to bytes
            byte[] imageBytes = Convert.FromBase64String(model.Base64Image);

            // Create uploads directory if it doesn't exist
            string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "employers");
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }

            // Generate unique filename
            string fileName = $"{employerId}_{DateTime.Now.Ticks}.jpg";
            string filePath = Path.Combine(uploadsDir, fileName);

            // Save the image file
            await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

            // Update the database with the new profile picture path
            string relativePath = $"/uploads/employers/{fileName}";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string query = "UPDATE Employers SET ProfilePicturePath = @ProfilePicturePath WHERE EmployerId = @EmployerId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProfilePicturePath", relativePath);
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true, imageUrl = relativePath });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> UpdateEmployerProfile([FromBody] UpdateEmployerProfileModel model)
    {
        try
        {
            if (model == null)
            {
                return Json(new { success = false, message = "No data received." });
            }

            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string query = @"UPDATE Employers 
                               SET FullName = @FullName, 
                                   Username = @Username, 
                                   Company = @Company, 
                                   Email = @Email, 
                                   PhoneNumber = @PhoneNumber, 
                                   Description = @Description
                               WHERE EmployerId = @EmployerId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@Username", model.Username);
                    command.Parameters.AddWithValue("@Company", model.Company);
                    command.Parameters.AddWithValue("@Email", model.Email ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@PhoneNumber", model.PhoneNumber ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Description", model.Description ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    
                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> UpdateEmployerProfileForm(IFormCollection form)
    {
        try
        {
            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                TempData["Error"] = "User not authenticated. Please log in again.";
                return RedirectToAction("Login", "Home");
            }

            string fullName = form["FullName"];
            string username = form["Username"];
            string password = form["Password"];
            string companyName = form["CompanyName"];
            string email = form["Email"];
            string phoneNumber = form["PhoneNumber"];
            string description = form["Description"];

            // Handle profile picture upload if provided
            string profilePicturePath = null;
            
            // Check for base64 image data first
            if (!string.IsNullOrEmpty(form["ProfilePictureBase64"]))
            {
                string base64Image = form["ProfilePictureBase64"];
                
                // Extract the base64 data from the data URL format
                if (base64Image.StartsWith("data:image"))
                {
                    // Remove the data URI prefix
                    int commaIndex = base64Image.IndexOf(',');
                    if (commaIndex != -1)
                    {
                        string fileType = base64Image.Substring(5, commaIndex - 5);
                        string extension = ".jpg"; // Default to jpg
                        
                        // Try to determine the file extension from the data URL
                        if (fileType.Contains("png"))
                            extension = ".png";
                        else if (fileType.Contains("gif"))
                            extension = ".gif";
                        else if (fileType.Contains("jpeg") || fileType.Contains("jpg"))
                            extension = ".jpg";
                        
                        // Get the actual base64 string
                        base64Image = base64Image.Substring(commaIndex + 1);
                        
                        // Convert base64 to bytes
                        byte[] imageBytes = Convert.FromBase64String(base64Image);
                        
                        // Create uploads directory if it doesn't exist
                        string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "employers");
                        if (!Directory.Exists(uploadsDir))
                        {
                            Directory.CreateDirectory(uploadsDir);
                        }
                        
                        // Generate unique filename
                        string fileName = $"{employerId}_{DateTime.Now.Ticks}{extension}";
                        string filePath = Path.Combine(uploadsDir, fileName);
                        
                        // Save the file
                        await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                        
                        // Set the relative path for database
                        profilePicturePath = $"/uploads/employers/{fileName}";
                    }
                }
            }
            // If no base64 image, check for file upload
            else if (form.Files.Count > 0 && form.Files["ProfilePicture"] != null)
            {
                var profilePicture = form.Files["ProfilePicture"];
                if (profilePicture.Length > 0)
                {
                    // Create uploads directory if it doesn't exist
                    string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "employers");
                    if (!Directory.Exists(uploadsDir))
                    {
                        Directory.CreateDirectory(uploadsDir);
                    }

                    // Generate unique filename
                    string fileName = $"{employerId}_{DateTime.Now.Ticks}{Path.GetExtension(profilePicture.FileName)}";
                    string filePath = Path.Combine(uploadsDir, fileName);

                    // Save the file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await profilePicture.CopyToAsync(stream);
                    }

                    // Set the relative path for database
                    profilePicturePath = $"/uploads/employers/{fileName}";
                }
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                
                // Construct the SQL query dynamically based on which fields are provided
                StringBuilder queryBuilder = new StringBuilder("UPDATE Employers SET ");
                List<string> updateFields = new List<string>();
                SqlCommand command = new SqlCommand();
                
                // Only add fields that were provided
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    updateFields.Add("FullName = @FullName");
                    command.Parameters.AddWithValue("@FullName", fullName);
                }
                
                if (!string.IsNullOrWhiteSpace(username))
                {
                    updateFields.Add("Username = @Username");
                    command.Parameters.AddWithValue("@Username", username);
                }
                
                if (!string.IsNullOrWhiteSpace(password))
                {
                    updateFields.Add("Password = @Password");
                    command.Parameters.AddWithValue("@Password", password);
                }
                
                if (!string.IsNullOrWhiteSpace(companyName))
                {
                    updateFields.Add("Company = @Company");
                    command.Parameters.AddWithValue("@Company", companyName);
                }
                
                if (!string.IsNullOrWhiteSpace(email))
                {
                    updateFields.Add("Email = @Email");
                    command.Parameters.AddWithValue("@Email", email);
                }
                else
                {
                    updateFields.Add("Email = NULL");
                }
                
                if (!string.IsNullOrWhiteSpace(phoneNumber))
                {
                    updateFields.Add("PhoneNumber = @PhoneNumber");
                    command.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                }
                else
                {
                    updateFields.Add("PhoneNumber = NULL");
                }
                
                if (!string.IsNullOrWhiteSpace(description))
                {
                    updateFields.Add("Description = @Description");
                    command.Parameters.AddWithValue("@Description", description);
                }
                else
                {
                    updateFields.Add("Description = NULL");
                }
                
                // Add profile picture update if it was uploaded
                if (!string.IsNullOrEmpty(profilePicturePath))
                {
                    updateFields.Add("ProfilePicturePath = @ProfilePicturePath");
                    command.Parameters.AddWithValue("@ProfilePicturePath", profilePicturePath);
                }
                
                // If no fields to update, just return success
                if (updateFields.Count == 0)
                {
                    TempData["Success"] = "No changes made to your profile.";
                    return RedirectToAction("EmployerProfile");
                }
                
                // Complete the query
                queryBuilder.Append(string.Join(", ", updateFields));
                queryBuilder.Append(" WHERE EmployerId = @EmployerId");
                
                // Add the employer ID parameter
                command.Parameters.AddWithValue("@EmployerId", employerId);
                
                // Set the command text and connection
                command.CommandText = queryBuilder.ToString();
                command.Connection = connection;
                
                // Execute the query
                await command.ExecuteNonQueryAsync();
            }

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("EmployerProfile");
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("EmployerProfile");
        }
    }
    
    [HttpPost]
    public async Task<IActionResult> ChangeEmployerPassword([FromBody] ChangePasswordModel model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(model.NewPassword))
            {
                return Json(new { success = false, message = "Invalid password data." });
            }

            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            // Verify current password
            bool isPasswordCorrect = false;
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT Password FROM Employers WHERE EmployerId = @EmployerId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    var storedPassword = await command.ExecuteScalarAsync() as string;
                    isPasswordCorrect = (storedPassword == model.CurrentPassword);
                }

                if (!isPasswordCorrect)
                {
                    return Json(new { success = false, message = "Current password is incorrect." });
                }

                // Update to new password
                string updateQuery = "UPDATE Employers SET Password = @NewPassword WHERE EmployerId = @EmployerId";
                using (SqlCommand command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@NewPassword", model.NewPassword);
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployerProfile(string employerId)
    {
        if (string.IsNullOrEmpty(employerId))
        {
            return Json(new { success = false, message = "Employer ID not provided." });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT EmployerId, FullName, Company, Email, PhoneNumber, 
                           Description, ProfilePicturePath
                    FROM Employers 
                    WHERE EmployerId = @EmployerId";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Handle profile picture
                            string profilePicture = null;
                            if (reader["ProfilePicturePath"] != DBNull.Value && !string.IsNullOrEmpty(reader["ProfilePicturePath"].ToString()))
                            {
                                profilePicture = reader["ProfilePicturePath"].ToString();
                            }

                            return Json(new
                            {
                                success = true,
                                employerId = reader["EmployerId"].ToString(),
                                fullName = reader["FullName"].ToString(),
                                company = reader["Company"].ToString(),
                                email = reader["Email"] != DBNull.Value ? reader["Email"].ToString() : "",
                                phoneNumber = reader["PhoneNumber"] != DBNull.Value ? reader["PhoneNumber"].ToString() : "",
                                description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : "",
                                profilePicture = profilePicture
                            });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Employer profile not found." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error: {ex.Message}" });
        }
    }

    // This method gets all employers a student has chatted with for the Recent Conversations panel
    [HttpGet]
    public async Task<IActionResult> GetStudentChats(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID not provided." });
            }
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT DISTINCT m.EmployerId, e.FullName as EmployerName, e.Company, e.ProfilePicturePath,
                           (SELECT TOP 1 Message FROM EmployerStudentMessages 
                            WHERE EmployerId = m.EmployerId AND StudentId = m.StudentId 
                            ORDER BY SentTime DESC) as LastMessage,
                           (SELECT TOP 1 SentTime FROM EmployerStudentMessages 
                            WHERE EmployerId = m.EmployerId AND StudentId = m.StudentId 
                            ORDER BY SentTime DESC) as LastMessageTime
                    FROM EmployerStudentMessages m
                    JOIN Employers e ON m.EmployerId = e.EmployerId
                    WHERE m.StudentId = @StudentId
                    ORDER BY LastMessageTime DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    var chats = new List<object>();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Handle profile picture
                            string profilePicture = null;
                            if (reader["ProfilePicturePath"] != DBNull.Value && !string.IsNullOrEmpty(reader["ProfilePicturePath"].ToString()))
                            {
                                profilePicture = reader["ProfilePicturePath"].ToString();
                            }

                            chats.Add(new
                            {
                                employerId = reader["EmployerId"].ToString(),
                                employerName = reader["EmployerName"].ToString(),
                                company = reader["Company"].ToString(),
                                profilePicture = profilePicture,
                                lastMessage = reader["LastMessage"] != DBNull.Value ? reader["LastMessage"].ToString() : null,
                                lastMessageTime = reader["LastMessageTime"] != DBNull.Value ? Convert.ToDateTime(reader["LastMessageTime"]) : DateTime.MinValue
                            });
                        }
                    }

                    return Json(new { success = true, chats = chats });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving chats: " + ex.Message });
        }
    }

    // This method retrieves detailed employer information for the student chat interface
    [HttpGet]
    public async Task<IActionResult> GetEmployerDetails(string employerId)
    {
        if (string.IsNullOrEmpty(employerId))
        {
            return Json(new { success = false, message = "Employer ID not provided." });
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    SELECT EmployerId, FullName, Company, Email, PhoneNumber, Description, ProfilePicturePath
                    FROM Employers
                    WHERE EmployerId = @EmployerId";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Handle profile picture
                            string profilePicture = null;
                            if (reader["ProfilePicturePath"] != DBNull.Value && !string.IsNullOrEmpty(reader["ProfilePicturePath"].ToString()))
                            {
                                profilePicture = reader["ProfilePicturePath"].ToString();
                            }

                            var employer = new
                            {
                                employerId = reader["EmployerId"].ToString(),
                                fullName = reader["FullName"].ToString(),
                                company = reader["Company"].ToString(),
                                email = reader["Email"] != DBNull.Value ? reader["Email"].ToString() : null,
                                phoneNumber = reader["PhoneNumber"] != DBNull.Value ? reader["PhoneNumber"].ToString() : null,
                                description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null,
                                profilePicturePath = profilePicture
                            };

                            return Json(new { success = true, employer = employer });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Employer not found." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error retrieving employer details: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ChangeStudentPassword([FromBody] ChangePasswordModel model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.CurrentPassword) || string.IsNullOrEmpty(model.NewPassword))
            {
                return Json(new { success = false, message = "Invalid password data." });
            }

            // Get student ID from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student not authenticated." });
            }

            // Verify current password
            bool isPasswordCorrect = false;
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT Password FROM Students WHERE IdNumber = @IdNumber";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    var storedPassword = await command.ExecuteScalarAsync() as string;
                    isPasswordCorrect = (storedPassword == model.CurrentPassword);
                }

                if (!isPasswordCorrect)
                {
                    return Json(new { success = false, message = "Current password is incorrect." });
                }

                // Update to new password
                string updateQuery = "UPDATE Students SET Password = @NewPassword WHERE IdNumber = @IdNumber";
                using (SqlCommand command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@NewPassword", model.NewPassword);
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    await command.ExecuteNonQueryAsync();
                }
            }

            return Json(new { success = true, message = "Password changed successfully." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error changing password: " + ex.Message });
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
    public string EmployerId { get; set; }
    public string Message { get; set; }
}

public class MessageViewModel
{
    public string Content { get; set; }
    public DateTime SentTime { get; set; }
    public bool IsFromEmployer { get; set; }
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