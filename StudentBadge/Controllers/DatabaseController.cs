using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Generic;

namespace StudentBadge.Controllers
{
    public class DatabaseController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseController> _logger;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public DatabaseController(IConfiguration configuration, ILogger<DatabaseController> logger, IWebHostEnvironment hostingEnvironment)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _hostingEnvironment = hostingEnvironment;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Migrate()
        {
            var results = new StringBuilder();
            
            try
            {
                string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "DatabaseMigration.sql");
                
                if (!System.IO.File.Exists(scriptPath))
                {
                    // If the file doesn't exist in the SQL folder, try a different location
                    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "StudentBadge", "SQL", "DatabaseMigration.sql");
                    
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return View("Error", "Migration script not found. Please make sure the DatabaseMigration.sql file exists.");
                    }
                }
                
                string script = await System.IO.File.ReadAllTextAsync(scriptPath);
                
                // Split the script by GO statements
                string[] batches = script.Split(new[] { "GO", "go" }, StringSplitOptions.RemoveEmptyEntries);
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    foreach (string batch in batches)
                    {
                        if (!string.IsNullOrWhiteSpace(batch))
                        {
                            using (var command = new SqlCommand(batch, connection))
                            {
                                try
                                {
                                    await command.ExecuteNonQueryAsync();
                                    // Capture PRINT messages
                                    results.AppendLine($"Executed: {batch.Substring(0, Math.Min(50, batch.Length))}...");
                                }
                                catch (Exception ex)
                                {
                                    results.AppendLine($"Error: {ex.Message} in batch: {batch.Substring(0, Math.Min(50, batch.Length))}...");
                                }
                            }
                        }
                    }
                }
                
                ViewBag.Results = results.ToString();
                ViewBag.Success = true;
                return View("MigrationResults");
            }
            catch (Exception ex)
            {
                ViewBag.Results = $"Migration error: {ex.Message}";
                ViewBag.Success = false;
                return View("MigrationResults");
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveScoreColumn()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string scriptPath = Path.Combine(_hostingEnvironment.ContentRootPath, "Data", "RemoveScoreColumn.sql");
                    string sqlScript = await System.IO.File.ReadAllTextAsync(scriptPath);
                    
                    using (var command = new SqlCommand(sqlScript, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    return Json(new { success = true, message = "Score column has been removed successfully." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing Score column: {Message}", ex.Message);
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAllStudentScores()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First, get all students
                    string getStudentsQuery = @"
                        SELECT 
                            sd.IdNumber,
                            u.FullName,
                            sd.Course,
                            sd.Section,
                            sd.Achievements,
                            sd.Comments,
                            sd.FirstYearGrade,
                            sd.SecondYearGrade,
                            sd.ThirdYearGrade,
                            sd.FourthYearGrade
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId";
                    
                    var students = new List<Models.Student>();
                    
                    using (var command = new SqlCommand(getStudentsQuery, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                students.Add(new Models.Student
                                {
                                    IdNumber = reader["IdNumber"].ToString(),
                                    FullName = reader["FullName"].ToString(),
                                    Course = reader["Course"].ToString(),
                                    Section = reader["Section"].ToString(),
                                    Achievements = reader["Achievements"]?.ToString(),
                                    Comments = reader["Comments"]?.ToString(),
                                    FirstYearGrade = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : (decimal?)null,
                                    SecondYearGrade = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : (decimal?)null,
                                    ThirdYearGrade = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : (decimal?)null,
                                    FourthYearGrade = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : (decimal?)null
                                });
                            }
                        }
                    }
                    
                    int updatedCount = 0;
                    
                    // Get all challenge submissions and populate them for each student
                    foreach (var student in students)
                    {
                        // Calculate the student's grade level
                        student.GradeLevel = student.CalculateGradeLevel();
                        
                        // Get completed challenges for this student
                        string userId = null;
                        string getUserIdQuery = @"
                            SELECT UserId 
                            FROM Users 
                            WHERE Username = @IdNumber";
                        
                        using (var cmd = new SqlCommand(getUserIdQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@IdNumber", student.IdNumber);
                            var result = await cmd.ExecuteScalarAsync();
                            
                            if (result != null && result != DBNull.Value)
                            {
                                userId = result.ToString();
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(userId))
                        {
                            student.CompletedChallenges = new List<Models.ChallengeSubmission>();
                            
                            string challengeQuery = @"
                                SELECT s.*, c.ChallengeName, c.ProgrammingLanguage, c.Description
                                FROM ChallengeSubmissions s
                                JOIN Challenges c ON s.ChallengeId = c.ChallengeId
                                WHERE s.StudentId = @StudentId";
                            
                            using (var cmd = new SqlCommand(challengeQuery, connection))
                            {
                                cmd.Parameters.AddWithValue("@StudentId", userId);
                                
                                using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
                                    {
                                        student.CompletedChallenges.Add(new Models.ChallengeSubmission
                                        {
                                            SubmissionId = Convert.ToInt32(reader["SubmissionId"]),
                                            ChallengeId = Convert.ToInt32(reader["ChallengeId"]),
                                            StudentId = userId,
                                            SubmissionDate = Convert.ToDateTime(reader["SubmissionDate"]),
                                            ChallengeName = reader["ChallengeName"].ToString(),
                                            ProgrammingLanguage = reader["ProgrammingLanguage"].ToString(),
                                            Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : "",
                                            PercentageScore = reader["PercentageScore"] != DBNull.Value ? Convert.ToInt32(reader["PercentageScore"]) : 0
                                        });
                                    }
                                }
                            }
                            
                            // Get total available challenges
                            string availableChallengesQuery = @"
                                SELECT COUNT(*) 
                                FROM Challenges 
                                WHERE IsActive = 1";
                            
                            using (var cmd = new SqlCommand(availableChallengesQuery, connection))
                            {
                                var result = await cmd.ExecuteScalarAsync();
                                student.TotalAvailableChallenges = result != null && result != DBNull.Value ? 
                                    Convert.ToInt32(result) : 0;
                            }
                            
                            // Get total available challenge points
                            string totalPointsQuery = @"
                                SELECT SUM(q.Points) 
                                FROM ChallengeQuestions q
                                JOIN Challenges c ON q.ChallengeId = c.ChallengeId
                                WHERE c.IsActive = 1";
                            
                            using (var cmd = new SqlCommand(totalPointsQuery, connection))
                            {
                                var result = await cmd.ExecuteScalarAsync();
                                student.TotalAvailableChallengePoints = result != null && result != DBNull.Value ? 
                                    Convert.ToDecimal(result) : 0;
                            }
                            
                            // Get student's total test scores from ALL year levels 1-4
                            string studentScoresQuery = @"
                                SELECT SUM(Score) AS TotalScore
                                FROM StudentTests 
                                WHERE StudentId = @StudentId
                                AND YearLevel BETWEEN 1 AND 4";
                            
                            try 
                            {
                                using (var cmd = new SqlCommand(studentScoresQuery, connection))
                                {
                                    cmd.Parameters.AddWithValue("@StudentId", userId);
                                    var result = await cmd.ExecuteScalarAsync();
                                    student.TotalMasteryScoreAllYears = result != null && result != DBNull.Value ? 
                                        Convert.ToDecimal(result) : 0;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Error retrieving student test scores: {ex.Message}");
                                student.TotalMasteryScoreAllYears = 0;
                            }
                            
                            // Get total items (questions) from ALL tests across ALL year levels 1-4
                            string totalTestItemsQuery = @"
                                SELECT SUM(TotalItems) AS TotalItems
                                FROM Tests 
                                WHERE YearLevel BETWEEN 1 AND 4
                                AND IsActive = 1";
                            
                            try 
                            {
                                using (var cmd = new SqlCommand(totalTestItemsQuery, connection))
                                {
                                    var result = await cmd.ExecuteScalarAsync();
                                    student.TotalMasteryItemsAllYears = result != null && result != DBNull.Value ? 
                                        Convert.ToDecimal(result) : 0;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Error retrieving total test items: {ex.Message}");
                                // Don't use a fixed fallback value, just log the error and set to 0
                                student.TotalMasteryItemsAllYears = 0;
                            }
                            
                            // Calculate the score
                            student.CalculateOverallScore();
                            
                            decimal calculatedScore = student.Score;
                            
                            // Get badge color based on calculated score
                            string badgeColor = student.Score >= 95 ? "platinum" : 
                                               student.Score >= 85 ? "gold" : 
                                               student.Score >= 75 ? "silver" : 
                                               student.Score >= 65 ? "bronze" : 
                                               student.Score >= 50 ? "rising-star" : 
                                               student.Score >= 1 ? "needs" : "none";
                            
                            // Update the score and badge color in the database
                            string updateSql = @"
                                UPDATE StudentDetails
                                SET Score = @Score, BadgeColor = @BadgeColor
                                WHERE IdNumber = @StudentId";
                            
                            using (var cmd = new SqlCommand(updateSql, connection))
                            {
                                cmd.Parameters.AddWithValue("@StudentId", student.IdNumber);
                                cmd.Parameters.AddWithValue("@Score", calculatedScore);
                                cmd.Parameters.AddWithValue("@BadgeColor", badgeColor);
                                
                                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                                if (rowsAffected > 0)
                                {
                                    updatedCount++;
                                }
                            }
                        }
                    }
                    
                    return Json(new { success = true, message = $"Updated scores for {updatedCount} students." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating student scores: {Message}", ex.Message);
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
} 