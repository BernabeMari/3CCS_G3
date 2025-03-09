using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient; // Use SqlClient for MSSQL
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using StudentBadge.Data;

public class DashboardController : Controller
{
    private readonly string _connectionString;

    public DashboardController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("YourConnectionString");
    }

    public IActionResult StudentDashboard()
    {
        ViewBag.FullName = HttpContext.Session.GetString("FullName");
        ViewBag.IdNumber = HttpContext.Session.GetString("IdNumber");
        ViewBag.Course = HttpContext.Session.GetString("Course");
        ViewBag.Section = HttpContext.Session.GetString("Section");

        var allStudents = new List<Student>();

        using (var connection = new SqlConnection(_connectionString)) // Use SqlConnection for MSSQL
        {
            connection.Open();
            string query = "SELECT IdNumber, FullName, Course, Section, IsProfileVisible FROM Students";

            using (var command = new SqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    allStudents.Add(new Student
                    {
                        IdNumber = reader["IdNumber"].ToString(),
                        FullName = reader["FullName"].ToString(),
                        Course = reader["Course"].ToString(),
                        Section = reader["Section"].ToString(),
                        IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"])
                    });
                }
            }
        }

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

        using (var connection = new SqlConnection(_connectionString)) // Use SqlConnection for MSSQL
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
        string idNumber = HttpContext.Session.GetString("IdNumber"); // Get ID from session

        if (string.IsNullOrEmpty(idNumber))
        {
            return RedirectToAction("Login", "Account");
        }

        bool isProfileVisible = false;

        using (var connection = new SqlConnection(_connectionString))
        {
            connection.Open();
            string query = "SELECT IsProfileVisible FROM Students WHERE IdNumber = @IdNumber";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@IdNumber", idNumber);

                var result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    isProfileVisible = Convert.ToBoolean(result);
                }
            }
        }

        ViewBag.IsProfileVisible = isProfileVisible; // Pass status to the view
        return View();
    }

    public IActionResult Login()
    {
        return View();
    }

}

// ✅ Move PrivacySettingModel OUTSIDE the controller
public class PrivacySettingModel
{
    public bool IsVisible { get; set; }
}
