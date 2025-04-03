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

public class DashboardController : Controller
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DashboardController> _logger;
    private readonly IWebHostEnvironment _hostingEnvironment;

    public DashboardController(IConfiguration configuration, ILogger<DashboardController> logger, IWebHostEnvironment hostingEnvironment)
    {
        _configuration = configuration;
        _logger = logger;
        _hostingEnvironment = hostingEnvironment;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(_connectionString))
        {
            _logger.LogError("Connection string 'DefaultConnection' not found in configuration.");
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found in configuration.");
        }
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
                                ViewBag.ProfilePicturePath = Url.Action("GetProfilePicture", "Dashboard", 
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
                                Url.Action("GetResume", "Dashboard", new { studentId = idNumber, t = DateTime.Now.Ticks }) : null;
                            
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
                                    Url.Action("GetResume", "Dashboard", new { studentId = idNumber }) : null;
                                
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
            
            string query = @"
                SELECT sd.IdNumber, u.FullName, sd.Course, sd.Section, 
                       sd.IsProfileVisible, sd.ProfilePicturePath, sd.ResumeFileName, 
                       sd.Score, sd.Achievements, sd.Comments, sd.BadgeColor, sd.IsResumeVisible 
                FROM StudentDetails sd
                JOIN Users u ON sd.UserId = u.UserId
                WHERE u.Role = 'student'
                ORDER BY sd.Score DESC";

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
            string contentType = "image/jpeg";

            // Handle the case when the full data URL is sent
            if (base64Image.Contains(","))
            {
                // Extract content type and base64 data
                string[] parts = base64Image.Split(',');
                if (parts.Length == 2)
                {
                    // Store the content type from the data URL
                    if (parts[0].Contains(":") && parts[0].Contains(";"))
                    {
                        contentType = parts[0].Split(':')[1].Split(';')[0];
                    }
                    // Just save the base64 part without the data URL prefix
                    base64Image = parts[1];
                }
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

            // Convert base64 to bytes
            byte[] imageBytes = Convert.FromBase64String(base64Image);
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the new database structure is being used and if binary columns exist
                bool usingNewBinaryColumns = false;
                string checkColumnQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ProfilePictureData'";
                
                using (var command = new SqlCommand(checkColumnQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    usingNewBinaryColumns = (count > 0);
                }
                
                string query;
                int rowsAffected = 0;
                
                if (usingNewBinaryColumns)
                {
                    // Create metadata JSON
                    string metadata = JsonConvert.SerializeObject(new
                    {
                        ContentType = contentType,
                        UploadDate = DateTime.UtcNow,
                        Source = "binary"
                    });
                    
                    // Update StudentDetails table with binary data and metadata
                    query = @"
                        UPDATE StudentDetails 
                        SET ProfilePictureData = @ProfilePictureData,
                            ProfileMetadata = @ProfileMetadata,
                            ProfilePicturePath = @ProfilePicturePath
                        WHERE IdNumber = @IdNumber";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        // Store the binary image data
                        command.Parameters.AddWithValue("@ProfilePictureData", imageBytes);
                        command.Parameters.AddWithValue("@ProfileMetadata", metadata);
                        
                        // Keep a reference to content type in the path for backwards compatibility
                        string dataUrl = $"data:{contentType};base64,{base64Image.Substring(0, Math.Min(100, base64Image.Length))}...";
                        command.Parameters.AddWithValue("@ProfilePicturePath", dataUrl);
                        
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        rowsAffected = await command.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Fall back to the old method of storing as a file
                    // Create a unique filename
                    string extension = ".jpg";
                    if (contentType == "image/png") extension = ".png";
                    else if (contentType == "image/gif") extension = ".gif";
                    
                    string fileName = $"{idNumber}_{DateTime.Now.Ticks}{extension}";
                    
                    // Define the directory path
                    string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");
                    
                    // Ensure directory exists
                    if (!Directory.Exists(uploadsDir))
                    {
                        Directory.CreateDirectory(uploadsDir);
                    }
                    
                    // Full path to save the file
                    string filePath = Path.Combine(uploadsDir, fileName);
                    
                    // Save the file
                    await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                    
                    // URL to access the file
                    string fileUrl = $"/uploads/profiles/{fileName}";
                    
                    // Check if we should use StudentDetails or Students table
                    bool useStudentDetails = false;
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'StudentDetails'";
                    
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        useStudentDetails = (count > 0);
                    }
                    
                    if (useStudentDetails)
                    {
                        // Update StudentDetails table in the new structure
                        query = @"
                            UPDATE StudentDetails 
                            SET ProfilePicturePath = @ProfilePicturePath
                            WHERE IdNumber = @IdNumber";
                    }
                    else
                    {
                        // Update Students table in the old structure
                        query = @"
                            UPDATE Students 
                            SET ProfilePicturePath = @ProfilePicturePath
                            WHERE IdNumber = @IdNumber";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        // Store just the file path
                        command.Parameters.AddWithValue("@ProfilePicturePath", fileUrl);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        rowsAffected = await command.ExecuteNonQueryAsync();
                    }
                }

                if (rowsAffected > 0)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Profile picture saved successfully.",
                        imageUrl = $"/Dashboard/GetProfilePicture?studentId={idNumber}&t={DateTime.Now.Ticks}" // Use timestamp to prevent caching
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to update profile picture. User not found." });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error saving profile picture: " + ex.Message });
        }
    }
    
    // Create an extremely small thumbnail to fit in database column
    private string CreateTinyThumbnail(string base64Image)
    {
        try
        {
            // Convert base64 to byte array
            byte[] imageBytes = Convert.FromBase64String(base64Image);
            
            using (var ms = new MemoryStream(imageBytes))
            {
                using (var image = Image.FromStream(ms))
                {
                    // Create a very small thumbnail (80x80) to fit in database
                    int size = 80;
                    int width, height;
                    
                    // Calculate aspect ratio to maintain proportions
                    if (image.Width > image.Height)
                    {
                        width = size;
                        height = (int)(image.Height * ((float)size / image.Width));
                    }
                    else
                    {
                        height = size;
                        width = (int)(image.Width * ((float)size / image.Height));
                    }
                    
                    using (var thumbnail = new Bitmap(width, height))
                    {
                        using (var g = Graphics.FromImage(thumbnail))
                        {
                            // Low quality for small file size
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                            
                            g.DrawImage(image, 0, 0, width, height);
                        }
                        
                        using (var outStream = new MemoryStream())
                        {
                            // Extremely low quality JPEG (15%) to keep size small
                            var jpegEncoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                                .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                            
                            var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                            encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                                System.Drawing.Imaging.Encoder.Quality, 15L);
                            
                            thumbnail.Save(outStream, jpegEncoder, encoderParams);
                            return Convert.ToBase64String(outStream.ToArray());
                        }
                    }
                }
            }
        }
        catch
        {
            try
            {
                // Ultimate fallback - create a tiny 40x40 image
                byte[] imageBytes = Convert.FromBase64String(base64Image);
                using (var ms = new MemoryStream(imageBytes))
                {
                    using (var image = Image.FromStream(ms))
                    {
                        using (var thumbnail = new Bitmap(40, 40))
                        {
                            using (var g = Graphics.FromImage(thumbnail))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                                g.DrawImage(image, 0, 0, 40, 40);
                            }
                            
                            using (var outStream = new MemoryStream())
                            {
                                // Super low quality (10%)
                                var jpegEncoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                                    .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                                
                                var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                                encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                                    System.Drawing.Imaging.Encoder.Quality, 10L);
                                
                                thumbnail.Save(outStream, jpegEncoder, encoderParams);
                                return Convert.ToBase64String(outStream.ToArray());
                            }
                        }
                    }
                }
            }
            catch
            {
                // If all else fails, return a tiny 1x1 pixel image
                return "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";
            }
        }
    }

    // Save resume with improved error handling and transaction management
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
            string contentType = "application/pdf"; // Default content type

            // Handle the case when the full data URL is sent
            if (base64File.Contains(","))
            {
                // Extract content type and base64 data
                string[] parts = base64File.Split(',');
                if (parts.Length == 2)
                {
                    // Store the content type from the data URL
                    if (parts[0].Contains(":") && parts[0].Contains(";"))
                    {
                        contentType = parts[0].Split(':')[1].Split(';')[0];
                    }
                    base64File = parts[1];
                }
            }

            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            // Convert base64 to bytes
            byte[] fileBytes = Convert.FromBase64String(base64File);
            
            // Use file storage approach to prevent transaction log errors
            string fileReference = $"Resume_{idNumber}_{DateTime.Now.Ticks}";
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = contentType == "application/pdf" ? ".pdf" : ".doc";
            }
            
            // Save file to disk
            string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "resumes");
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }
            
            string uniqueFileName = fileReference + extension;
            string filePath = Path.Combine(uploadsDir, uniqueFileName);
            await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);
            
            // URL path to access the file
            string fileUrl = $"/uploads/resumes/{uniqueFileName}";
            
            // Store reference in the database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if we're using the binary columns
                bool hasBinaryColumns = false;
                string checkColumnQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ResumeData'";
                
                using (var command = new SqlCommand(checkColumnQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    hasBinaryColumns = (count > 0);
                }
                
                // Check if OriginalResumeFileName column exists
                bool hasOriginalFileNameColumn = false;
                string checkOriginalFileNameColumnQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'OriginalResumeFileName'";
                
                using (var command = new SqlCommand(checkOriginalFileNameColumnQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    hasOriginalFileNameColumn = (count > 0);
                }
                
                int rowsAffected = 0;
                
                // Begin transaction to ensure data consistency
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        if (hasBinaryColumns)
                        {
                            // Create metadata for the file reference
                            string metadata = JsonConvert.SerializeObject(new { 
                                ContentType = contentType, 
                                UploadDate = DateTime.UtcNow,
                                Source = "file",
                                StoragePath = fileUrl
                            });
                            
                            // Build the SQL query based on whether OriginalResumeFileName exists
                            string query;
                            if (hasOriginalFileNameColumn)
                            {
                                query = @"
                                    UPDATE StudentDetails 
                                    SET ResumeData = NULL,
                                        ResumeMetadata = @ResumeMetadata,
                                        ResumeFileName = @ResumeFileName,
                                        OriginalResumeFileName = @OriginalFileName
                                    WHERE IdNumber = @IdNumber";
                            }
                            else
                            {
                                query = @"
                                    UPDATE StudentDetails 
                                    SET ResumeData = NULL,
                                        ResumeMetadata = @ResumeMetadata,
                                        ResumeFileName = @ResumeFileName
                                    WHERE IdNumber = @IdNumber";
                            }
                            
                            using (var command = new SqlCommand(query, connection, transaction))
                            {
                                // Important: Not storing actual file data in the database
                                command.Parameters.AddWithValue("@ResumeMetadata", metadata);
                                command.Parameters.AddWithValue("@ResumeFileName", fileUrl);
                                
                                if (hasOriginalFileNameColumn)
                                {
                                    command.Parameters.AddWithValue("@OriginalFileName", fileName);
                                }
                                
                                command.Parameters.AddWithValue("@IdNumber", idNumber);
                                rowsAffected = await command.ExecuteNonQueryAsync();
                            }
                        }
                        else
                        {
                            // Fall back to the appropriate table
                            bool usingNewTables = false;
                            string checkTableQuery = @"
                                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                                WHERE TABLE_NAME = 'StudentDetails'";
                            
                            using (var command = new SqlCommand(checkTableQuery, connection, transaction))
                            {
                                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                usingNewTables = (count > 0);
                            }
                            
                            string query;
                            
                            if (usingNewTables)
                            {
                                if (hasOriginalFileNameColumn)
                                {
                                    query = @"UPDATE StudentDetails SET ResumeFileName = @ResumeUrl, OriginalResumeFileName = @OriginalFileName WHERE IdNumber = @IdNumber";
                                }
                                else
                                {
                                    query = @"UPDATE StudentDetails SET ResumeFileName = @ResumeUrl WHERE IdNumber = @IdNumber";
                                }
                                
                                using (var command = new SqlCommand(query, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@ResumeUrl", fileUrl);
                                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                                    
                                    if (hasOriginalFileNameColumn)
                                    {
                                        command.Parameters.AddWithValue("@OriginalFileName", fileName);
                                    }
                                    
                                    rowsAffected = await command.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                // Old database structure
                                query = @"UPDATE Students SET ResumeFileName = @ResumeUrl WHERE IdNumber = @IdNumber";
                                
                                using (var command = new SqlCommand(query, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@ResumeUrl", fileUrl);
                                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                                    rowsAffected = await command.ExecuteNonQueryAsync();
                                }
                            }
                        }
                        
                        // Commit the transaction
                        transaction.Commit();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new
                            {
                                success = true,
                                message = "Resume uploaded successfully.",
                                resumeUrl = fileUrl + $"?t={DateTime.Now.Ticks}", // Add timestamp to prevent caching
                                fileName = fileName
                            });
                        }
                        
                        return Json(new { success = false, message = "Failed to update resume. User not found." });
                    }
                    catch (Exception ex)
                    {
                        // Rollback transaction on error
                        transaction.Rollback();
                        
                        // Try to delete the file if database update failed
                        try { System.IO.File.Delete(filePath); } catch { }
                        
                        return Json(new { success = false, message = "Database error: " + ex.Message });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error saving resume: " + ex.Message });
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
                
                // Check if we're using the new tables
                bool usingNewTables = false;
                string checkTableQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'EmployerStudentMessages'";
                
                using (var command = new SqlCommand(checkTableQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    usingNewTables = (count > 0);
                }
                
                string query;
                
                if (usingNewTables)
                {
                    query = @"
                        SELECT DISTINCT 
                            m.StudentId,
                            u.FullName as StudentName,
                            m.Message,
                            m.SentTime,
                            m.IsRead,
                            m.IsFromEmployer
                        FROM EmployerStudentMessages m
                        JOIN StudentDetails sd ON m.StudentId = sd.IdNumber
                        JOIN Users u ON sd.UserId = u.UserId
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
            _logger.LogError(ex, "Error getting student messages");
            return Json(new { success = false, message = "Failed to load messages. Please try again." });
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

    // Add teacher management methods
    [HttpPost]
    public async Task<IActionResult> AddTeacher(string fullName, string username, string password, string department, string position)
    {
        try
        {
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username) || 
                string.IsNullOrEmpty(password) || string.IsNullOrEmpty(department) || 
                string.IsNullOrEmpty(position))
            {
                TempData["Error"] = "All fields are required.";
                return RedirectToAction("AdminDashboard");
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if using new database structure
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
                    // Using new database structure
                    
                    // Check if username already exists
                    string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                    using (var command = new SqlCommand(checkUsernameQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        
                        if (count > 0)
                        {
                            TempData["Error"] = "Username already exists.";
                            return RedirectToAction("AdminDashboard");
                        }
                    }
                    
                    // Generate a unique TeacherId
                    string teacherId = $"TCH{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                    
                    // Insert into Users table first
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
                        command.Parameters.AddWithValue("@Department", department);
                        command.Parameters.AddWithValue("@Position", position);
                        
                        await command.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Using old database structure
                    
                    // Check if username already exists
                    string checkUsernameQuery = "SELECT COUNT(*) FROM dbo.Teachers WHERE Username = @Username";
                    using (var command = new SqlCommand(checkUsernameQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        
                        if (count > 0)
                        {
                            TempData["Error"] = "Username already exists.";
                            return RedirectToAction("AdminDashboard");
                        }
                    }
                    
                    // Generate a unique TeacherId
                    string teacherId = $"TCH{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                    
                    // Verify the generated ID is unique
                    string checkIdQuery = "SELECT COUNT(*) FROM dbo.Teachers WHERE TeacherId = @TeacherId";
                    using (var command = new SqlCommand(checkIdQuery, connection))
                    {
                        bool isUnique = false;
                        while (!isUnique)
                        {
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@TeacherId", teacherId);
                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            
                            if (count == 0)
                            {
                                isUnique = true;
                            }
                            else
                            {
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
                        command.Parameters.AddWithValue("@Department", department);
                        command.Parameters.AddWithValue("@Position", position);
                        
                        await command.ExecuteNonQueryAsync();
                    }
                }
                
                TempData["Success"] = "Teacher added successfully.";
            }
            
            return RedirectToAction("AdminDashboard");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Error adding teacher: " + ex.Message;
            return RedirectToAction("AdminDashboard");
        }
    }

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
            // Make sure to register the EPPlus license context at application startup
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;

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

                        // Skip header row (row 1)
                        for (int row = 2; row <= rowCount; row++)
                        {
                            try
                            {
                                string fullName = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                                string username = worksheet.Cells[row, 2].Value?.ToString()?.Trim();
                                string password = worksheet.Cells[row, 3].Value?.ToString()?.Trim();
                                string idNumber = worksheet.Cells[row, 4].Value?.ToString()?.Trim();
                                string course = worksheet.Cells[row, 5].Value?.ToString()?.Trim();
                                string section = worksheet.Cells[row, 6].Value?.ToString()?.Trim();
                                string email = worksheet.Cells[row, 7].Value?.ToString()?.Trim();
                                string phoneNumber = worksheet.Cells[row, 8].Value?.ToString()?.Trim();

                                // Validate required fields
                                if (string.IsNullOrEmpty(fullName) || 
                                    string.IsNullOrEmpty(username) || 
                                    string.IsNullOrEmpty(password) ||
                                    string.IsNullOrEmpty(idNumber) || 
                                    string.IsNullOrEmpty(course))
                                {
                                    errors.Add($"Row {row}: Missing required fields (Full Name, Username, Password, ID Number, or Course)");
                                    errorCount++;
                                    continue;
                                }

                                if (usingNewTables)
                                {
                                    // Check if username already exists in Users table
                                    string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                                    using (var command = new SqlCommand(checkUsernameQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@Username", username);
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        
                                        if (count > 0)
                                        {
                                            errors.Add($"Row {row}: Username {username} already exists");
                                            errorCount++;
                                            continue;
                                        }
                                    }

                                    // Check if ID number already exists in StudentDetails table
                                    string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @IdNumber";
                                    using (var command = new SqlCommand(checkIdQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        
                                        if (count > 0)
                                        {
                                            errors.Add($"Row {row}: Student with ID {idNumber} already exists");
                                            errorCount++;
                                            continue;
                                        }
                                    }

                                    // Insert into Users table
                                    string userId = Guid.NewGuid().ToString();
                                    string insertUserQuery = @"
                                        INSERT INTO Users (UserId, FullName, Username, Password, Role, Email, PhoneNumber) 
                                        VALUES (@UserId, @FullName, @Username, @Password, 'student', @Email, @PhoneNumber)";
                                    
                                    using (var command = new SqlCommand(insertUserQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@UserId", userId);
                                        command.Parameters.AddWithValue("@FullName", fullName);
                                        command.Parameters.AddWithValue("@Username", username);
                                        command.Parameters.AddWithValue("@Password", password);
                                        command.Parameters.AddWithValue("@Email", email ?? (object)DBNull.Value);
                                        command.Parameters.AddWithValue("@PhoneNumber", phoneNumber ?? (object)DBNull.Value);
                                        
                                        await command.ExecuteNonQueryAsync();
                                    }

                                    // Insert into StudentDetails table
                                    string insertStudentQuery = @"
                                        INSERT INTO StudentDetails (UserId, IdNumber, Course, Section, Score, BadgeColor, IsProfileVisible, IsResumeVisible)
                                        VALUES (@UserId, @IdNumber, @Course, @Section, 0, 'green', 1, 1)";
                                    
                                    using (var command = new SqlCommand(insertStudentQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@UserId", userId);
                                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                                        command.Parameters.AddWithValue("@Course", course);
                                        command.Parameters.AddWithValue("@Section", section ?? "");
                                        await command.ExecuteNonQueryAsync();
                                    }
                                }
                                else
                                {
                                    // Using old Students table
                                    
                                    // Check if student with this ID already exists
                                    string checkQuery = "SELECT COUNT(*) FROM Students WHERE IdNumber = @IdNumber";
                                    using (var command = new SqlCommand(checkQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        
                                        if (count > 0)
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
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        
                                        if (count > 0)
                                        {
                                            errors.Add($"Row {row}: Username {username} already exists");
                                            errorCount++;
                                            continue;
                                        }
                                    }

                                    // Insert into Students table
                                    string insertQuery = @"
                                        INSERT INTO Students (IdNumber, FullName, Username, Password, Course, Section, Score, BadgeColor, IsProfileVisible, IsResumeVisible)
                                        VALUES (@IdNumber, @FullName, @Username, @Password, @Course, @Section, 0, 'green', 1, 1)";
                                    
                                    using (var command = new SqlCommand(insertQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                                        command.Parameters.AddWithValue("@FullName", fullName);
                                        command.Parameters.AddWithValue("@Username", username);
                                        command.Parameters.AddWithValue("@Password", password);
                                        command.Parameters.AddWithValue("@Course", course);
                                        command.Parameters.AddWithValue("@Section", section ?? "");
                                        await command.ExecuteNonQueryAsync();
                                    }
                                }

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Row {row}: {ex.Message}");
                                errorCount++;
                            }
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
            TempData["Error"] = $"Error importing students: {ex.Message}";
            return RedirectToAction("AdminDashboard");
        }
    }

    [HttpPost]
    public async Task<IActionResult> ImportTeachers(IFormFile file)
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
            // Make sure to register the EPPlus license context at application startup
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;

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

                        // Skip header row (row 1)
                        for (int row = 2; row <= rowCount; row++)
                        {
                            try
                            {
                                string fullName = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                                string username = worksheet.Cells[row, 2].Value?.ToString()?.Trim();
                                string password = worksheet.Cells[row, 3].Value?.ToString()?.Trim();
                                string department = worksheet.Cells[row, 4].Value?.ToString()?.Trim();
                                string position = worksheet.Cells[row, 5].Value?.ToString()?.Trim();

                                // Validate required fields
                                if (string.IsNullOrEmpty(fullName) || 
                                    string.IsNullOrEmpty(username) || 
                                    string.IsNullOrEmpty(password) ||
                                    string.IsNullOrEmpty(department) || 
                                    string.IsNullOrEmpty(position))
                                {
                                    errors.Add($"Row {row}: Missing required fields (Full Name, Username, Password, Department, or Position)");
                                    errorCount++;
                                    continue;
                                }

                                if (usingNewTables)
                                {
                                    // Check if username already exists in Users table
                                    string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                                    using (var command = new SqlCommand(checkUsernameQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@Username", username);
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        
                                        if (count > 0)
                                        {
                                            errors.Add($"Row {row}: Username {username} already exists");
                                            errorCount++;
                                            continue;
                                        }
                                    }

                                    // Insert into Users table
                                    string userId = Guid.NewGuid().ToString();
                                    string insertUserQuery = @"
                                        INSERT INTO Users (UserId, FullName, Username, Password, Role) 
                                        VALUES (@UserId, @FullName, @Username, @Password, 'teacher')";
                                    
                                    using (var command = new SqlCommand(insertUserQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@UserId", userId);
                                        command.Parameters.AddWithValue("@FullName", fullName);
                                        command.Parameters.AddWithValue("@Username", username);
                                        command.Parameters.AddWithValue("@Password", password);
                                        
                                        await command.ExecuteNonQueryAsync();
                                    }

                                    // Insert into TeacherDetails table
                                    string insertTeacherQuery = @"
                                        INSERT INTO TeacherDetails (UserId, Department, Position)
                                        VALUES (@UserId, @Department, @Position)";
                                    
                                    using (var command = new SqlCommand(insertTeacherQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@UserId", userId);
                                        command.Parameters.AddWithValue("@Department", department);
                                        command.Parameters.AddWithValue("@Position", position);
                                        await command.ExecuteNonQueryAsync();
                                    }
                                }
                                else
                                {
                                    // Check if Teachers table exists
                                    bool teachersTableExists = false;
                                    string checkTeachersTableQuery = @"
                                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                                        WHERE TABLE_NAME = 'Teachers'";
                                        
                                    using (var command = new SqlCommand(checkTeachersTableQuery, connection))
                                    {
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        teachersTableExists = (count > 0);
                                    }
                                    
                                    if (!teachersTableExists)
                                    {
                                        // Create Teachers table if it doesn't exist
                                        string createTeachersTableQuery = @"
                                            CREATE TABLE Teachers (
                                                TeacherId VARCHAR(50) PRIMARY KEY,
                                                FullName NVARCHAR(100) NOT NULL,
                                                Username NVARCHAR(50) NOT NULL UNIQUE,
                                                Password NVARCHAR(50) NOT NULL,
                                                Department NVARCHAR(100) NOT NULL,
                                                Position NVARCHAR(100) NOT NULL
                                            )";
                                            
                                            using (var command = new SqlCommand(createTeachersTableQuery, connection))
                                            {
                                                await command.ExecuteNonQueryAsync();
                                            }
                                    }

                                    // Check if username already exists
                                    string checkUsernameQuery = "SELECT COUNT(*) FROM Teachers WHERE Username = @Username";
                                    using (var command = new SqlCommand(checkUsernameQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@Username", username);
                                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                        
                                        if (count > 0)
                                        {
                                            errors.Add($"Row {row}: Username {username} already exists");
                                            errorCount++;
                                            continue;
                                        }
                                    }

                                    // Insert into Teachers table
                                    string teacherId = Guid.NewGuid().ToString();
                                    string insertQuery = @"
                                        INSERT INTO Teachers (TeacherId, FullName, Username, Password, Department, Position)
                                        VALUES (@TeacherId, @FullName, @Username, @Password, @Department, @Position)";
                                    
                                    using (var command = new SqlCommand(insertQuery, connection))
                                    {
                                        command.Parameters.AddWithValue("@TeacherId", teacherId);
                                        command.Parameters.AddWithValue("@FullName", fullName);
                                        command.Parameters.AddWithValue("@Username", username);
                                        command.Parameters.AddWithValue("@Password", password);
                                        command.Parameters.AddWithValue("@Department", department);
                                        command.Parameters.AddWithValue("@Position", position);
                                        
                                        await command.ExecuteNonQueryAsync();
                                    }
                                }

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Row {row}: {ex.Message}");
                                errorCount++;
                            }
                        }
                    }
                }
            }

            TempData["Success"] = $"Successfully imported {successCount} teacher records.";
            if (errorCount > 0)
            {
                TempData["ErrorList"] = string.Join("<br/>", errors);
            }

            return RedirectToAction("AdminDashboard");
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error importing teachers: {ex.Message}";
            return RedirectToAction("AdminDashboard");
        }
    }

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
            worksheet.Cells[1, 7].Value = "Email";
            worksheet.Cells[1, 8].Value = "Phone Number";
            
            // Format header row with bold font and background color
            using (var range = worksheet.Cells[1, 1, 1, 8])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
            
            // Add sample data
            worksheet.Cells[2, 1].Value = "Mari John Robert M.Bernabe";
            worksheet.Cells[2, 2].Value = "mari123";
            worksheet.Cells[2, 3].Value = "password123";
            worksheet.Cells[2, 4].Value = "21-03000";
            worksheet.Cells[2, 5].Value = "CICT";
            worksheet.Cells[2, 6].Value = "C2022";
            worksheet.Cells[2, 7].Value = "mari123@example.com";
            worksheet.Cells[2, 8].Value = "09123456789";
            
            worksheet.Cells[3, 1].Value = "Joy Bantule";
            worksheet.Cells[3, 2].Value = "joy.joy";
            worksheet.Cells[3, 3].Value = "password456";
            worksheet.Cells[3, 4].Value = "21-03002";
            worksheet.Cells[3, 5].Value = "CICT";
            worksheet.Cells[3, 6].Value = "B2022";
            worksheet.Cells[3, 7].Value = "joy.joy@example.com";
            worksheet.Cells[3, 8].Value = "09987654321";
            
            // Add a note about valid Course values
            worksheet.Cells[5, 1].Value = "* All fields are required";
            worksheet.Cells[5, 1, 5, 8].Merge = true;
            worksheet.Cells[5, 1].Style.Font.Bold = true;
            worksheet.Cells[5, 1].Style.Font.Italic = true;
            
            worksheet.Cells[6, 1].Value = "Valid Course values: CAS, CBM, CCJ, COE, CET, CHTM, CICT";
            worksheet.Cells[6, 1, 6, 8].Merge = true;
            worksheet.Cells[6, 1].Style.Font.Italic = true;
            
            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            
            package.Save();
        }
        
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "StudentImportTemplate.xlsx");
    }

    // Add this method for downloading the teacher import template
    public IActionResult DownloadTeacherTemplate()
    {
        // Make sure to register the EPPlus license context at application startup
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        
        var stream = new MemoryStream();
        using (var package = new ExcelPackage(stream))
        {
            var worksheet = package.Workbook.Worksheets.Add("Teachers");
            
            // Add header row with all required columns
            worksheet.Cells[1, 1].Value = "Full Name*";
            worksheet.Cells[1, 2].Value = "Username*";
            worksheet.Cells[1, 3].Value = "Password*";
            worksheet.Cells[1, 4].Value = "Department*";
            worksheet.Cells[1, 5].Value = "Position*";
            
            // Format header row with bold font and background color
            using (var range = worksheet.Cells[1, 1, 1, 5])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
            }
            
            // Add sample data
            worksheet.Cells[2, 1].Value = "John Smith";
            worksheet.Cells[2, 2].Value = "john.smith";
            worksheet.Cells[2, 3].Value = "password123";
            worksheet.Cells[2, 4].Value = "Computer Science";
            worksheet.Cells[2, 5].Value = "Professor";
            
            worksheet.Cells[3, 1].Value = "Mary Johnson";
            worksheet.Cells[3, 2].Value = "mary.johnson";
            worksheet.Cells[3, 3].Value = "password456";
            worksheet.Cells[3, 4].Value = "Mathematics";
            worksheet.Cells[3, 5].Value = "Assistant Professor";
            
            // Add a note about all fields being required
            worksheet.Cells[5, 1].Value = "* All fields are required";
            worksheet.Cells[5, 1, 5, 5].Merge = true;
            worksheet.Cells[5, 1].Style.Font.Bold = true;
            worksheet.Cells[5, 1].Style.Font.Italic = true;
            
            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            
            package.Save();
        }
        
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "TeacherImportTemplate.xlsx");
    }

    [HttpPost]
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

    [HttpGet]
    public async Task<IActionResult> GetProfilePicture(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return NotFound();
            }
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if binary columns exist and contain data
                bool hasBinaryData = false;
                byte[] profileImageData = null;
                string contentType = "image/jpeg"; // Default content type
                
                // First check if we have binary data in the new table structure
                string checkBinaryDataQuery = @"
                    SELECT 
                        CASE WHEN ProfilePictureData IS NOT NULL THEN 1 ELSE 0 END AS HasBinaryData,
                        ProfilePictureData,
                        ProfileMetadata,
                        ProfilePicturePath
                    FROM StudentDetails 
                    WHERE IdNumber = @StudentId";
                
                using (var command = new SqlCommand(checkBinaryDataQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            hasBinaryData = Convert.ToBoolean(reader["HasBinaryData"]);
                            
                            if (hasBinaryData)
                            {
                                // Get binary data directly
                                profileImageData = (byte[])reader["ProfilePictureData"];
                                
                                // Get content type from metadata if available
                                string metadata = reader["ProfileMetadata"] as string;
                                if (!string.IsNullOrEmpty(metadata))
                                {
                                    try
                                    {
                                        var metadataObj = JsonConvert.DeserializeObject<dynamic>(metadata);
                                        contentType = metadataObj.ContentType ?? "image/jpeg";
                                    }
                                    catch
                                    {
                                        // Fallback to default content type
                                    }
                                }
                            }
                            else
                            {
                                // If no binary data, check for profile picture path
                                string profilePicturePath = reader["ProfilePicturePath"] as string;
                                
                                if (!string.IsNullOrEmpty(profilePicturePath))
                                {
                                    // Check if path is a data URL
                                    if (profilePicturePath.StartsWith("data:"))
                                    {
                                        // Parse data URL to get binary data
                                        string[] parts = profilePicturePath.Split(',');
                                        if (parts.Length == 2)
                                        {
                                            if (parts[0].Contains(":") && parts[0].Contains(";"))
                                            {
                                                contentType = parts[0].Split(':')[1].Split(';')[0];
                                            }
                                            
                                            try
                                            {
                                                // Convert base64 string to binary
                                                profileImageData = Convert.FromBase64String(parts[1]);
                                                hasBinaryData = true;
                                            }
                                            catch
                                            {
                                                // Invalid base64, will fall back to default image
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Physical file path, return file from wwwroot
                                        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                            profilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                        
                                        if (System.IO.File.Exists(filePath))
                                        {
                                            // Set content type based on file extension
                                            string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                            contentType = extension switch
                                            {
                                                ".jpg" or ".jpeg" => "image/jpeg",
                                                ".png" => "image/png",
                                                ".gif" => "image/gif",
                                                _ => "image/jpeg"
                                            };
                                            
                                            return PhysicalFile(filePath, contentType);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // If no binary data found in StudentDetails, check old Students table
                if (!hasBinaryData)
                {
                    string checkOldTableQuery = @"
                        SELECT ProfilePicturePath
                        FROM Students
                        WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(checkOldTableQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            string profilePicturePath = result.ToString();
                            
                            if (!string.IsNullOrEmpty(profilePicturePath))
                            {
                                // Check if path is a data URL
                                if (profilePicturePath.StartsWith("data:"))
                                {
                                    // Parse data URL to get binary data
                                    string[] parts = profilePicturePath.Split(',');
                                    if (parts.Length == 2)
                                    {
                                        if (parts[0].Contains(":") && parts[0].Contains(";"))
                                        {
                                            contentType = parts[0].Split(':')[1].Split(';')[0];
                                        }
                                        
                                        try
                                        {
                                            // Convert base64 string to binary
                                            profileImageData = Convert.FromBase64String(parts[1]);
                                            hasBinaryData = true;
                                        }
                                        catch
                                        {
                                            // Invalid base64, will fall back to default image
                                        }
                                    }
                                }
                                else
                                {
                                    // Physical file path, return file from wwwroot
                                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                        profilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                    
                                    if (System.IO.File.Exists(filePath))
                                    {
                                        // Set content type based on file extension
                                        string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                        contentType = extension switch
                                        {
                                            ".jpg" or ".jpeg" => "image/jpeg",
                                            ".png" => "image/png",
                                            ".gif" => "image/gif",
                                            _ => "image/jpeg"
                                        };
                                        
                                        return PhysicalFile(filePath, contentType);
                                    }
                                }
                            }
                        }
                    }
                }
                
                // If we have binary data, return it
                if (hasBinaryData && profileImageData != null)
                {
                    return File(profileImageData, contentType);
                }
            }
            
            // If we get here, no valid profile picture was found
            // Return a default profile picture from wwwroot
            string defaultImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "default-profile.png");
            
            if (System.IO.File.Exists(defaultImagePath))
            {
                return PhysicalFile(defaultImagePath, "image/png");
            }
            
            // If even the default image doesn't exist, return a 1x1 transparent pixel
            byte[] transparentPixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            return File(transparentPixel, "image/gif");
        }
        catch (Exception)
        {
            // In case of any error, return a 1x1 transparent pixel
            byte[] transparentPixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            return File(transparentPixel, "image/gif");
        }
    }
    
    [HttpGet]
    public async Task<IActionResult> GetResume(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return NotFound();
            }
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if binary columns exist and contain data
                bool hasBinaryData = false;
                byte[] resumeData = null;
                string contentType = "application/pdf"; // Default content type
                string originalFileName = "resume.pdf"; // Default filename
                
                // First check if we have binary data in the new table structure
                string checkBinaryDataQuery = @"
                    SELECT 
                        CASE WHEN ResumeData IS NOT NULL THEN 1 ELSE 0 END AS HasBinaryData,
                        ResumeData,
                        ResumeMetadata,
                        ResumeFileName,
                        OriginalResumeFileName
                    FROM StudentDetails 
                    WHERE IdNumber = @StudentId";
                
                using (var command = new SqlCommand(checkBinaryDataQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            hasBinaryData = Convert.ToBoolean(reader["HasBinaryData"]);
                            
                            if (hasBinaryData)
                            {
                                // Get binary data directly
                                resumeData = (byte[])reader["ResumeData"];
                                
                                // Get content type from metadata if available
                                string metadata = reader["ResumeMetadata"] as string;
                                if (!string.IsNullOrEmpty(metadata))
                                {
                                    try
                                    {
                                        var metadataObj = JsonConvert.DeserializeObject<dynamic>(metadata);
                                        contentType = metadataObj.ContentType ?? "application/pdf";
                                    }
                                    catch
                                    {
                                        // Fallback to default content type
                                    }
                                }
                                
                                // Get original filename if available
                                if (reader["OriginalResumeFileName"] != DBNull.Value)
                                {
                                    originalFileName = reader["OriginalResumeFileName"].ToString();
                                }
                            }
                            else
                            {
                                // If no binary data, check for resume file path
                                string resumeFilePath = reader["ResumeFileName"] as string;
                                
                                if (!string.IsNullOrEmpty(resumeFilePath))
                                {
                                    // Check if path is a data URL
                                    if (resumeFilePath.StartsWith("data:"))
                                    {
                                        // Parse data URL to get binary data
                                        string[] parts = resumeFilePath.Split(',');
                                        if (parts.Length == 2)
                                        {
                                            if (parts[0].Contains(":") && parts[0].Contains(";"))
                                            {
                                                contentType = parts[0].Split(':')[1].Split(';')[0];
                                            }
                                            
                                            try
                                            {
                                                // Convert base64 string to binary
                                                resumeData = Convert.FromBase64String(parts[1]);
                                                hasBinaryData = true;
                                            }
                                            catch
                                            {
                                                // Invalid base64, will fall back to not found
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Physical file path, return file from wwwroot
                                        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                            resumeFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                        
                                        if (System.IO.File.Exists(filePath))
                                        {
                                            // Set content type based on file extension
                                            string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                            contentType = extension switch
                                            {
                                                ".pdf" => "application/pdf",
                                                ".doc" => "application/msword",
                                                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                                _ => "application/octet-stream"
                                            };
                                            
                                            // Set original filename based on the last part of the path
                                            originalFileName = Path.GetFileName(filePath);
                                            
                                            // Return the file with download name
                                            return PhysicalFile(filePath, contentType, originalFileName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // If no binary data found in StudentDetails, check old Students table
                if (!hasBinaryData)
                {
                    string checkOldTableQuery = @"
                        SELECT ResumeFileName
                        FROM Students
                        WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(checkOldTableQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            string resumeFilePath = result.ToString();
                            
                            if (!string.IsNullOrEmpty(resumeFilePath))
                            {
                                // Check if path is a data URL
                                if (resumeFilePath.StartsWith("data:"))
                                {
                                    // Parse data URL to get binary data
                                    string[] parts = resumeFilePath.Split(',');
                                    if (parts.Length == 2)
                                    {
                                        if (parts[0].Contains(":") && parts[0].Contains(";"))
                                        {
                                            contentType = parts[0].Split(':')[1].Split(';')[0];
                                        }
                                        
                                        try
                                        {
                                            // Convert base64 string to binary
                                            resumeData = Convert.FromBase64String(parts[1]);
                                            hasBinaryData = true;
                                        }
                                        catch
                                        {
                                            // Invalid base64, will fall back to not found
                                        }
                                    }
                                }
                                else
                                {
                                    // Physical file path, return file from wwwroot
                                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                        resumeFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                    
                                    if (System.IO.File.Exists(filePath))
                                    {
                                        // Set content type based on file extension
                                        string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                        contentType = extension switch
                                        {
                                            ".pdf" => "application/pdf",
                                            ".doc" => "application/msword",
                                            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                            _ => "application/octet-stream"
                                        };
                                        
                                        // Set original filename based on the last part of the path
                                        originalFileName = Path.GetFileName(filePath);
                                        
                                        // Return the file with download name
                                        return PhysicalFile(filePath, contentType, originalFileName);
                                    }
                                }
                            }
                        }
                    }
                }
                
                // If we have binary data, return it as a downloadable file
                if (hasBinaryData && resumeData != null)
                {
                    return File(resumeData, contentType, originalFileName);
                }
            }
            
            // If we get here, no valid resume was found
            return NotFound("Resume not found.");
        }
        catch (Exception ex)
        {
            return BadRequest("Error retrieving resume: " + ex.Message);
        }
    }

    // Add this action method to handle the Employer Dashboard
    public async Task<IActionResult> EmployerDashboard()
    {
        // Get employer info from session
        ViewBag.EmployerName = HttpContext.Session.GetString("FullName");
        ViewBag.CompanyName = HttpContext.Session.GetString("Company");
        ViewBag.EmployerId = HttpContext.Session.GetString("EmployerId");

        // Get all students to display in the employer dashboard
        var allStudents = await GetAllStudentsWithDetails();
        ViewBag.AllStudents = allStudents;

        return View();
    }

    // Add this action method to handle the Employer Profile page
    public async Task<IActionResult> EmployerProfile()
    {
        try
        {
            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                // If no employer ID in session, redirect to login
                TempData["Error"] = "Session expired. Please log in again.";
                return RedirectToAction("Login", "Home");
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                string query;
                if (useNewTable)
                {
                    // Check which columns exist
                    bool hasEmailInUsers = await CheckColumnExists(conn, "Users", "Email");
                    bool hasPhoneInUsers = await CheckColumnExists(conn, "Users", "PhoneNumber");
                    bool hasProfilePicInEmployerDetails = await CheckColumnExists(conn, "EmployerDetails", "ProfilePicturePath");

                    // Build query for new table structure (Users + EmployerDetails)
                    query = @"
                        SELECT 
                            u.FullName,
                            u.Username,
                            ed.Company,
                            ed.Description";
                    
                    if (hasEmailInUsers)
                    {
                        query += ", u.Email";
                    }
                    
                    if (hasPhoneInUsers)
                    {
                        query += ", u.PhoneNumber";
                    }
                    
                    if (hasProfilePicInEmployerDetails)
                    {
                        query += ", ed.ProfilePicturePath";
                    }
                    
                    query += @"
                        FROM Users u
                        JOIN EmployerDetails ed ON u.UserId = ed.UserId
                        WHERE u.UserId = @EmployerId";
                }
                else
                {
                    // Check if old Employers table has these columns
                    var checkEmailColumn = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Employers' AND COLUMN_NAME = 'Email'",
                        conn
                    );
                    bool hasEmailColumn = (int)await checkEmailColumn.ExecuteScalarAsync() > 0;

                    var checkPhoneColumn = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Employers' AND COLUMN_NAME = 'PhoneNumber'",
                        conn
                    );
                    bool hasPhoneColumn = (int)await checkPhoneColumn.ExecuteScalarAsync() > 0;

                    var checkProfilePicColumn = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Employers' AND COLUMN_NAME = 'ProfilePicturePath'",
                        conn
                    );
                    bool hasProfilePicColumn = (int)await checkProfilePicColumn.ExecuteScalarAsync() > 0;

                    // Build query for old table structure (Employers)
                    query = @"
                        SELECT 
                            FullName,
                            Username, 
                            Company,
                            Description";
                    
                    if (hasEmailColumn)
                    {
                        query += ", Email";
                    }
                    
                    if (hasPhoneColumn)
                    {
                        query += ", PhoneNumber";
                    }
                    
                    if (hasProfilePicColumn)
                    {
                        query += ", ProfilePicturePath";
                    }
                    
                    query += " FROM Employers WHERE EmployerId = @EmployerId";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Set ViewBag values for the view
                            ViewBag.EmployerName = reader["FullName"].ToString();
                            ViewBag.Username = reader["Username"].ToString();
                            ViewBag.CompanyName = reader["Company"].ToString();
                            ViewBag.Description = reader["Description"]?.ToString() ?? "";
                            
                            // Check if columns exist in the result set
                            bool hasEmail = reader.HasColumn("Email");
                            bool hasPhoneNumber = reader.HasColumn("PhoneNumber");
                            bool hasProfilePicture = reader.HasColumn("ProfilePicturePath");

                            ViewBag.Email = hasEmail ? reader["Email"]?.ToString() ?? "" : "";
                            ViewBag.PhoneNumber = hasPhoneNumber ? reader["PhoneNumber"]?.ToString() ?? "" : "";
                            ViewBag.ProfilePicturePath = hasProfilePicture && reader["ProfilePicturePath"] != DBNull.Value
                                ? reader["ProfilePicturePath"].ToString()
                                : "/images/blank.jpg";

                            return View();
                        }
                        else
                        {
                            // Employer not found in database, but we have session
                            // Just display with session values as backup
                            ViewBag.EmployerName = HttpContext.Session.GetString("FullName") ?? "Unknown";
                            ViewBag.Username = "";
                            ViewBag.CompanyName = HttpContext.Session.GetString("Company") ?? "";
                            ViewBag.Description = "";
                            ViewBag.Email = "";
                            ViewBag.PhoneNumber = "";
                            ViewBag.ProfilePicturePath = "/images/blank.jpg";
                            
                            return View();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in EmployerProfile: {ex.Message}");
            
            // Fall back to session values in case of error
            ViewBag.EmployerName = HttpContext.Session.GetString("FullName") ?? "Unknown";
            ViewBag.Username = "";
            ViewBag.CompanyName = HttpContext.Session.GetString("Company") ?? "";
            ViewBag.Description = "";
            ViewBag.Email = "";
            ViewBag.PhoneNumber = "";
            ViewBag.ProfilePicturePath = "/images/blank.jpg";
            
            return View();
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
                            sd.ResumeFileName
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE sd.IdNumber = @StudentId AND sd.IsProfileVisible = 1";
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

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var student = new
                            {
                                FullName = reader["FullName"].ToString(),
                                IdNumber = reader["IdNumber"].ToString(),
                                Course = reader["Course"].ToString(),
                                Section = reader["Section"].ToString(),
                                Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                                Achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "",
                                Comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "",
                                BadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None",
                                ProfilePicture = reader["ProfilePicturePath"] != DBNull.Value ? reader["ProfilePicturePath"].ToString() : "/images/blank.jpg",
                                IsProfileVisible = Convert.ToBoolean(reader["IsProfileVisible"]),
                                IsResumeVisible = Convert.ToBoolean(reader["IsResumeVisible"]),
                                Resume = reader["ResumeFileName"] != DBNull.Value ? reader["ResumeFileName"].ToString() : null
                            };

                            return Json(new { success = true, student = student });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student not found or profile not visible" });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student profile for employer");
            return Json(new { success = false, message = "Error loading profile" });
        }
    }

    public IActionResult GetStudentProfile(string studentId)
    {
        try
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    conn
                );
                bool useNewTable = (int)checkTableCmd.ExecuteScalar() > 0;

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
                            sd.ResumeFileName
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE sd.IdNumber = @StudentId AND sd.IsProfileVisible = 1";
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

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var profileData = new
                            {
                                success = true,
                                fullName = reader["FullName"].ToString(),
                                idNumber = reader["IdNumber"].ToString(),
                                course = reader["Course"].ToString(),
                                section = reader["Section"].ToString(),
                                score = Convert.ToInt32(reader["Score"]),
                                achievements = reader["Achievements"].ToString(),
                                comments = reader["Comments"].ToString(),
                                badgeColor = reader["BadgeColor"].ToString(),
                                profilePicture = Url.Action("GetProfilePicture", "Dashboard", new { studentId = reader["IdNumber"].ToString(), t = DateTime.Now.Ticks }),
                                isProfileVisible = Convert.ToBoolean(reader["IsProfileVisible"]),
                                isResumeVisible = Convert.ToBoolean(reader["IsResumeVisible"]),
                                resume = reader["ResumeFileName"] != DBNull.Value ? reader["ResumeFileName"].ToString() : null
                            };

                            return Json(profileData);
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
            _logger.LogError($"Error in GetStudentProfile: {ex.Message}");
            return Json(new { success = false, message = "Error loading profile. Please try again." });
        }
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
                    bool hasProfilePic = await CheckColumnExists(conn, "EmployerDetails", "ProfilePicturePath");
                    
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
                    bool hasProfilePic = await CheckColumnExists(conn, "Employers", "ProfilePicturePath");
                    
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
    public async Task<IActionResult> GetEmployerDetails(string employerId)
    {
        try
        {
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer ID is required." });
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                string query;
                if (useNewTable)
                {
                    // Check which columns exist
                    bool hasEmailInUsers = await CheckColumnExists(conn, "Users", "Email");
                    bool hasPhoneInUsers = await CheckColumnExists(conn, "Users", "PhoneNumber");
                    bool hasProfilePicInEmployerDetails = await CheckColumnExists(conn, "EmployerDetails", "ProfilePicturePath");

                    // Build query for new table structure (Users + EmployerDetails)
                    query = @"
                        SELECT 
                            u.FullName,
                            u.Username,
                            ed.Company,
                            ed.Description";
                    
                    if (hasEmailInUsers)
                    {
                        query += ", u.Email";
                    }
                    
                    if (hasPhoneInUsers)
                    {
                        query += ", u.PhoneNumber";
                    }
                    
                    if (hasProfilePicInEmployerDetails)
                    {
                        query += ", ed.ProfilePicturePath";
                    }
                    
                    query += @"
                        FROM Users u
                        JOIN EmployerDetails ed ON u.UserId = ed.UserId
                        WHERE u.UserId = @EmployerId";
                }
                else
                {
                    // Check if old Employers table has these columns
                    bool hasEmailColumn = await CheckColumnExists(conn, "Employers", "Email");
                    bool hasPhoneColumn = await CheckColumnExists(conn, "Employers", "PhoneNumber");
                    bool hasProfilePicColumn = await CheckColumnExists(conn, "Employers", "ProfilePicturePath");

                    // Build query for old table structure
                    query = @"
                        SELECT 
                            FullName,
                            Username,
                            Company,
                            Description";
                    
                    if (hasEmailColumn)
                    {
                        query += ", Email";
                    }
                    
                    if (hasPhoneColumn)
                    {
                        query += ", PhoneNumber";
                    }
                    
                    if (hasProfilePicColumn)
                    {
                        query += ", ProfilePicturePath";
                    }
                    
                    query += " FROM Employers WHERE EmployerId = @EmployerId";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Check if columns exist in the result set
                            bool hasEmail = reader.HasColumn("Email");
                            bool hasPhoneNumber = reader.HasColumn("PhoneNumber");
                            bool hasProfilePicture = reader.HasColumn("ProfilePicturePath");
                            
                            var employer = new
                            {
                                fullName = reader["FullName"].ToString(),
                                username = reader["Username"].ToString(),
                                company = reader["Company"].ToString(),
                                description = reader["Description"]?.ToString() ?? "",
                                email = hasEmail ? reader["Email"]?.ToString() ?? "" : "",
                                phoneNumber = hasPhoneNumber ? reader["PhoneNumber"]?.ToString() ?? "" : "",
                                profilePicturePath = hasProfilePicture && reader["ProfilePicturePath"] != DBNull.Value
                                    ? reader["ProfilePicturePath"].ToString()
                                    : null
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
            _logger.LogError($"Error in GetEmployerDetails: {ex.Message}");
            return Json(new { success = false, message = "Error loading employer details. Please try again." });
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
    public async Task<IActionResult> GetEmployerChats(string employerId)
    {
        try
        {
            _logger.LogInformation($"GetEmployerChats called with employerId: {employerId}");
            
            if (string.IsNullOrEmpty(employerId))
            {
                _logger.LogWarning("GetEmployerChats: Employer ID is empty");
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
                
                // Get students using new database structure (Users + StudentDetails tables)
                string sql = $@"
                    SELECT u.UserId, u.FullName, u.Username, 
                           sd.IdNumber, sd.Course, sd.Section, sd.BadgeColor, sd.Score,
                           sd.Achievements, sd.Comments, sd.ProfilePicturePath,
                           sd.IsProfileVisible, sd.IsResumeVisible {gradeColumnsQuery}
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
                            
                            students.Add(new Student
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
                            });
                        }
                    }
                }
            }
            
            // Get the total count of students
            ViewBag.TotalStudentCount = students.Count;
        }
        
        return View(students);
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
                    string badgeColor = "green";
                    if (model.Score >= 95) badgeColor = "platinum";
                    else if (model.Score >= 85) badgeColor = "gold";
                    else if (model.Score >= 75) badgeColor = "silver";
                    else if (model.Score >= 65) badgeColor = "bronze";
                    else if (model.Score >= 50) badgeColor = "rising-star";
                    else badgeColor = "warning";
                    
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
                
                // Get badge color based on score
                string badgeColor = "green";
                if (score >= 95) badgeColor = "platinum";
                else if (score >= 85) badgeColor = "gold";
                else if (score >= 75) badgeColor = "silver";
                else if (score >= 65) badgeColor = "bronze";
                else if (score >= 50) badgeColor = "rising-star";
                else badgeColor = "warning";
                
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
                    command.Parameters.AddWithValue("@Score", score);
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
                
                // Get badge color based on new score
                string badgeColor = "green";
                if (newScore >= 95) badgeColor = "platinum";
                else if (newScore >= 85) badgeColor = "gold";
                else if (newScore >= 75) badgeColor = "silver";
                else if (newScore >= 65) badgeColor = "bronze";
                else if (newScore >= 50) badgeColor = "rising-star";
                else badgeColor = "warning";
                    
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
                
                // Return the new badge information
                return Json(new { 
                    success = true, 
                    message = "Grades saved successfully!",
                    newScore = newScore,
                    badgeColor = badgeColor,
                    badgeName = badgeName,
                    achievements = achievements,
                    comments = comments 
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating student grades for {studentId}: {ex.Message}");
            return Json(new { success = false, message = "An error occurred: " + ex.Message });
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
                // If no employer ID in session, redirect to login
                TempData["Error"] = "Session expired. Please log in again.";
                return RedirectToAction("Login", "Home");
            }

            // Extract form data
            string fullName = form["FullName"].ToString();
            string username = form["Username"].ToString();
            string companyName = form["CompanyName"].ToString();
            string email = form["Email"].ToString();
            string phoneNumber = form["PhoneNumber"].ToString();
            string description = form["Description"].ToString();
            string password = form["Password"].ToString();

            // Allow update even if some fields are empty - removed strict validation

            // Handle profile picture upload
            string profilePicturePath = null;
            
            // Check if we have a base64 image
            if (form.ContainsKey("ProfilePictureBase64"))
            {
                string base64Image = form["ProfilePictureBase64"];
                
                if (!string.IsNullOrEmpty(base64Image) && base64Image.Contains(","))
                {
                    // Extract the data part from the base64 string
                    string[] parts = base64Image.Split(',');
                    string base64Data = parts[1];
                    
                    try
                    {
                        // Convert base64 to bytes
                        byte[] imageBytes = Convert.FromBase64String(base64Data);
                        
                        // Generate a unique filename
                        string fileName = $"employer_{employerId}_{DateTime.Now.Ticks}.jpg";
                        string filePath = Path.Combine(_hostingEnvironment.WebRootPath, "images", "profiles", fileName);
                        
                        // Ensure directory exists
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        
                        // Save the file
                        System.IO.File.WriteAllBytes(filePath, imageBytes);
                        
                        // Set profile picture path
                        profilePicturePath = $"/images/profiles/{fileName}";
                        _logger.LogInformation($"Saved profile picture from base64 to {profilePicturePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing base64 image: {ex.Message}");
                    }
                }
            }
            // Handle regular file upload if no base64 image
            else if (form.Files.Count > 0 && form.Files["ProfilePicture"] != null && form.Files["ProfilePicture"].Length > 0)
            {
                try
                {
                    var file = form.Files["ProfilePicture"];
                    _logger.LogInformation($"Processing file upload: {file.FileName}, size: {file.Length} bytes");
                    
                    // Generate a unique filename
                    string fileName = $"employer_{employerId}_{DateTime.Now.Ticks}{Path.GetExtension(file.FileName)}";
                    string filePath = Path.Combine(_hostingEnvironment.WebRootPath, "images", "profiles", fileName);
                    
                    // Ensure directory exists
                    string directory = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directory))
                    {
                        _logger.LogInformation($"Creating directory: {directory}");
                        Directory.CreateDirectory(directory);
                    }
                    
                    // Save the file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }
                    
                    // Set profile picture path
                    profilePicturePath = $"/images/profiles/{fileName}";
                    _logger.LogInformation($"Saved profile picture to {profilePicturePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error uploading profile picture: {ex.Message}");
                }
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if we're using the new table structure
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool newTableExists = (int)await checkTableCmd.ExecuteScalarAsync() > 0;
                _logger.LogInformation($"Using new table structure: {newTableExists}");
                
                if (newTableExists)
                {
                    // Check if EmployerDetails has a ProfilePicturePath column
                    var hasProfilePicColumn = await CheckColumnExists(conn, "EmployerDetails", "ProfilePicturePath");
                    _logger.LogInformation($"EmployerDetails has ProfilePicturePath column: {hasProfilePicColumn}");
                    
                    // If EmployerDetails doesn't have the column, add it
                    if (!hasProfilePicColumn && profilePicturePath != null)
                    {
                        try
                        {
                            _logger.LogInformation("Adding ProfilePicturePath column to EmployerDetails table");
                            string addColumnQuery = "ALTER TABLE EmployerDetails ADD ProfilePicturePath NVARCHAR(255) NULL";
                            using (var addColumnCmd = new SqlCommand(addColumnQuery, conn))
                            {
                                await addColumnCmd.ExecuteNonQueryAsync();
                            }
                            hasProfilePicColumn = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error adding ProfilePicturePath column: {ex.Message}");
                        }
                    }
                    
                    // Update the EmployerDetails table
                    string detailsUpdateQuery = "UPDATE EmployerDetails SET Company = @Company, Description = @Description";
                    
                    if (hasProfilePicColumn && profilePicturePath != null)
                    {
                        detailsUpdateQuery += ", ProfilePicturePath = @ProfilePicturePath";
                    }
                    
                    detailsUpdateQuery += " WHERE UserId = @EmployerId";
                    
                    using (SqlCommand detailsCmd = new SqlCommand(detailsUpdateQuery, conn))
                    {
                        detailsCmd.Parameters.AddWithValue("@Company", companyName);
                        detailsCmd.Parameters.AddWithValue("@Description", description);
                        detailsCmd.Parameters.AddWithValue("@EmployerId", employerId);
                        
                        if (hasProfilePicColumn && profilePicturePath != null)
                        {
                            detailsCmd.Parameters.AddWithValue("@ProfilePicturePath", profilePicturePath);
                            _logger.LogInformation($"Updating ProfilePicturePath to {profilePicturePath} in EmployerDetails");
                        }
                        
                        await detailsCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Check if Employers has a ProfilePicturePath column
                    var hasProfilePicColumn = await CheckColumnExists(conn, "Employers", "ProfilePicturePath");
                    _logger.LogInformation($"Employers table has ProfilePicturePath column: {hasProfilePicColumn}");
                    
                    // If Employers doesn't have the column, add it
                    if (!hasProfilePicColumn && profilePicturePath != null)
                    {
                        try
                        {
                            _logger.LogInformation("Adding ProfilePicturePath column to Employers table");
                            string addColumnQuery = "ALTER TABLE Employers ADD ProfilePicturePath NVARCHAR(255) NULL";
                            using (var addColumnCmd = new SqlCommand(addColumnQuery, conn))
                            {
                                await addColumnCmd.ExecuteNonQueryAsync();
                            }
                            hasProfilePicColumn = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error adding ProfilePicturePath column: {ex.Message}");
                        }
                    }
                    
                    // Update query for the Employers table
                    string updateQuery = @"
                        UPDATE Employers 
                        SET FullName = @FullName, 
                            Username = @Username,
                            Company = @Company,
                            Description = @Description";
                    
                    if (!string.IsNullOrEmpty(password))
                    {
                        updateQuery += ", Password = @Password";
                    }
                    
                    if (hasProfilePicColumn && profilePicturePath != null)
                    {
                        updateQuery += ", ProfilePicturePath = @ProfilePicturePath";
                    }
                    
                    updateQuery += " WHERE EmployerId = @EmployerId";
                    
                    using (SqlCommand cmd = new SqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@FullName", fullName);
                        cmd.Parameters.AddWithValue("@Username", username);
                        cmd.Parameters.AddWithValue("@Company", companyName);
                        cmd.Parameters.AddWithValue("@Description", description);
                        cmd.Parameters.AddWithValue("@EmployerId", employerId);
                        
                        if (!string.IsNullOrEmpty(password))
                        {
                            cmd.Parameters.AddWithValue("@Password", password);
                        }
                        
                        if (hasProfilePicColumn && profilePicturePath != null)
                        {
                            cmd.Parameters.AddWithValue("@ProfilePicturePath", profilePicturePath);
                            _logger.LogInformation($"Updating ProfilePicturePath to {profilePicturePath} in Employers");
                        }
                        
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                
                // Update session values
                HttpContext.Session.SetString("FullName", fullName);
                HttpContext.Session.SetString("Company", companyName);
                if (profilePicturePath != null)
                {
                    HttpContext.Session.SetString("ProfilePicturePath", profilePicturePath);
                }
                
                TempData["Success"] = "Profile updated successfully!";
                return RedirectToAction("EmployerProfile");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in UpdateEmployerProfileForm: {ex.Message}");
            TempData["Error"] = "Error updating profile: " + ex.Message;
            return RedirectToAction("EmployerProfile");
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

    [HttpPost]
    public async Task<IActionResult> DeleteStudent(string id)
    {
        try
        {
            _logger.LogInformation($"DeleteStudent called with ID: {id}");
            
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogWarning("DeleteStudent called with empty ID");
                return Json(new { success = false, message = "Student ID is required." });
            }
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                _logger.LogInformation("Database connection opened");
                
                // Begin a transaction for all database operations
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // First check which tables exist
                        bool videocallsTableExists = await TableExists(connection, "VideoCalls");
                        bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                        bool studentsTableExists = await TableExists(connection, "Students");
                        bool usersTableExists = await TableExists(connection, "Users");
                        
                        _logger.LogInformation($"Table check: VideoCalls={videocallsTableExists}, StudentDetails={studentDetailsExists}, Students={studentsTableExists}, Users={usersTableExists}");
                        
                        // Check if we're using the new or old schema
                        if (studentDetailsExists && usersTableExists)
                        {
                            _logger.LogInformation("Using new table schema (StudentDetails + Users)");
                            
                            // First, get the UserId from StudentDetails
                            string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                            string userId = null;
                            
                            using (var command = new SqlCommand(getUserIdQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@IdNumber", id);
                                var result = await command.ExecuteScalarAsync();
                                
                                if (result != null)
                                {
                                    userId = result.ToString();
                                    _logger.LogInformation($"Found student with UserId: {userId}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Student with ID {id} not found in StudentDetails");
                                    transaction.Rollback();
                                    return Json(new { success = false, message = "Student not found." });
                                }
                            }
                            
                            // Delete from VideoCalls first if the table exists (handles FK constraint)
                            if (videocallsTableExists)
                            {
                                string deleteVideoCallsQuery = "DELETE FROM VideoCalls WHERE StudentId = @StudentId";
                                using (var videoCallsCommand = new SqlCommand(deleteVideoCallsQuery, connection, transaction))
                                {
                                    videoCallsCommand.Parameters.AddWithValue("@StudentId", userId);
                                    int videoCallsDeleted = await videoCallsCommand.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Deleted {videoCallsDeleted} related video call records for UserId {userId}");
                                }
                            }
                            
                            // Delete from StudentDetails first (child table)
                            string deleteDetailsQuery = "DELETE FROM StudentDetails WHERE IdNumber = @IdNumber";
                            using (var detailsCommand = new SqlCommand(deleteDetailsQuery, connection, transaction))
                            {
                                detailsCommand.Parameters.AddWithValue("@IdNumber", id);
                                int detailsRowsAffected = await detailsCommand.ExecuteNonQueryAsync();
                                _logger.LogInformation($"Deleted {detailsRowsAffected} row(s) from StudentDetails");
                            }
                            
                            // Delete from Users (parent table)
                            string deleteUserQuery = "DELETE FROM Users WHERE UserId = @UserId";
                            using (var userCommand = new SqlCommand(deleteUserQuery, connection, transaction))
                            {
                                userCommand.Parameters.AddWithValue("@UserId", userId);
                                int userRowsAffected = await userCommand.ExecuteNonQueryAsync();
                                _logger.LogInformation($"Deleted {userRowsAffected} row(s) from Users");
                            }
                        }
                        else if (studentsTableExists)
                        {
                            _logger.LogInformation("Using old table schema (Students)");
                            
                            // Check if student exists
                            string checkQuery = "SELECT COUNT(*) FROM Students WHERE IdNumber = @IdNumber";
                            using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                            {
                                checkCommand.Parameters.AddWithValue("@IdNumber", id);
                                int studentCount = (int)await checkCommand.ExecuteScalarAsync();
                                
                                if (studentCount == 0)
                                {
                                    _logger.LogWarning($"Student with ID {id} not found in Students table");
                                    transaction.Rollback();
                                    return Json(new { success = false, message = "Student not found." });
                                }
                            }
                            
                            // Delete from VideoCalls first if the table exists (handles FK constraint)
                            if (videocallsTableExists)
                            {
                                string deleteVideoCallsQuery = "DELETE FROM VideoCalls WHERE StudentId = @StudentId";
                                using (var videoCallsCommand = new SqlCommand(deleteVideoCallsQuery, connection, transaction))
                                {
                                    videoCallsCommand.Parameters.AddWithValue("@StudentId", id);
                                    int videoCallsDeleted = await videoCallsCommand.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Deleted {videoCallsDeleted} related video call records for student ID {id}");
                                }
                            }
                            
                            // Delete the student
                            string deleteQuery = "DELETE FROM Students WHERE IdNumber = @IdNumber";
                            using (var deleteCommand = new SqlCommand(deleteQuery, connection, transaction))
                            {
                                deleteCommand.Parameters.AddWithValue("@IdNumber", id);
                                int rowsAffected = await deleteCommand.ExecuteNonQueryAsync();
                                
                                _logger.LogInformation($"Deleted {rowsAffected} row(s) from Students table");
                            }
                        }
                        else
                        {
                            _logger.LogError("No student tables found in database");
                            transaction.Rollback();
                            return Json(new { success = false, message = "No student tables found in database." });
                        }
                        
                        // Commit the transaction
                        transaction.Commit();
                        _logger.LogInformation("Transaction committed successfully");
                        return Json(new { success = true, message = "Student deleted successfully." });
                    }
                    catch (Exception ex)
                    {
                        // Roll back the transaction
                        transaction.Rollback();
                        _logger.LogError($"Error in transaction, rolled back: {ex.Message}");
                        return Json(new { success = false, message = $"Database error: {ex.Message}" });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in DeleteStudent: {ex.Message}");
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }
    
    [HttpPost]
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
                _logger.LogInformation("Database connection opened");
                
                // Check which tables exist
                bool teacherDetailsExists = await TableExists(connection, "TeacherDetails");
                bool usersTableExists = await TableExists(connection, "Users");
                
                _logger.LogInformation($"Table check: TeacherDetails={teacherDetailsExists}, Users={usersTableExists}");
                
                // Begin a transaction for all operations
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        if (teacherDetailsExists && usersTableExists)
                        {
                            _logger.LogInformation("Using new table schema (TeacherDetails + Users)");
                            
                            // First, check if teacher exists and get the UserId if using new schema
                            string getUserIdQuery = "SELECT UserId FROM TeacherDetails WHERE UserId = @TeacherId";
                            string userId = teacherId; // Initially assume TeacherId is the UserId
                            
                            using (var command = new SqlCommand(getUserIdQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@TeacherId", teacherId);
                                var result = await command.ExecuteScalarAsync();
                                
                                if (result != null)
                                {
                                    userId = result.ToString();
                                    _logger.LogInformation($"Found teacher with UserId: {userId}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Teacher with ID {teacherId} not found in TeacherDetails");
                                    transaction.Rollback();
                                    return Json(new { success = false, message = "Teacher not found." });
                                }
                            }
                            
                            // Delete from TeacherDetails first (child table)
                            string deleteDetailsQuery = "DELETE FROM TeacherDetails WHERE UserId = @UserId";
                            using (var detailsCommand = new SqlCommand(deleteDetailsQuery, connection, transaction))
                            {
                                detailsCommand.Parameters.AddWithValue("@UserId", userId);
                                int detailsRowsAffected = await detailsCommand.ExecuteNonQueryAsync();
                                _logger.LogInformation($"Deleted {detailsRowsAffected} row(s) from TeacherDetails");
                            }
                            
                            // Delete from Users (parent table)
                            string deleteUserQuery = "DELETE FROM Users WHERE UserId = @UserId";
                            using (var userCommand = new SqlCommand(deleteUserQuery, connection, transaction))
                            {
                                userCommand.Parameters.AddWithValue("@UserId", userId);
                                int userRowsAffected = await userCommand.ExecuteNonQueryAsync();
                                _logger.LogInformation($"Deleted {userRowsAffected} row(s) from Users");
                            }
                        }
                        else
                        {
                            // Check if there's a direct Teachers table
                            bool teachersTableExists = await TableExists(connection, "Teachers");
                            
                            if (teachersTableExists)
                            {
                                _logger.LogInformation("Using old table schema (Teachers)");
                                
                                // Delete the teacher directly
                                string deleteQuery = "DELETE FROM Teachers WHERE TeacherId = @TeacherId";
                                using (var deleteCommand = new SqlCommand(deleteQuery, connection, transaction))
                                {
                                    deleteCommand.Parameters.AddWithValue("@TeacherId", teacherId);
                                    int rowsAffected = await deleteCommand.ExecuteNonQueryAsync();
                                    
                                    _logger.LogInformation($"Deleted {rowsAffected} row(s) from Teachers table");
                                    
                                    if (rowsAffected == 0)
                                    {
                                        transaction.Rollback();
                                        return Json(new { success = false, message = "Teacher not found." });
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogError("No teacher tables found in database");
                                transaction.Rollback();
                                return Json(new { success = false, message = "No teacher tables found in database." });
                            }
                        }
                        
                        // Commit the transaction
                        transaction.Commit();
                        _logger.LogInformation("Transaction committed successfully");
                        return Json(new { success = true, message = "Teacher deleted successfully." });
                    }
                    catch (Exception ex)
                    {
                        // Roll back the transaction
                        transaction.Rollback();
                        _logger.LogError($"Error in transaction, rolled back: {ex.Message}");
                        return Json(new { success = false, message = $"Database error: {ex.Message}" });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in DeleteTeacher: {ex.Message}");
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
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
    public async Task<IActionResult> UpdateTeacher(string TeacherId, string FullName, string Username, string Password, string Department, string Position)
    {
        _logger.LogInformation($"UpdateTeacher called with ID: {TeacherId}, Name: {FullName}, Username: {Username}, Department: {Department}, Position: {Position}");
        
        if (string.IsNullOrEmpty(TeacherId))
        {
            TempData["Error"] = "Teacher ID is required";
            return RedirectToAction("AdminDashboard");
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check which tables exist
                bool teacherDetailsExists = await TableExists(connection, "TeacherDetails");
                bool usersTableExists = await TableExists(connection, "Users");
                bool teachersTableExists = await TableExists(connection, "Teachers");
                
                _logger.LogInformation($"Table check: TeacherDetails={teacherDetailsExists}, Users={usersTableExists}, Teachers={teachersTableExists}");
                
                if (teacherDetailsExists && usersTableExists)
                {
                    _logger.LogInformation("Using new table schema (TeacherDetails + Users)");
                    
                    // First, check if teacher exists and get the UserId
                    string getUserIdQuery = "SELECT UserId FROM TeacherDetails WHERE UserId = @TeacherId";
                    string userId = TeacherId; // Initially assume TeacherId is the UserId
                    
                    using (var command = new SqlCommand(getUserIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@TeacherId", TeacherId);
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result == null)
                        {
                            _logger.LogWarning($"Teacher with ID {TeacherId} not found in TeacherDetails");
                            TempData["Error"] = "Teacher not found";
                            return RedirectToAction("AdminDashboard");
                        }
                        
                        userId = result.ToString();
                        _logger.LogInformation($"Found teacher with UserId: {userId}");
                    }
                    
                    // Update Users table
                    string updateUserQuery = @"
                        UPDATE Users 
                        SET FullName = @FullName, 
                            Username = @Username";
                    
                    // Only update password if it's provided
                    if (!string.IsNullOrEmpty(Password))
                    {
                        updateUserQuery += ", Password = @Password";
                    }
                    
                    updateUserQuery += " WHERE UserId = @UserId";
                    
                    using (var command = new SqlCommand(updateUserQuery, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", FullName);
                        command.Parameters.AddWithValue("@Username", Username);
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        if (!string.IsNullOrEmpty(Password))
                        {
                            command.Parameters.AddWithValue("@Password", Password);
                        }
                        
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    // Update TeacherDetails table
                    string updateDetailsQuery = @"
                        UPDATE TeacherDetails 
                        SET Department = @Department, 
                            Position = @Position
                        WHERE UserId = @UserId";
                    
                    using (var command = new SqlCommand(updateDetailsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Department", Department);
                        command.Parameters.AddWithValue("@Position", Position);
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            TempData["Warning"] = "No changes were made to teacher details";
                            return RedirectToAction("AdminDashboard");
                        }
                    }
                    
                    TempData["Success"] = "Teacher updated successfully";
                    return RedirectToAction("AdminDashboard");
                }
                else if (teachersTableExists)
                {
                    _logger.LogInformation("Using old table schema (Teachers)");
                    
                    // Check if teacher exists
                    string checkQuery = "SELECT COUNT(*) FROM Teachers WHERE TeacherId = @TeacherId";
                    using (var checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@TeacherId", TeacherId);
                        int count = (int)await checkCmd.ExecuteScalarAsync();
                        
                        if (count == 0)
                        {
                            TempData["Error"] = "Teacher not found";
                            return RedirectToAction("AdminDashboard");
                        }
                    }
                    
                    // Build the update query based on provided parameters
                    string updateQuery = @"
                        UPDATE Teachers 
                        SET FullName = @FullName, 
                            Username = @Username, 
                            Department = @Department, 
                            Position = @Position";
                    
                    // Only update password if it's provided
                    if (!string.IsNullOrEmpty(Password))
                    {
                        updateQuery += ", Password = @Password";
                    }
                    
                    updateQuery += " WHERE TeacherId = @TeacherId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", FullName);
                        command.Parameters.AddWithValue("@Username", Username);
                        command.Parameters.AddWithValue("@Department", Department);
                        command.Parameters.AddWithValue("@Position", Position);
                        command.Parameters.AddWithValue("@TeacherId", TeacherId);
                        
                        if (!string.IsNullOrEmpty(Password))
                        {
                            command.Parameters.AddWithValue("@Password", Password);
                        }
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected == 0)
                        {
                            TempData["Warning"] = "No changes were made";
                            return RedirectToAction("AdminDashboard");
                        }
                    }
                    
                    TempData["Success"] = "Teacher updated successfully";
                    return RedirectToAction("AdminDashboard");
                }
                else
                {
                    _logger.LogError("No teacher tables found in database");
                    TempData["Error"] = "No teacher tables found in database";
                    return RedirectToAction("AdminDashboard");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating teacher {TeacherId}: {ex.Message}");
            TempData["Error"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("AdminDashboard");
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
}