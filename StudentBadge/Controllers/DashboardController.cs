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
            string query = "SELECT Score, ProfilePicturePath, ResumePath FROM Students WHERE IdNumber = @IdNumber";

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
                        string resumePath = reader["ResumePath"] as string;
                        ViewBag.ResumePath = !string.IsNullOrEmpty(resumePath) ? resumePath : null;
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
            string query = "SELECT IdNumber, FullName, Course, Section, IsProfileVisible, ProfilePicturePath, ResumePath, Score, Achievements, Comments, BadgeColor, IsResumeVisible FROM Students ORDER BY Score DESC";

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
                        ResumePath = reader["ResumePath"] as string,
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
        string profilePicturePath = null;
        string resumePath = null;
        int score = 0;
        string achievements = "";
        string comments = "";
        string badgeColor = "";

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "SELECT IsProfileVisible, IsResumeVisible, ProfilePicturePath, ResumePath, Score, Achievements, Comments, BadgeColor FROM Students WHERE IdNumber = @IdNumber";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdNumber", idNumber);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        isProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                        isResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"]);
                        profilePicturePath = reader["ProfilePicturePath"] as string;
                        resumePath = reader["ResumePath"] as string;
                        score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0;
                        achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "";
                        comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "";
                        badgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None";
                    }
                }
            }
        }

        ViewBag.IsProfileVisible = isProfileVisible;
        ViewBag.IsResumeVisible = isResumeVisible;
        ViewBag.ProfilePicturePath = profilePicturePath;
        ViewBag.ResumePath = resumePath;
        ViewBag.Score = score;
        ViewBag.Achievements = achievements;
        ViewBag.Comments = comments;
        ViewBag.BadgeColor = badgeColor;

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

            byte[] imageBytes = Convert.FromBase64String(base64Image);

            // Check for reasonable size
            if (imageBytes.Length > 5 * 1024 * 1024) // 5MB limit
            {
                return Json(new { success = false, message = "Image too large. Maximum size is 5MB." });
            }

            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            // Generate a unique filename based on ID and timestamp
            string fileName = $"{idNumber}_{DateTime.Now.Ticks}.jpg";
            string filePath = Path.Combine("uploads", "profilepictures", fileName);
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", filePath);

            // Save the file to disk
            await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes);

            // Store the relative path in the database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "UPDATE Students SET ProfilePicturePath = @ProfilePicturePath WHERE IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ProfilePicturePath", "/" + filePath.Replace("\\", "/"));
                    command.Parameters.AddWithValue("@IdNumber", idNumber);

                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        return Json(new
                        {
                            success = true,
                            message = "Profile picture saved successfully.",
                            imageUrl = "/" + filePath.Replace("\\", "/") // Return the URL for immediate display
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

            byte[] fileBytes = Convert.FromBase64String(base64File);

            // Check for reasonable size
            if (fileBytes.Length > 10 * 1024 * 1024) // 10MB limit
            {
                return Json(new { success = false, message = "Resume too large. Maximum size is 10MB." });
            }

            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            // Generate a unique filename based on ID and timestamp
            string safeFileName = $"{idNumber}_{DateTime.Now.Ticks}_{Path.GetFileNameWithoutExtension(fileName)}{Path.GetExtension(fileName)}";
            string filePath = Path.Combine("uploads", "resumes", safeFileName);
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", filePath);

            // Ensure directory exists
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "resumes");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save the file to disk
            await System.IO.File.WriteAllBytesAsync(fullPath, fileBytes);

            // Store the relative path in the database
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "UPDATE Students SET ResumePath = @ResumePath WHERE IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ResumePath", "/" + filePath.Replace("\\", "/"));
                    command.Parameters.AddWithValue("@IdNumber", idNumber);

                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        return Json(new
                        {
                            success = true,
                            message = "Resume uploaded successfully.",
                            resumeUrl = "/" + filePath.Replace("\\", "/") // Return the URL for immediate display
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