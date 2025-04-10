using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace StudentBadge.Controllers
{
    public class VideoCallController : Controller
    {
        private readonly string _connectionString;

        public VideoCallController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Student's video call page
        [HttpGet]
        public IActionResult StudentVideoCall(int callId)
        {
            // Check if student is authenticated
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }

            ViewBag.CallId = callId;
            ViewBag.StudentId = studentId;
            ViewBag.UserType = "student";
            return View();
        }

        // Employer's video call page
        [HttpGet]
        public IActionResult EmployerVideoCall(int callId)
        {
            // Check if employer is authenticated
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return RedirectToAction("Login", "Home");
            }

            ViewBag.CallId = callId;
            ViewBag.EmployerId = employerId;
            ViewBag.UserType = "employer";
            return View();
        }

        // API to get call details
        [HttpGet]
        public async Task<IActionResult> GetCallDetails(int callId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        SELECT v.CallId, v.EmployerId, v.StudentId, v.Status, v.StartTime, 
                               u1.FullName AS EmployerName, ed.Company, ed.ProfilePicturePath AS EmployerProfilePic,
                               u2.FullName AS StudentName, sd.ProfilePicturePath AS StudentProfilePic,
                               sd.Course, sd.Section, sd.Score, sd.BadgeColor, sd.Achievements, 
                               sd.IdNumber AS StudentIdNumber
                        FROM VideoCalls v
                        JOIN Users u1 ON v.EmployerId = u1.UserId
                        JOIN EmployerDetails ed ON v.EmployerId = ed.UserId
                        JOIN Users u2 ON v.StudentId = u2.UserId
                        JOIN StudentDetails sd ON v.StudentId = sd.UserId
                        WHERE v.CallId = @CallId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Extract profile picture from database - it may be stored as a data URL or another format
                                string studentProfilePic = "/images/blank.jpg";
                                if (reader["StudentProfilePic"] != DBNull.Value)
                                {
                                    var profilePicData = reader["StudentProfilePic"].ToString();
                                    
                                    // If already in data URL format, use directly
                                    if (profilePicData.StartsWith("data:"))
                                    {
                                        studentProfilePic = profilePicData;
                                    }
                                    // If it's a raw value but not a path (might be base64 without the prefix)
                                    else if (!profilePicData.StartsWith("/") && !string.IsNullOrEmpty(profilePicData))
                                    {
                                        // Try to convert assuming it's base64 encoded image data
                                        try {
                                            // Add the data URL prefix if missing
                                            studentProfilePic = profilePicData.Contains("data:") 
                                                ? profilePicData 
                                                : "data:image/jpeg;base64," + profilePicData;
                                        }
                                        catch {
                                            studentProfilePic = "/images/blank.jpg";
                                        }
                                    }
                                    // Use file path if it looks like one
                                    else if (profilePicData.StartsWith("/"))
                                    {
                                        studentProfilePic = profilePicData;
                                    }
                                }

                                var callDetails = new
                                {
                                    callId = Convert.ToInt32(reader["CallId"]),
                                    employerId = reader["EmployerId"].ToString(),
                                    studentId = reader["StudentId"].ToString(),
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]),
                                    employerName = reader["EmployerName"].ToString(),
                                    company = reader["Company"].ToString(),
                                    studentName = reader["StudentName"].ToString(),
                                    employerProfilePic = reader["EmployerProfilePic"] != DBNull.Value ? reader["EmployerProfilePic"].ToString() : "/images/blank.jpg",
                                    studentProfilePic = studentProfilePic,
                                    studentIdNumber = reader["StudentIdNumber"].ToString(),
                                    studentCourse = reader["Course"] != DBNull.Value ? reader["Course"].ToString() : "N/A",
                                    studentSection = reader["Section"] != DBNull.Value ? reader["Section"].ToString() : "N/A",
                                    studentScore = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                                    studentBadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "green",
                                    studentAchievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : ""
                                };
                                return Json(new { success = true, call = callDetails });
                            }
                            else
                            {
                                return Json(new { success = false, message = "Call not found" });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error retrieving call details: " + ex.Message });
            }
        }

        // Get calls for a student
        [HttpGet]
        public async Task<IActionResult> GetStudentCalls()
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student not authenticated" });
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        SELECT v.CallId, v.EmployerId, v.Status, v.StartTime, 
                               u.FullName AS EmployerName, ed.Company
                        FROM VideoCalls v
                        JOIN Users u ON v.EmployerId = u.UserId
                        JOIN EmployerDetails ed ON v.EmployerId = ed.UserId
                        WHERE v.StudentId = @StudentId
                        ORDER BY v.StartTime DESC";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var calls = new List<object>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                calls.Add(new
                                {
                                    callId = Convert.ToInt32(reader["CallId"]),
                                    employerId = reader["EmployerId"].ToString(),
                                    employerName = reader["EmployerName"].ToString(),
                                    company = reader["Company"].ToString(),
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]).ToString("yyyy-MM-dd HH:mm:ss")
                                });
                            }
                        }

                        return Json(new { success = true, calls = calls });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error retrieving calls: " + ex.Message });
            }
        }

        // Get calls for an employer
        [HttpGet]
        public async Task<IActionResult> GetEmployerCalls()
        {
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer not authenticated" });
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    string query = @"
                        SELECT v.CallId, v.StudentId, v.Status, v.StartTime, 
                               u.FullName AS StudentName, sd.Course
                        FROM VideoCalls v
                        JOIN Users u ON v.StudentId = u.UserId
                        JOIN StudentDetails sd ON v.StudentId = sd.UserId
                        WHERE v.EmployerId = @EmployerId
                        ORDER BY v.StartTime DESC";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        var calls = new List<object>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                calls.Add(new
                                {
                                    callId = Convert.ToInt32(reader["CallId"]),
                                    studentId = reader["StudentId"].ToString(),
                                    studentName = reader["StudentName"].ToString(),
                                    course = reader["Course"].ToString(),
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]).ToString("yyyy-MM-dd HH:mm:ss")
                                });
                            }
                        }

                        return Json(new { success = true, calls = calls });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error retrieving calls: " + ex.Message });
            }
        }
    }
} 
