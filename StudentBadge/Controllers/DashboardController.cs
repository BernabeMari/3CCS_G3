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
            string query = "SELECT Score, ProfilePicture FROM Students WHERE IdNumber = @IdNumber";

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

                        // Get profile picture
                        byte[] profilePicture = reader["ProfilePicture"] as byte[];
                        if (profilePicture != null && profilePicture.Length > 0)
                        {
                            ViewBag.Base64Image = Convert.ToBase64String(profilePicture);
                        }
                        else
                        {
                            ViewBag.Base64Image = null;
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
            // Including all needed fields including Score, Achievements, Comments, and BadgeColor
            string query = "SELECT IdNumber, FullName, Course, Section, IsProfileVisible, ProfilePicture, Score, Achievements, Comments, BadgeColor FROM Students ORDER BY Score DESC";

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
                        ProfilePicture = reader["ProfilePicture"] as byte[],
                        Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                        Achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "",
                        Comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "",
                        BadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None"
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

    public IActionResult StudentProfile()
    {
        ViewBag.FullName = HttpContext.Session.GetString("FullName");
        ViewBag.IdNumber = HttpContext.Session.GetString("IdNumber");
        ViewBag.Course = HttpContext.Session.GetString("Course");
        ViewBag.Section = HttpContext.Session.GetString("Section");

        string idNumber = HttpContext.Session.GetString("IdNumber");

        if (string.IsNullOrEmpty(idNumber))
        {
            return RedirectToAction("Login", "Home"); // Changed to match your Login action
        }

        bool isProfileVisible = false;
        byte[] profilePicture = null;
        int score = 0;
        string achievements = "";
        string comments = "";
        string badgeColor = "";

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "SELECT IsProfileVisible, ProfilePicture, Score, Achievements, Comments, BadgeColor FROM Students WHERE IdNumber = @IdNumber";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdNumber", idNumber);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        isProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                        profilePicture = reader["ProfilePicture"] as byte[];
                        score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0;
                        achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "";
                        comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "";
                        badgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None";
                    }
                }
            }
        }

        ViewBag.IsProfileVisible = isProfileVisible;
        ViewBag.Score = score;
        ViewBag.Achievements = achievements;
        ViewBag.Comments = comments;
        ViewBag.BadgeColor = badgeColor;

        // Ensure profilePicture is not null and has a valid length
        if (profilePicture != null && profilePicture.Length > 0)
        {
            // Convert the byte array to a Base64 string
            ViewBag.Base64Image = Convert.ToBase64String(profilePicture);
        }
        else
        {
            ViewBag.Base64Image = null; // In case there is no image
        }

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

            // Check for reasonable size (prevent too large/small files)
            if (imageBytes.Length > 5 * 1024 * 1024) // 5MB limit
            {
                return Json(new { success = false, message = "Image too large. Maximum size is 5MB." });
            }

            // Update the profile picture in the database
            string idNumber = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(idNumber))
            {
                return Json(new { success = false, message = "User not authenticated." });
            }

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                string query = "UPDATE Students SET ProfilePicture = @ProfilePicture WHERE IdNumber = @IdNumber";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.Add("@ProfilePicture", System.Data.SqlDbType.VarBinary, imageBytes.Length).Value = imageBytes;
                    command.Parameters.AddWithValue("@IdNumber", idNumber);

                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        return Json(new
                        {
                            success = true,
                            message = "Profile picture saved successfully.",
                            imageData = "data:image/jpeg;base64," + base64Image // Return with proper format for immediate display
                        });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update profile picture. User not found." });
                    }
                }
            }
        }
        catch (FormatException ex)
        {
            return Json(new { success = false, message = "Invalid image format: " + ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Error saving profile picture: " + ex.Message });
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

public class ProfilePictureModel
{
    public string Base64Image { get; set; }
}