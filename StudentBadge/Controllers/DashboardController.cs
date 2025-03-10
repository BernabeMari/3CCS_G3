using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient; // Use SqlClient for MSSQL
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

        var allStudents = new List<Student>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            string query = "SELECT IdNumber, FullName, Course, Section, IsProfileVisible, ProfilePicture FROM Students"; // Ensure ProfilePicture is part of the query

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
                        ProfilePicture = reader["ProfilePicture"] as byte[] // Ensure you're fetching it as a byte[]
                    });
                }
            }
        }


        // Add the list of students to ViewBag to be accessed in the view
        ViewBag.AllStudents = allStudents;

        return View();
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
        string idNumber = HttpContext.Session.GetString("IdNumber");

        if (string.IsNullOrEmpty(idNumber))
        {
            return RedirectToAction("Login", "Account");
        }

        bool isProfileVisible = false;
        byte[] profilePicture = null;

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "SELECT IsProfileVisible, ProfilePicture FROM Students WHERE IdNumber = @IdNumber";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdNumber", idNumber);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        isProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]);
                        profilePicture = reader["ProfilePicture"] as byte[];
                    }
                }
            }
        }

        ViewBag.IsProfileVisible = isProfileVisible;

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
    public IActionResult SaveProfilePicture([FromBody] ProfilePictureModel model)
    {
        if (model != null && !string.IsNullOrEmpty(model.Base64Image))
        {
            try
            {
                // Decode the Base64 image string into a byte array
                var base64Image = model.Base64Image;

                if (!IsValidBase64(base64Image))
                {
                    return Json(new { success = false, message = "Invalid image format." });
                }

                byte[] imageBytes = Convert.FromBase64String(base64Image);

                // Update the profile picture in the database
                string idNumber = HttpContext.Session.GetString("IdNumber");

                if (string.IsNullOrEmpty(idNumber))
                {
                    return Json(new { success = false, message = "User not authenticated." });
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    string query = "UPDATE Students SET ProfilePicture = @ProfilePicture WHERE IdNumber = @IdNumber";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ProfilePicture", imageBytes);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        command.ExecuteNonQuery();
                    }
                }

                return Json(new { success = true, message = "Profile picture saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        return Json(new { success = false, message = "Invalid data." });
    }

    private bool IsValidBase64(string base64String)
    {
        try
        {
            Convert.FromBase64String(base64String);
            return true;
        }
        catch
        {
            return false; // Invalid Base64 string
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
