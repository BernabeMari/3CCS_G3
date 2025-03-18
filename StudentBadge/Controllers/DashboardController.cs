using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using StudentBadge.Data;
using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;

public class DashboardController : Controller
{
    private readonly string _connectionString;

    public DashboardController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("YourConnectionString");
    }

    public async Task<IActionResult> StudentDashboard()
    {
        ViewBag.FullName = HttpContext.Session.GetString("FullName");
        ViewBag.IdNumber = HttpContext.Session.GetString("IdNumber");
        ViewBag.Course = HttpContext.Session.GetString("Course");
        ViewBag.Section = HttpContext.Session.GetString("Section");

        // Get the current student's score and profile picture
        await GetCurrentStudentScoreAndPicture();

        var allStudents = await GetAllStudentsWithDetails();

        // Add the list of students to ViewBag to be accessed in the view
        ViewBag.AllStudents = allStudents;

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
            string query = "SELECT Score, ProfilePicturePath, ResumeFileName FROM Students WHERE IdNumber = @IdNumber";

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

                        // Get resume path
                        string resumeFileName = reader["ResumeFileName"] as string;
                        ViewBag.ResumePath = !string.IsNullOrEmpty(resumeFileName) ? Url.Action("GetResume", "Dashboard") : null;
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
                        IdNumber = reader["IdNumber"].ToString(),
                        FullName = reader["FullName"].ToString(),
                        Course = reader["Course"].ToString(),
                        Section = reader["Section"].ToString(),
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
                                IdNumber = reader["IdNumber"].ToString(),
                                FullName = reader["FullName"].ToString(),
                                Course = reader["Course"].ToString(),
                                Section = reader["Section"].ToString(),
                                ProfilePicturePath = reader["ProfilePicturePath"]?.ToString(),
                                Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0
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
        Console.WriteLine("Received SendStudentMessage request");
        Console.WriteLine($"Message model: {JsonConvert.SerializeObject(model)}");

        if (model == null || string.IsNullOrEmpty(model.Message))
        {
            Console.WriteLine("Invalid message data received");
            return Json(new { success = false, message = "No message content received." });
        }

        if (string.IsNullOrEmpty(model.EmployerId))
        {
            Console.WriteLine("No employer ID received");
            return Json(new { success = false, message = "No employer ID received." });
        }

        try
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                Console.WriteLine("Student not authenticated");
                return Json(new { success = false, message = "Student not authenticated." });
            }

            Console.WriteLine($"Processing message from student {studentId} to employer {model.EmployerId}");

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = @"
                    INSERT INTO EmployerStudentMessages (EmployerId, StudentId, Message, IsFromEmployer, SentTime, IsRead) 
                    VALUES (@EmployerId, @StudentId, @Message, 0, @SentTime, 0)";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@EmployerId", model.EmployerId);
                    command.Parameters.AddWithValue("@Message", model.Message);
                    command.Parameters.AddWithValue("@SentTime", DateTime.UtcNow);

                    Console.WriteLine("Executing SQL query...");
                    await command.ExecuteNonQueryAsync();
                    Console.WriteLine("Message inserted successfully");

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
            Console.WriteLine($"Error sending message: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
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