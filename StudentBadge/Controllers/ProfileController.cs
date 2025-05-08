using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Data.SqlClient;
using System.IO;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class ProfileController : Controller
    {
        private readonly string _connectionString;

        public ProfileController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost("UpdatePrivacySetting")]
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

        [HttpPost("UpdateResumeVisibility")]
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
}
