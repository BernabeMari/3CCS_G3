using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using System;
using System.Data.SqlClient;
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
            _connectionString = configuration.GetConnectionString("YourConnectionString");
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
                               e.FullName AS EmployerName, e.Company, e.ProfilePicturePath AS EmployerProfilePic,
                               s.FullName AS StudentName, s.ProfilePicturePath AS StudentProfilePic
                        FROM VideoCalls v
                        JOIN Employers e ON v.EmployerId = e.EmployerId
                        JOIN Students s ON v.StudentId = s.IdNumber
                        WHERE v.CallId = @CallId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
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
                                    employerProfilePic = reader["EmployerProfilePic"] != DBNull.Value ? reader["EmployerProfilePic"].ToString() : "/uploads/profilepictures/blank.jpg",
                                    studentProfilePic = reader["StudentProfilePic"] != DBNull.Value ? 
                                        (reader["StudentProfilePic"].ToString().StartsWith("data:") ? 
                                            reader["StudentProfilePic"].ToString() : 
                                            (reader["StudentProfilePic"].ToString().StartsWith("/") ? 
                                                reader["StudentProfilePic"].ToString() : 
                                                "/uploads/profilepictures/blank.jpg")) : 
                                        "/uploads/profilepictures/blank.jpg"
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
                               e.FullName AS EmployerName, e.Company
                        FROM VideoCalls v
                        JOIN Employers e ON v.EmployerId = e.EmployerId
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
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]),
                                    employerName = reader["EmployerName"].ToString(),
                                    company = reader["Company"].ToString()
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
                               s.FullName AS StudentName, s.Course
                        FROM VideoCalls v
                        JOIN Students s ON v.StudentId = s.IdNumber
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
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]),
                                    studentName = reader["StudentName"].ToString(),
                                    course = reader["Course"].ToString()
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