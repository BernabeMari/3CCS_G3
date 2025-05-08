using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using StudentBadge.Services;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace StudentBadge.Controllers
{
    public class ChallengeController : Controller
    {
        private readonly string _connectionString;
        private readonly CertificateService _certificateService;
        private readonly ILogger<ChallengeController> _logger;
        private readonly BadgeService _badgeService;
        
        public ChallengeController(IConfiguration configuration, CertificateService certificateService, 
            ILogger<ChallengeController> logger, BadgeService badgeService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _certificateService = certificateService;
            _logger = logger;
            _badgeService = badgeService;
        }
        
        public async Task<IActionResult> Index()
        {
            // Get current teacher information from session
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            var challenges = await GetTeacherChallenges(teacherId);
            
            // Group challenges by school year
            var challengesByYear = challenges
                .GroupBy(c => c.YearLevel)
                .Select(g => new
                {
                    SchoolYear = g.Key,
                    ChallengeCount = g.Count(),
                    TotalQuestions = g.Sum(c => c.Questions.Count),
                    FirstCreatedDate = g.Min(c => c.CreatedDate)
                })
                .OrderByDescending(g => g.FirstCreatedDate)
                .ToList();
            
            return View(challengesByYear);
        }
        
        [HttpGet]
        public async Task<IActionResult> ChallengesByYear(string yearLevel)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            var allChallenges = await GetTeacherChallenges(teacherId);
            
            // Filter challenges by the selected year
            var challenges = allChallenges
                .Where(c => c.YearLevel == yearLevel)
                .OrderByDescending(c => c.CreatedDate)
                .ToList();
            
            // Load questions for each challenge
            foreach (var challenge in challenges)
            {
                challenge.Questions = await GetChallengeQuestions(challenge.ChallengeId);
            }
            
            ViewBag.SchoolYear = yearLevel;
            
            // Check if the school year is visible to students
            bool isYearVisible = await IsSchoolYearVisible(teacherId, yearLevel);
            ViewBag.IsYearVisible = isYearVisible;
            
            return View(challenges);
        }
        
        [HttpPost]
        public async Task<IActionResult> ToggleSchoolYearVisibility(string yearLevel, bool isVisible)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            bool success = await SetSchoolYearVisibility(teacherId, yearLevel, isVisible);
            
            if (success)
            {
                TempData["Success"] = isVisible ? 
                    $"School year {yearLevel} is now visible to students" : 
                    $"School year {yearLevel} is now hidden from students";
            }
            else
            {
                TempData["Error"] = "Failed to update school year visibility";
            }
            
            return RedirectToAction("ChallengesByYear", new { yearLevel });
        }
        
        private async Task<bool> IsSchoolYearVisible(string teacherId, string yearLevel)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // First check if the table exists
                bool tableExists = await TableExists(connection, "SchoolYearVisibility");
                
                if (!tableExists)
                {
                    // Create the table if it doesn't exist
                    string createTableQuery = @"
                        CREATE TABLE SchoolYearVisibility (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            TeacherId NVARCHAR(128) NOT NULL,
                            YearLevel NVARCHAR(20) NOT NULL,
                            IsVisible BIT NOT NULL DEFAULT 1,
                            CONSTRAINT UQ_SchoolYearVisibility UNIQUE (TeacherId, YearLevel)
                        )";
                    
                    using (SqlCommand command = new SqlCommand(createTableQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    // By default, return true (visible) for new school years
                    return true;
                }
                
                // Check visibility setting
                string query = @"
                    SELECT IsVisible
                    FROM SchoolYearVisibility
                    WHERE TeacherId = @TeacherId AND YearLevel = @YearLevel";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", teacherId);
                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToBoolean(result);
                    }
                    else
                    {
                        // No record found, insert default (visible)
                        string insertQuery = @"
                            INSERT INTO SchoolYearVisibility (TeacherId, YearLevel, IsVisible)
                            VALUES (@TeacherId, @YearLevel, 1)";
                        
                        using (SqlCommand insertCommand = new SqlCommand(insertQuery, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@TeacherId", teacherId);
                            insertCommand.Parameters.AddWithValue("@YearLevel", yearLevel);
                            
                            await insertCommand.ExecuteNonQueryAsync();
                        }
                        
                        return true;
                    }
                }
            }
        }
        
        private async Task<bool> SetSchoolYearVisibility(string teacherId, string yearLevel, bool isVisible)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // First check if the table exists
                bool tableExists = await TableExists(connection, "SchoolYearVisibility");
                
                if (!tableExists)
                {
                    // Create the table if it doesn't exist
                    string createTableQuery = @"
                        CREATE TABLE SchoolYearVisibility (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            TeacherId NVARCHAR(128) NOT NULL,
                            YearLevel NVARCHAR(20) NOT NULL,
                            IsVisible BIT NOT NULL DEFAULT 1,
                            CONSTRAINT UQ_SchoolYearVisibility UNIQUE (TeacherId, YearLevel)
                        )";
                    
                    using (SqlCommand command = new SqlCommand(createTableQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }
                
                // Check if record exists
                string checkQuery = @"
                    SELECT COUNT(*)
                    FROM SchoolYearVisibility
                    WHERE TeacherId = @TeacherId AND YearLevel = @YearLevel";
                
                bool recordExists = false;
                
                using (SqlCommand command = new SqlCommand(checkQuery, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", teacherId);
                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                    
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    recordExists = count > 0;
                }
                
                if (recordExists)
                {
                    // Update existing record
                    string updateQuery = @"
                        UPDATE SchoolYearVisibility
                        SET IsVisible = @IsVisible
                        WHERE TeacherId = @TeacherId AND YearLevel = @YearLevel";
                    
                    using (SqlCommand command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@TeacherId", teacherId);
                        command.Parameters.AddWithValue("@YearLevel", yearLevel);
                        command.Parameters.AddWithValue("@IsVisible", isVisible);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                }
                else
                {
                    // Insert new record
                    string insertQuery = @"
                        INSERT INTO SchoolYearVisibility (TeacherId, YearLevel, IsVisible)
                        VALUES (@TeacherId, @YearLevel, @IsVisible)";
                    
                    using (SqlCommand command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@TeacherId", teacherId);
                        command.Parameters.AddWithValue("@YearLevel", yearLevel);
                        command.Parameters.AddWithValue("@IsVisible", isVisible);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                }
            }
        }
        
        private async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string query = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = @TableName
                ) THEN 1 ELSE 0 END";
            
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                
                int result = Convert.ToInt32(await command.ExecuteScalarAsync());
                return result == 1;
            }
        }
        
        [HttpGet]
        public IActionResult Create()
        {
            // Initialize a new challenge with an empty questions list to avoid validation errors
            return View(new Challenge 
            { 
                Questions = new List<ChallengeQuestion>(),
                IsActive = true
            });
        }
        
        [HttpPost]
        public async Task<IActionResult> Create(Challenge challenge)
        {
            if (ModelState.IsValid)
            {
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Ensure TeacherId is set (as backup in case hidden field doesn't work)
                challenge.TeacherId = teacherId;
                challenge.CreatedDate = DateTime.Now;
                
                // Initialize empty Questions list
                challenge.Questions = new List<ChallengeQuestion>();
                
                int challengeId = await CreateChallenge(challenge);
                
                if (challengeId > 0)
                {
                    TempData["Success"] = "Challenge created successfully!";
                    return RedirectToAction("Edit", new { id = challengeId });
                }
                else
                {
                    ModelState.AddModelError("", "Failed to create the challenge.");
                }
            }
            
            return View(challenge);
        }
        
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            var existingChallenge = await GetChallenge(id);
            
            if (existingChallenge == null || existingChallenge.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            // Load questions for this challenge
            existingChallenge.Questions = await GetChallengeQuestions(id);
            
            return View(existingChallenge);
        }
        
        [HttpPost]
        public async Task<IActionResult> Edit(Challenge challenge)
        {
            if (ModelState.IsValid)
            {
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Verify this challenge belongs to the teacher
                var existingChallenge = await GetChallenge(challenge.ChallengeId);
                
                if (existingChallenge == null || existingChallenge.TeacherId != teacherId)
                {
                    return NotFound();
                }
                
                challenge.TeacherId = teacherId;
                challenge.LastUpdatedDate = DateTime.Now;
                
                bool success = await UpdateChallenge(challenge);
                
                if (success)
                {
                    TempData["Success"] = "Challenge updated successfully!";
                    return RedirectToAction("Index");
                }
                else
                {
                    ModelState.AddModelError("", "Failed to update the challenge.");
                }
            }
            
            // Load questions for this challenge for the view
            challenge.Questions = await GetChallengeQuestions(challenge.ChallengeId);
            
            return View(challenge);
        }
        
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this challenge belongs to the teacher
            var existingChallenge = await GetChallenge(id);
            
            if (existingChallenge == null || existingChallenge.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            bool success = await DeleteChallenge(id);
            
            if (success)
            {
                TempData["Success"] = "Challenge deleted successfully!";
            }
            else
            {
                TempData["Error"] = "Failed to delete the challenge.";
            }
            
            return RedirectToAction("Index");
        }
        
        [HttpGet]
        public async Task<IActionResult> AddQuestion(int challengeId)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this challenge belongs to the teacher
            var parentChallenge = await GetChallenge(challengeId);
            
            if (parentChallenge == null || parentChallenge.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            var question = new ChallengeQuestion 
            { 
                ChallengeId = challengeId,
                Points = 10 // Default value
            };
            
            ViewBag.ChallengeName = parentChallenge.ChallengeName;
            
            return View(question);
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddQuestion(ChallengeQuestion question)
        {
            // Debug information
            System.Diagnostics.Debug.WriteLine("======== ADD QUESTION STARTED ========");
            System.Diagnostics.Debug.WriteLine($"Received question for ChallengeId: {question.ChallengeId}");
            System.Diagnostics.Debug.WriteLine($"Question Text: {question.QuestionText?.Substring(0, Math.Min(50, question.QuestionText?.Length ?? 0))}...");
            System.Diagnostics.Debug.WriteLine($"Answer Text: {question.AnswerText?.Substring(0, Math.Min(50, question.AnswerText?.Length ?? 0))}...");
            System.Diagnostics.Debug.WriteLine($"Points: {question.Points}");
            
            // Test database connection
            bool dbTestResult = await TestDatabaseConnection();
            System.Diagnostics.Debug.WriteLine($"Database connection test result: {dbTestResult}");
            
            if (!ModelState.IsValid)
            {
                // Log model state errors for debugging
                System.Diagnostics.Debug.WriteLine("ModelState invalid with following errors:");
                foreach (var state in ModelState)
                {
                    System.Diagnostics.Debug.WriteLine($"Model State: {state.Key} - {state.Value.Errors.Count} errors");
                    foreach (var error in state.Value.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"- Error: {error.ErrorMessage}");
                    }
                }
                
                // Re-populate ViewBag data for the view
                var errorChallenge = await GetChallenge(question.ChallengeId);
                ViewBag.ChallengeName = errorChallenge?.ChallengeName;
                
                return View(question);
            }
            
            System.Diagnostics.Debug.WriteLine("ModelState is valid, continuing");
            
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                System.Diagnostics.Debug.WriteLine("TeacherId is missing from session, redirecting to login");
                return RedirectToAction("Login", "Home");
            }
            
            System.Diagnostics.Debug.WriteLine($"TeacherId from session: {teacherId}");
            
            // Verify this challenge belongs to the teacher
            var parentChallengeObj = await GetChallenge(question.ChallengeId);
            
            if (parentChallengeObj == null)
            {
                System.Diagnostics.Debug.WriteLine($"Challenge with ID {question.ChallengeId} not found");
                ModelState.AddModelError("", "Challenge not found.");
                ViewBag.ChallengeName = "Unknown Challenge";
                return View(question);
            }
            
            System.Diagnostics.Debug.WriteLine($"Found challenge: {parentChallengeObj.ChallengeName}");
            
            if (parentChallengeObj.TeacherId != teacherId)
            {
                System.Diagnostics.Debug.WriteLine($"Challenge belongs to teacher {parentChallengeObj.TeacherId}, not current teacher {teacherId}");
                return NotFound();
            }
            
            question.CreatedDate = DateTime.Now;
            
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting to create question in database");
                int questionId = await CreateChallengeQuestion(question);
                
                if (questionId > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Question created successfully with ID: {questionId}");
                    
                    // After successfully adding a question, update scores for affected students
                    _logger.LogInformation($"Updating scores for students who have submitted challenge {question.ChallengeId}");
                    await RecalculateScoresForChallengeSubmitters(question.ChallengeId);
                    
                    TempData["Success"] = "Question added successfully!";
                    return RedirectToAction("Edit", new { id = question.ChallengeId });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("CreateChallengeQuestion returned 0 or negative ID");
                    ModelState.AddModelError("", "Failed to add the question.");
                }
            }
            catch (Exception ex)
            {
                // Log the error
                System.Diagnostics.Debug.WriteLine($"Exception occurred: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Error message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
            }
            
            System.Diagnostics.Debug.WriteLine("Failed to create question, returning to form");
            // Re-populate ViewBag data for the view
            ViewBag.ChallengeName = parentChallengeObj.ChallengeName;
            
            return View(question);
        }
        
        [HttpPost]
        public async Task<IActionResult> DeleteQuestion(int id, int challengeId)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this challenge belongs to the teacher
            var parentChallenge = await GetChallenge(challengeId);
            
            if (parentChallenge == null || parentChallenge.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            bool success = await DeleteChallengeQuestion(id);
            
            if (success)
            {
                // After successfully deleting a question, update scores for affected students
                _logger.LogInformation($"Question deleted from challenge {challengeId}. Recalculating scores for affected students.");
                await RecalculateScoresForChallengeSubmitters(challengeId);
                
                TempData["Success"] = "Question deleted successfully!";
            }
            else
            {
                TempData["Error"] = "Failed to delete the question.";
            }
            
            return RedirectToAction("Edit", new { id = challengeId });
        }
        
        [HttpGet]
        public async Task<IActionResult> EditQuestion(int id, int challengeId)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this challenge belongs to the teacher
            var parentChallenge = await GetChallenge(challengeId);
            
            if (parentChallenge == null || parentChallenge.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            var question = await GetChallengeQuestion(id);
            
            if (question == null || question.ChallengeId != challengeId)
            {
                return NotFound();
            }
            
            ViewBag.ChallengeName = parentChallenge.ChallengeName;
            
            return View(question);
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQuestion(ChallengeQuestion question)
        {
            // Debug information
            System.Diagnostics.Debug.WriteLine($"Editing question for ChallengeId: {question.ChallengeId}, QuestionId: {question.QuestionId}");
            
            if (!ModelState.IsValid)
            {
                // Log model state errors for debugging
                foreach (var state in ModelState)
                {
                    System.Diagnostics.Debug.WriteLine($"Model State: {state.Key} - {state.Value.Errors.Count} errors");
                    foreach (var error in state.Value.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"- Error: {error.ErrorMessage}");
                    }
                }
                
                // Re-populate ViewBag data for the view
                var errorChallenge = await GetChallenge(question.ChallengeId);
                ViewBag.ChallengeName = errorChallenge?.ChallengeName;
                
                return View(question);
            }
            
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this challenge belongs to the teacher
            var parentChallengeInfo = await GetChallenge(question.ChallengeId);
            
            if (parentChallengeInfo == null)
            {
                ModelState.AddModelError("", "Challenge not found.");
                ViewBag.ChallengeName = "Unknown Challenge";
                return View(question);
            }
            
            if (parentChallengeInfo.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            // Make sure this is a valid question
            var existingQuestion = await GetChallengeQuestion(question.QuestionId);
            
            if (existingQuestion == null || existingQuestion.ChallengeId != question.ChallengeId)
            {
                ModelState.AddModelError("", "Question not found or does not belong to this challenge.");
                ViewBag.ChallengeName = parentChallengeInfo.ChallengeName;
                return View(question);
            }
            
            // Check if the points value has changed
            bool pointsChanged = existingQuestion.Points != question.Points;
            
            question.LastUpdatedDate = DateTime.Now;
            
            try
            {
                bool success = await UpdateChallengeQuestion(question);
                
                if (success)
                {
                    // If points changed or answer changed, recalculate scores for affected students
                    if (pointsChanged || existingQuestion.AnswerText != question.AnswerText)
                    {
                        _logger.LogInformation($"Question points or answer changed. Recalculating scores for challenge {question.ChallengeId}");
                        await RecalculateScoresForChallengeSubmitters(question.ChallengeId);
                    }
                    
                    TempData["Success"] = "Question updated successfully!";
                    return RedirectToAction("Edit", new { id = question.ChallengeId });
                }
                else
                {
                    ModelState.AddModelError("", "Failed to update the question.");
                }
            }
            catch (Exception ex)
            {
                // Log the error
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
            }
            
            // Re-populate ViewBag data for the view
            ViewBag.ChallengeName = parentChallengeInfo.ChallengeName;
            
            return View(question);
        }
        
        // Add a test endpoint for direct database insertion
        [HttpGet]
        public async Task<IActionResult> TestQuestionInsert(int challengeId)
        {
            try
            {
                // Create a test question
                var testQuestion = new ChallengeQuestion
                {
                    ChallengeId = challengeId,
                    QuestionText = "Test Question " + DateTime.Now.ToString(),
                    AnswerText = "Test Answer",
                    Points = 10,
                    CreatedDate = DateTime.Now
                };
                
                System.Diagnostics.Debug.WriteLine("=== TEST QUESTION INSERT ===");
                System.Diagnostics.Debug.WriteLine($"Creating test question for challenge ID: {challengeId}");
                
                // Try to insert the question
                int newId = await CreateChallengeQuestion(testQuestion);
                
                if (newId > 0)
                {
                    return Content($"Test successful! Created question with ID: {newId}");
                }
                else
                {
                    return Content("Test failed - no ID returned");
                }
            }
            catch (Exception ex)
            {
                return Content($"Test failed with error: {ex.Message}");
            }
        }
        
        // Add database schema checker
        [HttpGet]
        public async Task<IActionResult> CheckDatabaseSchema()
        {
            try
            {
                StringBuilder result = new StringBuilder();
                result.AppendLine("Database Schema Information:");
                
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if ChallengeQuestions table exists
                    string tableCheckQuery = @"
                        SELECT 
                            t.name AS TableName,
                            c.name AS ColumnName,
                            ty.name AS DataType,
                            c.max_length AS MaxLength,
                            c.is_nullable AS IsNullable,
                            c.is_identity AS IsIdentity
                        FROM 
                            sys.tables t
                            INNER JOIN sys.columns c ON t.object_id = c.object_id
                            INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                        WHERE 
                            t.name = 'ChallengeQuestions'
                        ORDER BY 
                            c.column_id";
                    
                    using (SqlCommand command = new SqlCommand(tableCheckQuery, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                result.AppendLine("ChallengeQuestions table does not exist!");
                            }
                            else
                            {
                                result.AppendLine("\nChallengeQuestions Table Schema:");
                                result.AppendLine("------------------------------");
                                result.AppendLine("Column Name | Data Type | Max Length | Is Nullable | Is Identity");
                                result.AppendLine("------------------------------");
                                
                                while (await reader.ReadAsync())
                                {
                                    string columnName = reader["ColumnName"].ToString();
                                    string dataType = reader["DataType"].ToString();
                                    int maxLength = Convert.ToInt32(reader["MaxLength"]);
                                    bool isNullable = Convert.ToBoolean(reader["IsNullable"]);
                                    bool isIdentity = Convert.ToBoolean(reader["IsIdentity"]);
                                    
                                    result.AppendLine($"{columnName} | {dataType} | {maxLength} | {isNullable} | {isIdentity}");
                                }
                            }
                        }
                    }
                    
                    // Check for any foreign key constraints
                    string fkQuery = @"
                        SELECT 
                            fk.name AS FKName,
                            OBJECT_NAME(fk.parent_object_id) AS TableName,
                            COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
                            OBJECT_NAME(fk.referenced_object_id) AS ReferencedTableName,
                            COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumnName
                        FROM 
                            sys.foreign_keys fk
                            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                        WHERE 
                            OBJECT_NAME(fk.parent_object_id) = 'ChallengeQuestions'";
                    
                    using (SqlCommand command = new SqlCommand(fkQuery, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            result.AppendLine("\nForeign Key Constraints:");
                            result.AppendLine("------------------------------");
                            
                            if (!reader.HasRows)
                            {
                                result.AppendLine("No foreign key constraints found.");
                            }
                            else
                            {
                                result.AppendLine("FK Name | Column | Referenced Table | Referenced Column");
                                while (await reader.ReadAsync())
                                {
                                    string fkName = reader["FKName"].ToString();
                                    string columnName = reader["ColumnName"].ToString();
                                    string referencedTable = reader["ReferencedTableName"].ToString();
                                    string referencedColumn = reader["ReferencedColumnName"].ToString();
                                    
                                    result.AppendLine($"{fkName} | {columnName} | {referencedTable} | {referencedColumn}");
                                }
                            }
                        }
                    }
                    
                    // Test with a simple count query
                    string countQuery = "SELECT COUNT(*) FROM ChallengeQuestions";
                    using (SqlCommand command = new SqlCommand(countQuery, connection))
                    {
                        try
                        {
                            int count = (int)await command.ExecuteScalarAsync();
                            result.AppendLine($"\nCurrent row count in ChallengeQuestions: {count}");
                        }
                        catch (Exception ex)
                        {
                            result.AppendLine($"\nError counting rows: {ex.Message}");
                        }
                    }
                }
                
                return Content(result.ToString(), "text/plain");
            }
            catch (Exception ex)
            {
                return Content($"Error checking schema: {ex.Message}", "text/plain");
            }
        }
        
        // Student Challenge Methods
        private async Task<string> GetStudentYearLevel(string studentId)
        {
            // Since there's no year level in the database and all students take the same challenges,
            // just return a default value
            return "All"; // Default value since year level doesn't apply
        }

        [HttpGet]
        public async Task<IActionResult> AvailableChallenges()
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Get the student's year level from database (numeric value)
            int studentYearLevel = 0;
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT GradeLevel
                    FROM StudentDetails
                    WHERE IdNumber = @StudentId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        studentYearLevel = Convert.ToInt32(result);
                    }
                }
            }
            
            // Check if the student is a graduate - redirect them to dashboard
            if (studentYearLevel == 5)
            {
                TempData["Warning"] = "Graduates cannot access available challenges.";
                return RedirectToAction("StudentDashboard", "Dashboard");
            }
            
            // Set the student ID in ViewBag for the view
            ViewBag.StudentId = studentId;
            
            // Get all active challenges
            var challenges = await GetAvailableChallenges();
            _logger.LogInformation($"Retrieved {challenges.Count} active challenges from GetAvailableChallenges");
            
            // Create a dictionary to track completed challenges
            Dictionary<int, bool> completedChallenges = new Dictionary<int, bool>();
            
            // Check each challenge if it's been completed by the student
            foreach (var challenge in challenges)
            {
                bool hasCompleted = await HasStudentCompletedChallenge(challenge.ChallengeId, studentId);
                completedChallenges[challenge.ChallengeId] = hasCompleted;
            }
            
            // Add the completed challenges dictionary to ViewBag
            ViewBag.CompletedChallenges = completedChallenges;
            
            // Filter out challenges from school years that are not visible to students
            var visibleChallenges = new List<Challenge>();
            foreach (var challenge in challenges)
            {
                // Check if the challenge's school year is visible to students
                bool isVisible = await IsSchoolYearVisibleToStudents(challenge.TeacherId, challenge.YearLevel);
                _logger.LogInformation($"Challenge {challenge.ChallengeId} ({challenge.ChallengeName}) with YearLevel '{challenge.YearLevel}' visibility: {isVisible}");
                
                if (isVisible)
                {
                    visibleChallenges.Add(challenge);
                }
                else
                {
                    _logger.LogWarning($"Challenge {challenge.ChallengeId} ({challenge.ChallengeName}) with YearLevel '{challenge.YearLevel}' is hidden due to school year visibility settings");
                }
            }
            
            _logger.LogInformation($"After school year visibility filtering: {visibleChallenges.Count} visible challenges");
            
            // Add student year level info to ViewBag (for display in the view)
            ViewBag.StudentYearLevel = studentYearLevel;
            
            // Filter out challenges the student has already completed
            List<Challenge> availableChallenges = new List<Challenge>();
            
            foreach (var challenge in visibleChallenges)
            {
                bool hasCompleted = completedChallenges[challenge.ChallengeId];
                
                if (!hasCompleted)
                {
                    availableChallenges.Add(challenge);
                    _logger.LogDebug($"Adding challenge to available list: {challenge.ChallengeId} ({challenge.ChallengeName})");
                }
            }
            
            _logger.LogInformation($"Final available challenges for display: {availableChallenges.Count} challenges");
            
            // The GetAvailableChallenges method already handles transferee students with special access
            // All challenges in availableChallenges list are valid for this student
            
            return View(availableChallenges);
        }
        
        private async Task<bool> IsSchoolYearVisibleToStudents(string teacherId, string yearLevel)
        {
            // If the year level is null or empty, default to visible
            // This is critical for new school year challenges that might not have a year level set
            if (string.IsNullOrEmpty(yearLevel))
            {
                _logger.LogInformation($"Empty year level detected for challenge, defaulting to visible");
                return true;
            }
            
            // If teacher ID is null, default to visible
            if (string.IsNullOrEmpty(teacherId))
            {
                _logger.LogInformation($"Empty teacher ID detected for challenge with year level '{yearLevel}', defaulting to visible");
                return true;
            }
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the table exists
                bool tableExists = await TableExists(connection, "SchoolYearVisibility");
                
                if (!tableExists)
                {
                    // If table doesn't exist yet, default to visible
                    _logger.LogInformation($"SchoolYearVisibility table doesn't exist, defaulting to visible for year '{yearLevel}'");
                    return true;
                }
                
                // Check visibility setting
                string query = @"
                    SELECT IsVisible
                    FROM SchoolYearVisibility
                    WHERE TeacherId = @TeacherId AND YearLevel = @YearLevel";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", teacherId);
                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                    
                    try
                    {
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            bool isVisible = Convert.ToBoolean(result);
                            _logger.LogInformation($"Found visibility setting for year '{yearLevel}': {isVisible}");
                            return isVisible;
                        }
                        else
                        {
                            // No record found, default to visible - critical for new school years
                            _logger.LogInformation($"No visibility record found for year '{yearLevel}', defaulting to visible");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error checking year visibility for '{yearLevel}', defaulting to visible");
                        return true;
                    }
                }
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> TakeChallenge(int id)
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Get challenge details
            var challenge = await GetChallenge(id);
            
            if (challenge == null)
            {
                TempData["Error"] = "Challenge not found.";
                return RedirectToAction("AvailableChallenges");
            }
            
            // Check if student has already completed this challenge
            bool hasCompleted = await HasStudentCompletedChallenge(id, studentId);
            
            if (hasCompleted)
            {
                TempData["Error"] = "You have already completed this challenge.";
                return RedirectToAction("AvailableChallenges");
            }
            
            // Load questions for this challenge
            challenge.Questions = await GetChallengeQuestions(id);
            
            if (challenge.Questions == null || !challenge.Questions.Any())
            {
                TempData["Error"] = "This challenge has no questions.";
                return RedirectToAction("AvailableChallenges");
            }
            
            // Create view model
            var viewModel = new SubmitChallengeViewModel
            {
                ChallengeId = id,
                StudentId = studentId,
                ChallengeName = challenge.ChallengeName,
                Answers = new Dictionary<int, string>()
            };
            
            // Initialize answers dictionary
            foreach (var question in challenge.Questions)
            {
                viewModel.Answers[question.QuestionId] = "";
            }
            
            // Populate ViewBag with challenge data
            ViewBag.Description = challenge.Description;
            ViewBag.ProgrammingLanguage = challenge.ProgrammingLanguage;
            ViewBag.QuestionCount = challenge.Questions.Count;
            ViewBag.TotalPoints = challenge.Questions.Sum(q => q.Points);
            ViewBag.Questions = challenge.Questions;
            
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitChallenge(SubmitChallengeViewModel model)
        {
            // Get the student ID from the session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }

            try
            {
                // Get the challenge details
                var challenge = await GetChallenge(model.ChallengeId);
                
                if (challenge == null)
                {
                    TempData["Error"] = "Challenge not found.";
                    return RedirectToAction("AvailableChallenges");
                }
                
                // Get the challenge questions
                challenge.Questions = await GetChallengeQuestions(model.ChallengeId);
                
                if (challenge.Questions == null || !challenge.Questions.Any())
                {
                    TempData["Error"] = "This challenge has no questions.";
                    return RedirectToAction("AvailableChallenges");
                }
                
                // Grade the answers
                int pointsEarned = 0;
                int totalPoints = challenge.Questions.Sum(q => q.Points);
                
                foreach (var question in challenge.Questions)
                {
                    if (model.Answers.TryGetValue(question.QuestionId, out string answer))
                    {
                        if (string.Equals(answer?.Trim(), question.AnswerText?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            pointsEarned += question.Points;
                        }
                    }
                }
                
                // Calculate percentage score
                int percentageScore = totalPoints > 0 ? (pointsEarned * 100) / totalPoints : 0;
                
                // Save the submission
                int submissionId = await SaveChallengeSubmission(model.ChallengeId, studentId, percentageScore, pointsEarned, totalPoints);
                
                if (submissionId > 0)
                {
                    // Update the student's CompletedChallengesScore
                    await UpdateCompletedChallengesScore(studentId);
                    
                    // Update the badge color automatically
                    try
                    {
                        await _badgeService.UpdateBadgeColor(studentId);
                        _logger.LogInformation($"BadgeColor updated for student {studentId} after challenge completion");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating badge color for student {studentId}");
                    }
                    
                    // Store challenge submission results in TempData for the success page
                    TempData["ChallengeName"] = challenge.ChallengeName;
                    TempData["PercentageScore"] = percentageScore;
                    TempData["PointsEarned"] = pointsEarned;
                    TempData["TotalPoints"] = totalPoints;
                    TempData["ProgrammingLanguage"] = challenge.ProgrammingLanguage;
                    
                    // Redirect to success page
                    return RedirectToAction("ChallengeSuccess");
                }
                else
                {
                    TempData["Error"] = "Failed to save challenge submission.";
                    return RedirectToAction("TakeChallenge", new { id = model.ChallengeId });
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogError(ex, "Error submitting challenge");
                TempData["Error"] = "An error occurred while processing your submission.";
                return RedirectToAction("AvailableChallenges");
            }
        }

        [HttpGet]
        public IActionResult ChallengeSuccess()
        {
            // Check if we have challenge success data in TempData
            if (TempData["ChallengeName"] == null)
            {
                return RedirectToAction("AvailableChallenges");
            }
            
            // Pass the data to the view
            ViewBag.ChallengeName = TempData["ChallengeName"];
            ViewBag.PercentageScore = TempData["PercentageScore"];
            ViewBag.PointsEarned = TempData["PointsEarned"];
            ViewBag.TotalPoints = TempData["TotalPoints"];
            ViewBag.ProgrammingLanguage = TempData["ProgrammingLanguage"];
            
            return View();
        }

        // Helper methods for student challenges
        private async Task<bool> HasStudentCompletedChallenge(int challengeId, string studentId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // First, get the UserId for this student
                string getUserIdQuery = @"
                    SELECT UserId 
                    FROM Users 
                    WHERE Username = @IdNumber";
                
                string userId = null;
                using (SqlCommand command = new SqlCommand(getUserIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        userId = result.ToString();
                    }
                    else
                    {
                        // If no UserId found, they can't have completed the challenge
                        return false;
                    }
                }
                
                string query = @"
                    SELECT COUNT(*) 
                    FROM ChallengeSubmissions 
                    WHERE ChallengeId = @ChallengeId AND StudentId = @StudentId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                    command.Parameters.AddWithValue("@StudentId", userId);
                    
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    
                    return count > 0;
                }
            }
        }

        private async Task<int> SaveChallengeSubmission(int challengeId, string studentId, int percentageScore, int pointsEarned, int totalPoints)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                try
                {
                    // First, get the UserId for this student from the Users table
                    string getUserIdQuery = @"
                        SELECT UserId 
                        FROM Users 
                        WHERE Username = @IdNumber";
                    
                    string userId = null;
                    using (SqlCommand cmd = new SqlCommand(getUserIdQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@IdNumber", studentId);
                        var result = await cmd.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            userId = result.ToString();
                        }
                        else
                        {
                            // If no UserId found, we can't proceed
                            throw new Exception($"Cannot find user with IdNumber {studentId} in the Users table");
                        }
                    }
                    
                    // Check if ChallengeSubmissions table exists
                    string checkTableQuery = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChallengeSubmissions')
                        BEGIN
                            CREATE TABLE ChallengeSubmissions (
                                SubmissionId INT IDENTITY(1,1) PRIMARY KEY,
                                ChallengeId INT NOT NULL,
                                StudentId NVARCHAR(128) NOT NULL,  -- Match the type of UserId
                                SubmissionDate DATETIME NOT NULL DEFAULT GETDATE(),
                                PercentageScore INT NOT NULL DEFAULT 0,
                                PointsEarned INT NOT NULL DEFAULT 0,
                                TotalPoints INT NOT NULL DEFAULT 0,
                                CONSTRAINT FK_ChallengeSubmissions_Users
                                    FOREIGN KEY (StudentId) REFERENCES Users(UserId)
                            )
                        END";
                    
                    using (SqlCommand cmd = new SqlCommand(checkTableQuery, connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    
                    // Make sure all needed columns exist
                    string ensureColumnsQuery = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'PercentageScore')
                        BEGIN
                            ALTER TABLE ChallengeSubmissions ADD PercentageScore INT NOT NULL DEFAULT 0
                        END
                        
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'PointsEarned')
                        BEGIN
                            ALTER TABLE ChallengeSubmissions ADD PointsEarned INT NOT NULL DEFAULT 0
                        END
                        
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'TotalPoints')
                        BEGIN
                            ALTER TABLE ChallengeSubmissions ADD TotalPoints INT NOT NULL DEFAULT 0
                        END";
                    
                    using (SqlCommand cmd = new SqlCommand(ensureColumnsQuery, connection))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    
                    // Insert challenge submission with all needed fields and the correct UserId
                    string insertQuery = @"
                        INSERT INTO ChallengeSubmissions (ChallengeId, StudentId, SubmissionDate, PercentageScore, PointsEarned, TotalPoints)
                        VALUES (@ChallengeId, @StudentId, @SubmissionDate, @PercentageScore, @PointsEarned, @TotalPoints);
                        SELECT SCOPE_IDENTITY();";
                    
                    using (SqlCommand cmd = new SqlCommand(insertQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@ChallengeId", challengeId);
                        cmd.Parameters.AddWithValue("@StudentId", userId);  // Use the retrieved UserId
                        cmd.Parameters.AddWithValue("@SubmissionDate", DateTime.Now);
                        cmd.Parameters.AddWithValue("@PercentageScore", percentageScore);
                        cmd.Parameters.AddWithValue("@PointsEarned", pointsEarned);
                        cmd.Parameters.AddWithValue("@TotalPoints", totalPoints);
                        
                        var result = await cmd.ExecuteScalarAsync();
                        return Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in SaveChallengeSubmission: {ex.Message}");
                    throw; // Rethrow to be handled by caller
                }
            }
        }

        private async Task<List<ChallengeSubmission>> GetCompletedChallengesForStudent(string studentId)
        {
            List<ChallengeSubmission> submissions = new List<ChallengeSubmission>();
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // First, get the UserId for this student
                string getUserIdQuery = @"
                    SELECT UserId 
                    FROM Users 
                    WHERE Username = @IdNumber";
                
                string userId = null;
                using (SqlCommand command = new SqlCommand(getUserIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        userId = result.ToString();
                    }
                    else
                    {
                        // If no UserId found, just return empty list
                        return submissions;
                    }
                }
                
                string query = @"
                    SELECT s.*, c.ChallengeName, c.ProgrammingLanguage, c.Description
                    FROM ChallengeSubmissions s
                    JOIN Challenges c ON s.ChallengeId = c.ChallengeId
                    WHERE s.StudentId = @StudentId
                    ORDER BY s.SubmissionDate DESC";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", userId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var submission = new ChallengeSubmission
                            {
                                SubmissionId = Convert.ToInt32(reader["SubmissionId"]),
                                ChallengeId = Convert.ToInt32(reader["ChallengeId"]),
                                StudentId = studentId, // Use the original student ID for display
                                SubmissionDate = Convert.ToDateTime(reader["SubmissionDate"]),
                                ChallengeName = reader["ChallengeName"].ToString(),
                                ProgrammingLanguage = reader["ProgrammingLanguage"].ToString(),
                                Description = reader["Description"]?.ToString()
                            };
                            
                            // Add data from the specific columns we know exist
                            if (ReaderHasColumn(reader, "PercentageScore"))
                            {
                                submission.PercentageScore = reader["PercentageScore"] != DBNull.Value ? Convert.ToInt32(reader["PercentageScore"]) : 0;
                            }
                            
                            if (ReaderHasColumn(reader, "PointsEarned"))
                            {
                                submission.PointsEarned = reader["PointsEarned"] != DBNull.Value ? Convert.ToInt32(reader["PointsEarned"]) : 0;
                            }
                            
                            if (ReaderHasColumn(reader, "TotalPoints"))
                            {
                                submission.TotalPoints = reader["TotalPoints"] != DBNull.Value ? Convert.ToInt32(reader["TotalPoints"]) : 0;
                            }
                            
                            submissions.Add(submission);
                        }
                    }
                }
            }
            
            return submissions;
        }

        // Helper method to check if a column exists in a reader
        private bool ReaderHasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        
        // Database methods
        
        private async Task<ChallengeQuestion> GetChallengeQuestion(int questionId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT * FROM ChallengeQuestions
                    WHERE QuestionId = @QuestionId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@QuestionId", questionId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new ChallengeQuestion
                            {
                                QuestionId = (int)reader["QuestionId"],
                                ChallengeId = (int)reader["ChallengeId"],
                                QuestionText = reader["QuestionText"].ToString(),
                                AnswerText = reader["AnswerText"].ToString(),
                                CodeSnippet = reader["CodeSnippet"] != DBNull.Value ? reader["CodeSnippet"].ToString() : null,
                                Points = (int)reader["Points"],
                                CreatedDate = (DateTime)reader["CreatedDate"],
                                LastUpdatedDate = reader["LastUpdatedDate"] != DBNull.Value ? (DateTime?)reader["LastUpdatedDate"] : null
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        
        private async Task<bool> UpdateChallengeQuestion(ChallengeQuestion question)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    UPDATE ChallengeQuestions
                    SET QuestionText = @QuestionText,
                        AnswerText = @AnswerText,
                        CodeSnippet = @CodeSnippet,
                        Points = @Points,
                        LastUpdatedDate = @LastUpdatedDate
                    WHERE QuestionId = @QuestionId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@QuestionId", question.QuestionId);
                    command.Parameters.AddWithValue("@QuestionText", question.QuestionText);
                    command.Parameters.AddWithValue("@AnswerText", question.AnswerText);
                    command.Parameters.AddWithValue("@CodeSnippet", (object)question.CodeSnippet ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Points", question.Points);
                    command.Parameters.AddWithValue("@LastUpdatedDate", DateTime.Now);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        private async Task<List<Challenge>> GetTeacherChallenges(string teacherId)
        {
            List<Challenge> challenges = new List<Challenge>();
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT c.*, 
                           COUNT(q.QuestionId) AS QuestionCount,
                           SUM(q.Points) AS TotalPoints
                    FROM Challenges c
                    LEFT JOIN ChallengeQuestions q ON c.ChallengeId = q.ChallengeId
                    WHERE c.TeacherId = @TeacherId
                    GROUP BY c.ChallengeId, c.TeacherId, c.ChallengeName, c.ProgrammingLanguage, 
                             c.Description, c.YearLevel, c.CreatedDate, c.LastUpdatedDate, c.IsActive, c.VisibleFromDate, c.ExpirationDate
                    ORDER BY c.LastUpdatedDate DESC, c.CreatedDate DESC";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", teacherId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Get the question count from the query result
                            int questionCount = reader["QuestionCount"] != DBNull.Value ? Convert.ToInt32(reader["QuestionCount"]) : 0;
                            
                            Challenge challenge = new Challenge
                            {
                                ChallengeId = Convert.ToInt32(reader["ChallengeId"]),
                                TeacherId = reader["TeacherId"].ToString(),
                                ChallengeName = reader["ChallengeName"].ToString(),
                                ProgrammingLanguage = reader["ProgrammingLanguage"].ToString(),
                                Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null,
                                YearLevel = reader["YearLevel"].ToString(),
                                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                                LastUpdatedDate = reader["LastUpdatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastUpdatedDate"]) : null,
                                VisibleFromDate = reader["VisibleFromDate"] != DBNull.Value ? Convert.ToDateTime(reader["VisibleFromDate"]) : null,
                                ExpirationDate = reader["ExpirationDate"] != DBNull.Value ? Convert.ToDateTime(reader["ExpirationDate"]) : null,
                                IsActive = Convert.ToBoolean(reader["IsActive"]),
                                Questions = new List<ChallengeQuestion>()
                            };
                            
                            // Create placeholder questions to match the actual count
                            for (int i = 0; i < questionCount; i++)
                            {
                                challenge.Questions.Add(new ChallengeQuestion());
                            }
                            
                            // Store additional information in ViewData - this can still be useful
                            ViewData[$"QuestionCount_{challenge.ChallengeId}"] = questionCount;
                            ViewData[$"TotalPoints_{challenge.ChallengeId}"] = reader["TotalPoints"] != DBNull.Value ? Convert.ToInt32(reader["TotalPoints"]) : 0;
                            
                            challenges.Add(challenge);
                        }
                    }
                }
            }
            
            return challenges;
        }
        
        private async Task<Challenge> GetChallenge(int challengeId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT * FROM Challenges
                    WHERE ChallengeId = @ChallengeId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Challenge
                            {
                                ChallengeId = Convert.ToInt32(reader["ChallengeId"]),
                                TeacherId = reader["TeacherId"].ToString(),
                                ChallengeName = reader["ChallengeName"].ToString(),
                                ProgrammingLanguage = reader["ProgrammingLanguage"].ToString(),
                                Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null,
                                YearLevel = reader["YearLevel"].ToString(),
                                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                                LastUpdatedDate = reader["LastUpdatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastUpdatedDate"]) : null,
                                VisibleFromDate = reader["VisibleFromDate"] != DBNull.Value ? Convert.ToDateTime(reader["VisibleFromDate"]) : null,
                                ExpirationDate = reader["ExpirationDate"] != DBNull.Value ? Convert.ToDateTime(reader["ExpirationDate"]) : null,
                                IsActive = Convert.ToBoolean(reader["IsActive"]),
                                Questions = new List<ChallengeQuestion>()
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        
        private async Task<int> CreateChallenge(Challenge challenge)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    INSERT INTO Challenges (TeacherId, ChallengeName, ProgrammingLanguage, Description, YearLevel, CreatedDate, IsActive, VisibleFromDate, ExpirationDate)
                    VALUES (@TeacherId, @ChallengeName, @ProgrammingLanguage, @Description, @YearLevel, @CreatedDate, @IsActive, @VisibleFromDate, @ExpirationDate);
                    SELECT SCOPE_IDENTITY();";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", challenge.TeacherId);
                    command.Parameters.AddWithValue("@ChallengeName", challenge.ChallengeName);
                    command.Parameters.AddWithValue("@ProgrammingLanguage", challenge.ProgrammingLanguage);
                    command.Parameters.AddWithValue("@Description", (object)challenge.Description ?? DBNull.Value);
                    command.Parameters.AddWithValue("@YearLevel", challenge.YearLevel);
                    command.Parameters.AddWithValue("@CreatedDate", challenge.CreatedDate);
                    command.Parameters.AddWithValue("@IsActive", challenge.IsActive);
                    command.Parameters.AddWithValue("@VisibleFromDate", (object)challenge.VisibleFromDate ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ExpirationDate", (object)challenge.ExpirationDate ?? DBNull.Value);
                    
                    var result = await command.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
        }
        
        private async Task<bool> UpdateChallenge(Challenge challenge)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    UPDATE Challenges
                    SET ChallengeName = @ChallengeName,
                        ProgrammingLanguage = @ProgrammingLanguage,
                        Description = @Description,
                        YearLevel = @YearLevel,
                        LastUpdatedDate = @LastUpdatedDate,
                        IsActive = @IsActive,
                        VisibleFromDate = @VisibleFromDate,
                        ExpirationDate = @ExpirationDate
                    WHERE ChallengeId = @ChallengeId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challenge.ChallengeId);
                    command.Parameters.AddWithValue("@ChallengeName", challenge.ChallengeName);
                    command.Parameters.AddWithValue("@ProgrammingLanguage", challenge.ProgrammingLanguage);
                    command.Parameters.AddWithValue("@Description", (object)challenge.Description ?? DBNull.Value);
                    command.Parameters.AddWithValue("@YearLevel", challenge.YearLevel);
                    command.Parameters.AddWithValue("@LastUpdatedDate", challenge.LastUpdatedDate);
                    command.Parameters.AddWithValue("@IsActive", challenge.IsActive);
                    command.Parameters.AddWithValue("@VisibleFromDate", (object)challenge.VisibleFromDate ?? DBNull.Value);
                    command.Parameters.AddWithValue("@ExpirationDate", (object)challenge.ExpirationDate ?? DBNull.Value);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        private async Task<bool> DeleteChallenge(int challengeId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // First delete all submissions for this challenge
                string deleteSubmissionsQuery = "DELETE FROM ChallengeSubmissions WHERE ChallengeId = @ChallengeId";
                
                using (SqlCommand command = new SqlCommand(deleteSubmissionsQuery, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                    await command.ExecuteNonQueryAsync();
                }
                
                // Next delete all questions for this challenge
                string deleteQuestionsQuery = "DELETE FROM ChallengeQuestions WHERE ChallengeId = @ChallengeId";
                
                using (SqlCommand command = new SqlCommand(deleteQuestionsQuery, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                    await command.ExecuteNonQueryAsync();
                }
                
                // Finally delete the challenge
                string deleteChallengeQuery = "DELETE FROM Challenges WHERE ChallengeId = @ChallengeId";
                
                using (SqlCommand command = new SqlCommand(deleteChallengeQuery, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        private async Task<List<ChallengeQuestion>> GetChallengeQuestions(int challengeId)
        {
            List<ChallengeQuestion> questions = new List<ChallengeQuestion>();
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = @"
                    SELECT * FROM ChallengeQuestions
                    WHERE ChallengeId = @ChallengeId
                    ORDER BY QuestionId ASC";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ChallengeId", challengeId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            questions.Add(new ChallengeQuestion
                            {
                                QuestionId = (int)reader["QuestionId"],
                                ChallengeId = (int)reader["ChallengeId"],
                                QuestionText = reader["QuestionText"].ToString(),
                                AnswerText = reader["AnswerText"].ToString(),
                                CodeSnippet = reader["CodeSnippet"] != DBNull.Value ? reader["CodeSnippet"].ToString() : null,
                                Points = (int)reader["Points"],
                                CreatedDate = (DateTime)reader["CreatedDate"],
                                LastUpdatedDate = reader["LastUpdatedDate"] != DBNull.Value ? (DateTime?)reader["LastUpdatedDate"] : null
                            });
                        }
                    }
                }
            }
            
            return questions;
        }
        
        private async Task<int> CreateChallengeQuestion(ChallengeQuestion question)
        {
            System.Diagnostics.Debug.WriteLine("Entering CreateChallengeQuestion method");
            System.Diagnostics.Debug.WriteLine($"Creating question for challenge ID: {question.ChallengeId}");
            
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    System.Diagnostics.Debug.WriteLine("Opening database connection");
                    await connection.OpenAsync();
                    
                    string query = @"
                        INSERT INTO ChallengeQuestions (ChallengeId, QuestionText, AnswerText, CodeSnippet, Points, CreatedDate)
                        VALUES (@ChallengeId, @QuestionText, @AnswerText, @CodeSnippet, @Points, @CreatedDate);
                        SELECT SCOPE_IDENTITY();";
                    
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        System.Diagnostics.Debug.WriteLine("Setting up SQL parameters");
                        command.Parameters.AddWithValue("@ChallengeId", question.ChallengeId);
                        command.Parameters.AddWithValue("@QuestionText", question.QuestionText ?? "");
                        command.Parameters.AddWithValue("@AnswerText", question.AnswerText ?? "");
                        command.Parameters.AddWithValue("@CodeSnippet", (object)question.CodeSnippet ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Points", question.Points);
                        command.Parameters.AddWithValue("@CreatedDate", question.CreatedDate);
                        
                        // Log parameter values
                        foreach (SqlParameter param in command.Parameters)
                        {
                            string paramValue = param.Value == DBNull.Value ? "NULL" : param.Value.ToString();
                            System.Diagnostics.Debug.WriteLine($"Parameter {param.ParameterName}: {paramValue}");
                        }
                        
                        // Log complete query for debugging
                        System.Diagnostics.Debug.WriteLine($"Executing SQL query: {query}");
                        
                        System.Diagnostics.Debug.WriteLine("Executing SQL command");
                        var result = await command.ExecuteScalarAsync();
                        System.Diagnostics.Debug.WriteLine($"SQL command executed, result: {result}");
                        
                        if (result != null && result != DBNull.Value)
                        {
                            int newId = Convert.ToInt32(result);
                            System.Diagnostics.Debug.WriteLine($"New question ID: {newId}");
                            return newId;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("No ID returned from database");
                            return 0;
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                System.Diagnostics.Debug.WriteLine($"SQL Exception: {sqlEx.Message}");
                System.Diagnostics.Debug.WriteLine($"Error Number: {sqlEx.Number}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {sqlEx.StackTrace}");
                throw; // Rethrow to be handled by caller
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"General Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                throw; // Rethrow to be handled by caller
            }
        }
        
        private async Task<bool> DeleteChallengeQuestion(int questionId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = "DELETE FROM ChallengeQuestions WHERE QuestionId = @QuestionId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@QuestionId", questionId);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        private async Task<bool> TestDatabaseConnection()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    System.Diagnostics.Debug.WriteLine($"Connection string (partial): {_connectionString.Substring(0, Math.Min(20, _connectionString.Length))}...");
                    await connection.OpenAsync();
                    System.Diagnostics.Debug.WriteLine("Database connection opened successfully");
                    
                    // Check if ChallengeQuestions table exists
                    string tableCheckQuery = @"
                        SELECT CASE WHEN EXISTS (
                            SELECT 1 FROM INFORMATION_SCHEMA.TABLES 
                            WHERE TABLE_NAME = 'ChallengeQuestions'
                        ) THEN 1 ELSE 0 END";
                    
                    using (SqlCommand command = new SqlCommand(tableCheckQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        bool tableExists = Convert.ToBoolean(result);
                        System.Diagnostics.Debug.WriteLine($"ChallengeQuestions table exists: {tableExists}");
                        
                        if (tableExists)
                        {
                            // Try a simple insert and delete to test write permissions
                            string testInsertQuery = @"
                                BEGIN TRANSACTION;
                                
                                DECLARE @TestId INT;
                                
                                INSERT INTO ChallengeQuestions 
                                    (ChallengeId, QuestionText, AnswerText, Points, CreatedDate) 
                                VALUES 
                                    (0, 'TEST_QUESTION', 'TEST_ANSWER', 1, GETDATE());
                                    
                                SET @TestId = SCOPE_IDENTITY();
                                
                                DELETE FROM ChallengeQuestions WHERE QuestionId = @TestId;
                                
                                COMMIT;
                                
                                SELECT 1;";
                            
                            try
                            {
                                using (SqlCommand testCommand = new SqlCommand(testInsertQuery, connection))
                                {
                                    var testResult = await testCommand.ExecuteScalarAsync();
                                    System.Diagnostics.Debug.WriteLine($"Test insert/delete succeeded: {testResult != null}");
                                    return true;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Test insert failed: {ex.Message}");
                                return false;
                            }
                        }
                        return tableExists;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database connection test failed: {ex.Message}");
                return false;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddQuestionDirect()
        {
            System.Diagnostics.Debug.WriteLine("======== ADD QUESTION DIRECT STARTED ========");
            
            try
            {
                // Manually read form values instead of relying on model binding
                int challengeId = int.Parse(Request.Form["ChallengeId"]);
                string questionText = Request.Form["QuestionText"];
                string answerText = Request.Form["AnswerText"];
                string codeSnippet = Request.Form["CodeSnippet"];
                int points = int.Parse(Request.Form["Points"]);
                
                System.Diagnostics.Debug.WriteLine($"Form values - ChallengeId: {challengeId}, Question: {questionText.Substring(0, Math.Min(20, questionText.Length))}...");
                
                // Get teacher ID from session
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    System.Diagnostics.Debug.WriteLine("TeacherId is missing from session");
                    return RedirectToAction("Login", "Home");
                }
                
                // Verify challenge belongs to teacher
                var challenge = await GetChallenge(challengeId);
                
                if (challenge == null || challenge.TeacherId != teacherId)
                {
                    System.Diagnostics.Debug.WriteLine("Challenge not found or doesn't belong to teacher");
                    return NotFound();
                }
                
                // Create question object
                var question = new ChallengeQuestion
                {
                    ChallengeId = challengeId,
                    QuestionText = questionText,
                    AnswerText = answerText,
                    CodeSnippet = string.IsNullOrEmpty(codeSnippet) ? null : codeSnippet,
                    Points = points,
                    CreatedDate = DateTime.Now
                };
                
                // Insert question
                System.Diagnostics.Debug.WriteLine("Attempting to insert question directly");
                
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string query = @"
                        INSERT INTO ChallengeQuestions (ChallengeId, QuestionText, AnswerText, CodeSnippet, Points, CreatedDate)
                        VALUES (@ChallengeId, @QuestionText, @AnswerText, @CodeSnippet, @Points, @CreatedDate);
                        SELECT SCOPE_IDENTITY();";
                    
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ChallengeId", question.ChallengeId);
                        command.Parameters.AddWithValue("@QuestionText", question.QuestionText);
                        command.Parameters.AddWithValue("@AnswerText", question.AnswerText);
                        command.Parameters.AddWithValue("@CodeSnippet", (object)question.CodeSnippet ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Points", question.Points);
                        command.Parameters.AddWithValue("@CreatedDate", question.CreatedDate);
                        
                        var result = await command.ExecuteScalarAsync();
                        System.Diagnostics.Debug.WriteLine($"Direct insert result: {result}");
                        
                        if (result != null && result != DBNull.Value)
                        {
                            int newId = Convert.ToInt32(result);
                            System.Diagnostics.Debug.WriteLine($"New question ID: {newId}");
                            
                            // Update scores for affected students after adding a question directly
                            _logger.LogInformation($"Updating scores for students who have submitted challenge {challengeId}");
                            await RecalculateScoresForChallengeSubmitters(challengeId);
                            
                            TempData["Success"] = "Question added successfully (direct method)!";
                            return RedirectToAction("Edit", new { id = challengeId });
                        }
                    }
                }
                
                TempData["Error"] = "Failed to add question using direct method.";
                return RedirectToAction("Edit", new { id = challengeId });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Direct insert exception: {ex.Message}");
                TempData["Error"] = $"Error: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        // Temporary endpoint to check ChallengeSubmissions schema
        [HttpGet]
        public async Task<IActionResult> CheckChallengeSubmissionsSchema()
        {
            try
            {
                StringBuilder result = new StringBuilder();
                result.AppendLine("ChallengeSubmissions Table Schema:");
                
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if ChallengeSubmissions table exists and get its columns
                    string tableCheckQuery = @"
                        SELECT 
                            c.name AS ColumnName,
                            ty.name AS DataType,
                            c.max_length AS MaxLength,
                            c.is_nullable AS IsNullable,
                            c.is_identity AS IsIdentity
                        FROM 
                            sys.tables t
                            INNER JOIN sys.columns c ON t.object_id = c.object_id
                            INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                        WHERE 
                            t.name = 'ChallengeSubmissions'
                        ORDER BY 
                            c.column_id";
                    
                    using (SqlCommand command = new SqlCommand(tableCheckQuery, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                result.AppendLine("ChallengeSubmissions table does not exist!");
                            }
                            else
                            {
                                result.AppendLine("\nColumn Name | Data Type | Max Length | Is Nullable | Is Identity");
                                result.AppendLine("------------------------------");
                                
                                while (await reader.ReadAsync())
                                {
                                    string columnName = reader["ColumnName"].ToString();
                                    string dataType = reader["DataType"].ToString();
                                    int maxLength = Convert.ToInt32(reader["MaxLength"]);
                                    bool isNullable = Convert.ToBoolean(reader["IsNullable"]);
                                    bool isIdentity = Convert.ToBoolean(reader["IsIdentity"]);
                                    
                                    result.AppendLine($"{columnName} | {dataType} | {maxLength} | {isNullable} | {isIdentity}");
                                }
                            }
                        }
                    }
                }
                
                return Content(result.ToString(), "text/plain");
            }
            catch (Exception ex)
            {
                return Content($"Error checking schema: {ex.Message}", "text/plain");
            }
        }

        [HttpGet]
        public async Task<IActionResult> EnsureChallengeSubmissionsTable()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if table exists
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'ChallengeSubmissions'";
                    
                    bool tableExists = false;
                    using (SqlCommand command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        tableExists = (count > 0);
                    }
                    
                    if (!tableExists)
                    {
                        // Create table
                        string createTableQuery = @"
                            CREATE TABLE ChallengeSubmissions (
                                SubmissionId INT IDENTITY(1,1) PRIMARY KEY,
                                ChallengeId INT NOT NULL,
                                StudentId NVARCHAR(50) NOT NULL,
                                SubmissionDate DATETIME NOT NULL DEFAULT GETDATE(),
                                PercentageScore INT NOT NULL DEFAULT 0,
                                PointsEarned INT NOT NULL DEFAULT 0,
                                TotalPoints INT NOT NULL DEFAULT 0,
                                CONSTRAINT FK_ChallengeSubmissions_Challenges 
                                    FOREIGN KEY (ChallengeId) REFERENCES Challenges(ChallengeId)
                            )";
                        
                        using (SqlCommand command = new SqlCommand(createTableQuery, connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            return Content("ChallengeSubmissions table created successfully!", "text/plain");
                        }
                    }
                    else
                    {
                        // Check if required columns exist
                        string checkColumnsQuery = @"
                            SELECT 
                                COUNT(*) AS ColumnCount
                            FROM 
                                INFORMATION_SCHEMA.COLUMNS 
                            WHERE 
                                TABLE_NAME = 'ChallengeSubmissions' 
                                AND COLUMN_NAME IN ('PercentageScore', 'PointsEarned', 'TotalPoints')";
                        
                        int existingColumns = 0;
                        using (SqlCommand command = new SqlCommand(checkColumnsQuery, connection))
                        {
                            existingColumns = Convert.ToInt32(await command.ExecuteScalarAsync());
                        }
                        
                        if (existingColumns < 3)
                        {
                            // Check if Score column exists - need to decide whether to convert it
                            string checkScoreQuery = @"
                                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                                WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'Score'";
                            
                            bool hasScoreColumn = false;
                            using (SqlCommand command = new SqlCommand(checkScoreQuery, connection))
                            {
                                hasScoreColumn = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
                            }
                            
                            // Let's just add the missing columns
                            StringBuilder alterTableSql = new StringBuilder();
                            
                            if (!ColumnExists(connection, "ChallengeSubmissions", "PercentageScore").Result)
                            {
                                alterTableSql.AppendLine("ALTER TABLE ChallengeSubmissions ADD PercentageScore INT NOT NULL DEFAULT 0;");
                                
                                // If we have a Score column, copy its values to PercentageScore
                                if (hasScoreColumn)
                                {
                                    alterTableSql.AppendLine("UPDATE ChallengeSubmissions SET PercentageScore = Score;");
                                }
                            }
                            
                            if (!ColumnExists(connection, "ChallengeSubmissions", "PointsEarned").Result)
                            {
                                alterTableSql.AppendLine("ALTER TABLE ChallengeSubmissions ADD PointsEarned INT NOT NULL DEFAULT 0;");
                            }
                            
                            if (!ColumnExists(connection, "ChallengeSubmissions", "TotalPoints").Result)
                            {
                                alterTableSql.AppendLine("ALTER TABLE ChallengeSubmissions ADD TotalPoints INT NOT NULL DEFAULT 0;");
                            }
                            
                            if (alterTableSql.Length > 0)
                            {
                                using (SqlCommand command = new SqlCommand(alterTableSql.ToString(), connection))
                                {
                                    await command.ExecuteNonQueryAsync();
                                    return Content("Missing columns added to ChallengeSubmissions table!", "text/plain");
                                }
                            }
                        }
                        
                        return Content("ChallengeSubmissions table already exists with proper structure!", "text/plain");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content($"Error: {ex.Message}\n{ex.StackTrace}", "text/plain");
            }
        }
        
        // Helper method to check if a column exists
        private async Task<bool> ColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
                
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }

        // New diagnostic endpoint to test challenge submission
        [HttpGet]
        public async Task<IActionResult> TestChallengeSubmission(int challengeId = 1)
        {
            try
            {
                // Get the first challenge if id not provided
                if (challengeId <= 0)
                {
                    var allChallenges = await GetAllChallenges();
                    if (allChallenges.Any())
                    {
                        challengeId = allChallenges.First().ChallengeId;
                    }
                    else
                    {
                        return Content("No challenges found in the database.", "text/plain");
                    }
                }
                
                // Get the challenge to make sure it exists
                var challenge = await GetChallenge(challengeId);
                if (challenge == null)
                {
                    return Content($"Challenge with ID {challengeId} not found.", "text/plain");
                }
                
                // Get test questions for this challenge
                var questions = await GetChallengeQuestions(challengeId);
                if (questions == null || !questions.Any())
                {
                    return Content($"Challenge has no questions.", "text/plain");
                }
                
                StringBuilder result = new StringBuilder();
                result.AppendLine($"Testing challenge submission for Challenge ID: {challengeId}");
                result.AppendLine($"Challenge Name: {challenge.ChallengeName}");
                result.AppendLine($"Question Count: {questions.Count}");
                
                // Attempt direct table creation if needed
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Ensure the table exists with correct columns
                    string createTableQuery = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ChallengeSubmissions')
                        BEGIN
                            CREATE TABLE ChallengeSubmissions (
                                SubmissionId INT IDENTITY(1,1) PRIMARY KEY,
                                ChallengeId INT NOT NULL,
                                StudentId NVARCHAR(50) NOT NULL,
                                SubmissionDate DATETIME NOT NULL DEFAULT GETDATE(),
                                PercentageScore INT NOT NULL DEFAULT 0,
                                PointsEarned INT NOT NULL DEFAULT 0,
                                TotalPoints INT NOT NULL DEFAULT 0
                            )
                        END
                        ELSE
                        BEGIN
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'PercentageScore')
                            BEGIN
                                ALTER TABLE ChallengeSubmissions ADD PercentageScore INT NOT NULL DEFAULT 0
                            END
                            
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'PointsEarned')
                            BEGIN
                                ALTER TABLE ChallengeSubmissions ADD PointsEarned INT NOT NULL DEFAULT 0
                            END
                            
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ChallengeSubmissions' AND COLUMN_NAME = 'TotalPoints')
                            BEGIN
                                ALTER TABLE ChallengeSubmissions ADD TotalPoints INT NOT NULL DEFAULT 0
                            END
                        END";
                    
                    using (SqlCommand command = new SqlCommand(createTableQuery, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        result.AppendLine("Ensured ChallengeSubmissions table exists with required columns.");
                    }
                    
                    // Show current schema
                    string schemaQuery = @"
                        SELECT
                            COLUMN_NAME,
                            DATA_TYPE,
                            IS_NULLABLE
                        FROM INFORMATION_SCHEMA.COLUMNS
                        WHERE TABLE_NAME = 'ChallengeSubmissions'
                        ORDER BY ORDINAL_POSITION";
                    
                    using (SqlCommand command = new SqlCommand(schemaQuery, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            result.AppendLine("\nCurrent ChallengeSubmissions Schema:");
                            result.AppendLine("----------------------------------");
                            
                            if (!reader.HasRows)
                            {
                                result.AppendLine("No columns found!");
                            }
                            else
                            {
                                result.AppendLine("Column Name | Data Type | Nullable");
                                
                                while (await reader.ReadAsync())
                                {
                                    string colName = reader["COLUMN_NAME"].ToString();
                                    string dataType = reader["DATA_TYPE"].ToString();
                                    string isNullable = reader["IS_NULLABLE"].ToString();
                                    
                                    result.AppendLine($"{colName} | {dataType} | {isNullable}");
                                }
                            }
                        }
                    }
                    
                    // Try to insert a test record
                    string testInsertQuery = @"
                        INSERT INTO ChallengeSubmissions 
                            (ChallengeId, StudentId, SubmissionDate, PercentageScore, PointsEarned, TotalPoints)
                        VALUES 
                            (@ChallengeId, @StudentId, @SubmissionDate, @PercentageScore, @PointsEarned, @TotalPoints);
                        SELECT SCOPE_IDENTITY();";
                    
                    try
                    {
                        using (SqlCommand command = new SqlCommand(testInsertQuery, connection))
                        {
                            command.Parameters.AddWithValue("@ChallengeId", challengeId);
                            command.Parameters.AddWithValue("@StudentId", "TEST_USER");
                            command.Parameters.AddWithValue("@SubmissionDate", DateTime.Now);
                            command.Parameters.AddWithValue("@PercentageScore", 85);
                            command.Parameters.AddWithValue("@PointsEarned", 17);
                            command.Parameters.AddWithValue("@TotalPoints", 20);
                            
                            var insertResult = await command.ExecuteScalarAsync();
                            
                            if (insertResult != null && insertResult != DBNull.Value)
                            {
                                int submissionId = Convert.ToInt32(insertResult);
                                result.AppendLine($"\nTest submission created successfully! ID: {submissionId}");
                                
                                // Clean up the test record
                                string deleteQuery = "DELETE FROM ChallengeSubmissions WHERE SubmissionId = @SubmissionId";
                                using (SqlCommand deleteCmd = new SqlCommand(deleteQuery, connection))
                                {
                                    deleteCmd.Parameters.AddWithValue("@SubmissionId", submissionId);
                                    int rowsDeleted = await deleteCmd.ExecuteNonQueryAsync();
                                    result.AppendLine($"Test record cleaned up: {rowsDeleted} row(s) deleted.");
                                }
                                
                                return Content(result.ToString(), "text/plain");
                            }
                            else
                            {
                                result.AppendLine("\nFailed to get ID of inserted record.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine($"\nError during test insert: {ex.Message}");
                        result.AppendLine($"Stack trace: {ex.StackTrace}");
                    }
                }
                
                return Content(result.ToString(), "text/plain");
            }
            catch (Exception ex)
            {
                return Content($"Test failed with error: {ex.Message}\n{ex.StackTrace}", "text/plain");
            }
        }
        
        // Helper method to get all challenges
        private async Task<List<Challenge>> GetAllChallenges()
        {
            List<Challenge> challenges = new List<Challenge>();
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string query = "SELECT * FROM Challenges ORDER BY ChallengeId";
                
                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        challenges.Add(new Challenge
                        {
                            ChallengeId = Convert.ToInt32(reader["ChallengeId"]),
                            TeacherId = reader["TeacherId"].ToString(),
                            ChallengeName = reader["ChallengeName"].ToString(),
                            ProgrammingLanguage = reader["ProgrammingLanguage"].ToString(),
                            Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : null,
                            YearLevel = reader["YearLevel"].ToString(),
                            CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                            LastUpdatedDate = reader["LastUpdatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["LastUpdatedDate"]) : null,
                            VisibleFromDate = reader["VisibleFromDate"] != DBNull.Value ? Convert.ToDateTime(reader["VisibleFromDate"]) : null,
                            ExpirationDate = reader["ExpirationDate"] != DBNull.Value ? Convert.ToDateTime(reader["ExpirationDate"]) : null,
                            IsActive = Convert.ToBoolean(reader["IsActive"]),
                            Questions = new List<ChallengeQuestion>()
                        });
                    }
                }
            }
            
            return challenges;
        }

        // Add a diagnostic endpoint to understand the ID relationships
        [HttpGet]
        public async Task<IActionResult> DiagnoseUserIds(string studentId = null)
        {
            StringBuilder result = new StringBuilder();
            result.AppendLine("User ID Diagnostics\n");
            
            if (string.IsNullOrEmpty(studentId))
            {
                // If no ID provided, use the one from session
                studentId = HttpContext.Session.GetString("IdNumber");
            }
            
            if (string.IsNullOrEmpty(studentId))
            {
                return Content("No student ID provided or found in session", "text/plain");
            }
            
            result.AppendLine($"Student ID (IdNumber/Username): {studentId}");
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Look up in Users table
                string usersQuery = @"
                    SELECT UserId, Username, FullName, Role 
                    FROM Users 
                    WHERE Username = @IdNumber";
                
                using (SqlCommand command = new SqlCommand(usersQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            result.AppendLine("\nFound in Users table:");
                            result.AppendLine($"  UserId: {reader["UserId"]}");
                            result.AppendLine($"  Username: {reader["Username"]}");
                            result.AppendLine($"  FullName: {reader["FullName"]}");
                            result.AppendLine($"  Role: {reader["Role"]}");
                        }
                        else
                        {
                            result.AppendLine("\nNot found in Users table");
                        }
                    }
                }
                
                // Look up in StudentDetails table
                string studentDetailsQuery = @"
                    SELECT sd.*, u.UserId, u.FullName
                    FROM StudentDetails sd
                    LEFT JOIN Users u ON sd.UserId = u.UserId
                    WHERE u.Username = @IdNumber";
                
                using (SqlCommand command = new SqlCommand(studentDetailsQuery, connection))
                {
                    command.Parameters.AddWithValue("@IdNumber", studentId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            result.AppendLine("\nFound in StudentDetails table:");
                            result.AppendLine($"  StudentDetails.UserId: {reader["UserId"]}");
                            result.AppendLine($"  IdNumber: {reader["IdNumber"]}");
                            result.AppendLine($"  FullName: {reader["FullName"]}");
                            
                            if (ReaderHasColumn(reader, "GradeLevel"))
                            {
                                result.AppendLine($"  GradeLevel: {reader["GradeLevel"]}");
                            }
                        }
                        else
                        {
                            result.AppendLine("\nNot found in StudentDetails table");
                        }
                    }
                }
                
                // Check constraints on ChallengeSubmissions table
                string constraintQuery = @"
                    SELECT 
                        fk.name AS ConstraintName,
                        OBJECT_NAME(fk.parent_object_id) AS TableName,
                        COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
                        OBJECT_NAME(fk.referenced_object_id) AS ReferencedTableName,
                        COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumnName
                    FROM 
                        sys.foreign_keys fk
                        INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                    WHERE 
                        OBJECT_NAME(fk.parent_object_id) = 'ChallengeSubmissions'";
                
                using (SqlCommand command = new SqlCommand(constraintQuery, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        result.AppendLine("\nForeign Key Constraints on ChallengeSubmissions:");
                        
                        if (!reader.HasRows)
                        {
                            result.AppendLine("  No constraints found");
                        }
                        else
                        {
                            while (await reader.ReadAsync())
                            {
                                result.AppendLine($"  {reader["ConstraintName"]}:");
                                result.AppendLine($"    {reader["TableName"]}.{reader["ColumnName"]} -> {reader["ReferencedTableName"]}.{reader["ReferencedColumnName"]}");
                            }
                        }
                    }
                }
            }
            
            return Content(result.ToString(), "text/plain");
        }

        [HttpGet]
        public async Task<IActionResult> ExamineDbSchema()
        {
            StringBuilder result = new StringBuilder();
            result.AppendLine("Database Schema Examination");
            result.AppendLine("=========================");
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Users table columns
                result.AppendLine("\nUsers Table:");
                string usersColumnsQuery = @"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'Users'
                    ORDER BY ORDINAL_POSITION";
                
                using (SqlCommand command = new SqlCommand(usersColumnsQuery, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows)
                        {
                            result.AppendLine("  No columns found or table doesn't exist!");
                        }
                        else
                        {
                            result.AppendLine("  Column Name | Data Type | Nullable");
                            result.AppendLine("  ------------------------------");
                            
                            while (await reader.ReadAsync())
                            {
                                string columnName = reader["COLUMN_NAME"].ToString();
                                string dataType = reader["DATA_TYPE"].ToString();
                                string isNullable = reader["IS_NULLABLE"].ToString();
                                
                                result.AppendLine($"  {columnName} | {dataType} | {isNullable}");
                            }
                        }
                    }
                }
                
                // StudentDetails table columns
                result.AppendLine("\nStudentDetails Table:");
                string studentDetailsColumnsQuery = @"
                    SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'StudentDetails'
                    ORDER BY ORDINAL_POSITION";
                
                using (SqlCommand command = new SqlCommand(studentDetailsColumnsQuery, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows)
                        {
                            result.AppendLine("  No columns found or table doesn't exist!");
                        }
                        else
                        {
                            result.AppendLine("  Column Name | Data Type | Nullable");
                            result.AppendLine("  ------------------------------");
                            
                            while (await reader.ReadAsync())
                            {
                                string columnName = reader["COLUMN_NAME"].ToString();
                                string dataType = reader["DATA_TYPE"].ToString();
                                string isNullable = reader["IS_NULLABLE"].ToString();
                                
                                result.AppendLine($"  {columnName} | {dataType} | {isNullable}");
                            }
                        }
                    }
                }
                
                // Check for any sample data in Users table
                result.AppendLine("\nSample Users Data (First 5 rows):");
                string usersDataQuery = "SELECT TOP 5 * FROM Users";
                
                using (SqlCommand command = new SqlCommand(usersDataQuery, connection))
                {
                    try
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                result.AppendLine("  No rows found in Users table!");
                            }
                            else
                            {
                                // Get column names
                                List<string> columnNames = new List<string>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    columnNames.Add(reader.GetName(i));
                                }
                                
                                result.AppendLine("  " + string.Join(" | ", columnNames));
                                result.AppendLine("  " + new string('-', columnNames.Count * 15));
                                
                                while (await reader.ReadAsync())
                                {
                                    List<string> rowValues = new List<string>();
                                    
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        string value = reader[i]?.ToString() ?? "NULL";
                                        // Truncate long values
                                        if (value.Length > 20) value = value.Substring(0, 17) + "...";
                                        rowValues.Add(value);
                                    }
                                    
                                    result.AppendLine("  " + string.Join(" | ", rowValues));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine($"  Error retrieving Users data: {ex.Message}");
                    }
                }
            }
            
            return Content(result.ToString(), "text/plain");
        }

        [HttpGet]
        public async Task<IActionResult> CompletedChallenges()
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Get the student's year level from database
            int studentYearLevel = 0;
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT GradeLevel
                    FROM StudentDetails
                    WHERE IdNumber = @StudentId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        studentYearLevel = Convert.ToInt32(result);
                    }
                }
            }
            
            // Get completed challenges for the student
            var completedChallenges = await GetCompletedChallengesForStudent(studentId);
            ViewBag.StudentId = studentId;
            ViewBag.StudentYearLevel = studentYearLevel;
            
            return View(completedChallenges);
        }

        /// <summary>
        /// Updates the CompletedChallengesScore for a student based on their challenge submissions
        /// </summary>
        private async Task UpdateCompletedChallengesScore(string studentId)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First, get the UserId for this student - try by both Username and IdNumber
                    string getUserIdQuery = @"
                        SELECT U.UserId 
                        FROM Users U
                        LEFT JOIN StudentDetails SD ON U.UserId = SD.UserId
                        WHERE U.Username = @IdNumber OR SD.IdNumber = @IdNumber";
                    
                    string userId = null;
                    
                    using (SqlCommand command = new SqlCommand(getUserIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@IdNumber", studentId);
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            userId = result.ToString();
                        }
                        else
                        {
                            // If no UserId found, can't update score
                            _logger.LogError($"Could not find student with ID {studentId} for challenge score update");
                            return;
                        }
                    }
                    
                    // Get the IdNumber for this student to update the score correctly
                    string getIdNumberQuery = @"
                        SELECT IdNumber 
                        FROM StudentDetails 
                        WHERE UserId = @UserId";
                    
                    string idNumber = studentId; // Default to the passed-in value
                    
                    using (SqlCommand command = new SqlCommand(getIdNumberQuery, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            idNumber = result.ToString();
                        }
                    }
                    
                    // Check if the DeletedChallenges table exists, create it if it doesn't
                    await EnsureDeletedChallengesTableExists(connection);
                    
                    // Find the earliest school year when the student participated in a challenge
                    string firstYearQuery = @"
                        SELECT MIN(c.YearLevel) as FirstYear
                        FROM ChallengeSubmissions cs
                        JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                        WHERE cs.StudentId = @StudentId";
                    
                    string firstParticipationYear = null;
                    
                    using (SqlCommand command = new SqlCommand(firstYearQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", userId);
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            firstParticipationYear = result.ToString();
                        }
                        else
                        {
                            // No challenges attempted yet, nothing to calculate
                            return;
                        }
                    }
                    
                    // Get list of deleted challenges for this student
                    HashSet<int> deletedChallengeIds = new HashSet<int>();
                    string deletedChallengesQuery = @"
                        SELECT ChallengeId 
                        FROM DeletedChallenges 
                        WHERE StudentId = @StudentId";
                    
                    using (SqlCommand command = new SqlCommand(deletedChallengesQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", userId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                deletedChallengeIds.Add(Convert.ToInt32(reader["ChallengeId"]));
                            }
                        }
                    }
                    
                    if (deletedChallengeIds.Count > 0)
                    {
                        _logger.LogInformation($"Found {deletedChallengeIds.Count} deleted challenges for student {userId}: {string.Join(", ", deletedChallengeIds)}");
                    }
                    
                    // Get all challenge submissions for this student with their scores, but only from challenges
                    // that belong to the first participation year onwards
                    List<int> allPercentageScores = new List<int>();
                    int totalPointsEarned = 0;
                    int completedChallengeItems = 0;
                    int totalItemsInAllChallenges = 0;
                    
                    // First, get the total available points from ALL challenges in the school year
                    // EXCLUDING deleted challenges for repeater students
                    string allChallengesQuery = @"
                        SELECT SUM(cq.Points) AS TotalAvailablePoints
                        FROM ChallengeQuestions cq
                        JOIN Challenges c ON cq.ChallengeId = c.ChallengeId
                        WHERE c.YearLevel >= @FirstYear 
                        AND c.IsActive = 1";
                        
                    if (deletedChallengeIds.Count > 0)
                    {
                        allChallengesQuery += $" AND c.ChallengeId NOT IN ({string.Join(",", deletedChallengeIds)})";
                    }
                    
                    using (SqlCommand command = new SqlCommand(allChallengesQuery, connection))
                    {
                        command.Parameters.AddWithValue("@FirstYear", firstParticipationYear);
                        var result = await command.ExecuteScalarAsync();
                        
                        if (result != null && result != DBNull.Value)
                        {
                            totalItemsInAllChallenges = Convert.ToInt32(result);
                        }
                    }
                    
                    // Then get points earned from completed challenges (excluding deleted ones)
                    string submissionsQuery = @"
                        SELECT cs.PercentageScore, cs.PointsEarned, cs.TotalPoints, c.ChallengeId
                        FROM ChallengeSubmissions cs
                        JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                        WHERE cs.StudentId = @StudentId
                        AND c.YearLevel >= @FirstYear";
                        
                    if (deletedChallengeIds.Count > 0)
                    {
                        submissionsQuery += $" AND c.ChallengeId NOT IN ({string.Join(",", deletedChallengeIds)})";
                    }
                    
                    using (SqlCommand command = new SqlCommand(submissionsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", userId);
                        command.Parameters.AddWithValue("@FirstYear", firstParticipationYear);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                int percentageScore = reader["PercentageScore"] != DBNull.Value ? 
                                    Convert.ToInt32(reader["PercentageScore"]) : 0;
                                
                                int pointsEarned = reader["PointsEarned"] != DBNull.Value ?
                                    Convert.ToInt32(reader["PointsEarned"]) : 0;
                                    
                                int totalPoints = reader["TotalPoints"] != DBNull.Value ?
                                    Convert.ToInt32(reader["TotalPoints"]) : 0;
                                
                                int challengeId = Convert.ToInt32(reader["ChallengeId"]);
                                
                                // Double-check that this isn't a deleted challenge
                                if (!deletedChallengeIds.Contains(challengeId))
                                {
                                    allPercentageScores.Add(percentageScore);
                                    totalPointsEarned += pointsEarned;
                                    completedChallengeItems += totalPoints;
                                }
                            }
                        }
                    }
                    
                    // Calculate the challenge score: (total score / total items of ALL challenges) * 100 * (weight/100)
                    decimal challengeScore = 0;

                    // Get the current CompletedChallenges weight from the ScoreWeights table
                    decimal challengesWeight = 20.0m; // Default to 20% if not found
                    string weightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
                    using (var weightCmd = new SqlCommand(weightQuery, connection))
                    {
                        var weightResult = await weightCmd.ExecuteScalarAsync();
                        if (weightResult != null && weightResult != DBNull.Value)
                        {
                            challengesWeight = Convert.ToDecimal(weightResult);
                            _logger.LogInformation($"Retrieved current CompletedChallenges weight from database: {challengesWeight}%");
                        }
                        else
                        {
                            _logger.LogInformation("Using default CompletedChallenges weight of 20%");
                        }
                    }

                    if (totalItemsInAllChallenges > 0)
                    {
                        // Use the formula: (total points earned / total possible points) * 100 * (weight/100)
                        challengeScore = ((decimal)totalPointsEarned / totalItemsInAllChallenges) * 100 * (challengesWeight / 100.0m);
                        
                        _logger.LogInformation($"Calculated challenges score using formula: ({totalPointsEarned}/{totalItemsInAllChallenges}) * 100 * ({challengesWeight}/100) = {challengeScore}");
                        
                        // Ensure the score doesn't exceed the maximum possible (100% * weight%)
                        decimal maxScore = challengesWeight;
                        if (challengeScore > maxScore)
                        {
                            challengeScore = maxScore;
                            _logger.LogInformation($"Capped challenges score at maximum value of {maxScore}");
                        }
                    }
                    
                    // Update the student's CompletedChallengesScore
                    string updateScoreQuery = @"
                        UPDATE StudentDetails
                        SET CompletedChallengesScore = @ChallengeScore
                        WHERE IdNumber = @StudentId";
                    
                    using (SqlCommand command = new SqlCommand(updateScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", idNumber);
                        command.Parameters.AddWithValue("@ChallengeScore", challengeScore);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Successfully updated CompletedChallengesScore to {challengeScore} for student {idNumber}");
                            
                            // Now calculate the overall score by adding all 5 category scores
                            decimal academicScore = 0;
                            decimal extracurricularScore = 0;
                            decimal seminarScore = 0;
                            decimal masteryScore = 0;
                            
                            // Get the current values for all scores
                            string getScoresQuery = @"
                                SELECT 
                                    COALESCE(AcademicGradesScore, 0) AS AcademicScore,
                                    COALESCE(ExtracurricularScore, 0) AS ExtracurricularScore,
                                    COALESCE(SeminarsWebinarsScore, 0) AS SeminarScore,
                                    COALESCE(MasteryScore, 0) AS MasteryScore
                                FROM StudentDetails
                                WHERE IdNumber = @StudentId";
                            
                            using (SqlCommand scoreCmd = new SqlCommand(getScoresQuery, connection))
                            {
                                scoreCmd.Parameters.AddWithValue("@StudentId", idNumber);
                                
                                using (var scoreReader = await scoreCmd.ExecuteReaderAsync())
                                {
                                    if (await scoreReader.ReadAsync())
                                    {
                                        academicScore = Convert.ToDecimal(scoreReader["AcademicScore"]);
                                        extracurricularScore = Convert.ToDecimal(scoreReader["ExtracurricularScore"]);
                                        seminarScore = Convert.ToDecimal(scoreReader["SeminarScore"]);
                                        masteryScore = Convert.ToDecimal(scoreReader["MasteryScore"]);
                                    }
                                }
                            }
                            
                            // Calculate overall score as the sum of all component scores
                            decimal overallScore = 
                                academicScore +        // Already weighted
                                extracurricularScore + // Raw score
                                seminarScore +         // Already weighted
                                masteryScore +         // Already weighted
                                challengeScore;        // Already weighted
                            
                            _logger.LogInformation($"Calculated overall score: {academicScore} + {extracurricularScore} + {seminarScore} + {masteryScore} + {challengeScore} = {overallScore}");
                            
                            // Update the overall score in the database
                            string updateOverallScoreQuery = @"
                                UPDATE StudentDetails
                                SET Score = @OverallScore
                                WHERE IdNumber = @StudentId";
                            
                            using (SqlCommand updateOverallCmd = new SqlCommand(updateOverallScoreQuery, connection))
                            {
                                updateOverallCmd.Parameters.AddWithValue("@OverallScore", overallScore);
                                updateOverallCmd.Parameters.AddWithValue("@StudentId", idNumber);
                                
                                int overallRowsAffected = await updateOverallCmd.ExecuteNonQueryAsync();
                                
                                if (overallRowsAffected > 0)
                                {
                                    _logger.LogInformation($"Successfully updated overall score to {overallScore} for student {idNumber}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Failed to update overall score for student {idNumber}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - we don't want to stop the submission process
                _logger.LogError(ex, $"Error updating challenge score for student {studentId}");
            }
        }

        private async Task<List<Challenge>> GetAvailableChallenges()
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            List<Challenge> availableChallenges = new List<Challenge>();
            
            _logger.LogInformation($"GetAvailableChallenges - Retrieving challenges for student ID: {studentId}");
            
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the student is a transferee
                bool isTransferee = await IsStudentTransferee(connection, studentId);
                _logger.LogInformation($"Student {studentId} is transferee: {isTransferee}");
                
                // Get accessible years for transferees
                List<string> accessibleYears = new List<string>();
                // Get individually accessible challenge IDs for transferees
                List<int> accessibleChallengeIds = new List<int>();
                
                if (isTransferee)
                {
                    accessibleYears = await GetTransfereeAccessibleYears(connection, studentId);
                    accessibleChallengeIds = await GetTransfereeAccessibleChallengeIds(connection, studentId);
                    
                    _logger.LogInformation($"Transferee student has access to {accessibleYears.Count} years and {accessibleChallengeIds.Count} individual challenges");
                    if (accessibleYears.Any())
                    {
                        _logger.LogInformation($"Accessible years: {string.Join(", ", accessibleYears)}");
                    }
                }
                
                string query = @"
                    SELECT c.ChallengeId, c.TeacherId, c.ChallengeName, c.ProgrammingLanguage, 
                           c.Description, c.YearLevel, c.CreatedDate, c.LastUpdatedDate,
                           c.VisibleFromDate, c.ExpirationDate, c.IsActive
                    FROM Challenges c
                    WHERE c.IsActive = 1";
                    
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Challenge challenge = new Challenge
                            {
                                ChallengeId = Convert.ToInt32(reader["ChallengeId"]),
                                TeacherId = reader["TeacherId"].ToString(),
                                ChallengeName = reader["ChallengeName"].ToString(),
                                ProgrammingLanguage = reader["ProgrammingLanguage"].ToString(),
                                Description = reader["Description"]?.ToString(),
                                YearLevel = reader["YearLevel"].ToString(),
                                CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                                IsActive = Convert.ToBoolean(reader["IsActive"]),
                                Questions = new List<ChallengeQuestion>()
                            };
                            
                            if (reader["LastUpdatedDate"] != DBNull.Value)
                            {
                                challenge.LastUpdatedDate = Convert.ToDateTime(reader["LastUpdatedDate"]);
                            }
                            
                            if (reader["VisibleFromDate"] != DBNull.Value)
                            {
                                challenge.VisibleFromDate = Convert.ToDateTime(reader["VisibleFromDate"]);
                            }
                            
                            if (reader["ExpirationDate"] != DBNull.Value)
                            {
                                challenge.ExpirationDate = Convert.ToDateTime(reader["ExpirationDate"]);
                            }
                            
                            bool isAvailable = true;
                            
                            // Check challenge visibility date
                            if (challenge.VisibleFromDate.HasValue && challenge.VisibleFromDate.Value > DateTime.Now)
                            {
                                isAvailable = false;
                                _logger.LogDebug($"Challenge {challenge.ChallengeId} ({challenge.ChallengeName}) not available - not yet visible");
                            }
                            
                            // Check challenge expiration date
                            if (challenge.ExpirationDate.HasValue && challenge.ExpirationDate.Value < DateTime.Now)
                            {
                                // Special case for transferee students with access to this year or this specific challenge
                                if (isTransferee && (accessibleYears.Contains(challenge.YearLevel) || 
                                    accessibleChallengeIds.Contains(challenge.ChallengeId)))
                                {
                                    // Allow access to expired challenges for transferees with specific permissions
                                    isAvailable = true;
                                    _logger.LogInformation($"Transferee student allowed access to expired challenge {challenge.ChallengeId} ({challenge.ChallengeName}) with year level {challenge.YearLevel}");
                                }
                                else
                                {
                                    isAvailable = false;
                                    _logger.LogDebug($"Challenge {challenge.ChallengeId} ({challenge.ChallengeName}) not available - expired");
                                }
                            }
                            
                            if (isAvailable)
                            {
                                availableChallenges.Add(challenge);
                                _logger.LogDebug($"Added challenge {challenge.ChallengeId} ({challenge.ChallengeName}) to available challenges, YearLevel: {challenge.YearLevel}");
                            }
                        }
                    }
                }
                
                // Load questions for each challenge
                foreach (var challenge in availableChallenges)
                {
                    challenge.Questions = await GetChallengeQuestions(challenge.ChallengeId);
                    _logger.LogDebug($"Loaded {challenge.Questions.Count} questions for challenge {challenge.ChallengeId} ({challenge.ChallengeName})");
                }
            }
            
            _logger.LogInformation($"GetAvailableChallenges - Returning {availableChallenges.Count} available challenges for student {studentId}");
            return availableChallenges;
        }
        
        private async Task<List<int>> GetTransfereeAccessibleChallengeIds(SqlConnection connection, string studentId)
        {
            List<int> accessibleChallengeIds = new List<int>();
            
            // Check if TransfereeChallengeAccess table exists
            if (!await TableExists(connection, "TransfereeChallengeAccess"))
            {
                _logger.LogWarning($"TransfereeChallengeAccess table does not exist for student {studentId}");
                return accessibleChallengeIds;
            }
                
            string query = @"
                SELECT ChallengeId 
                FROM TransfereeChallengeAccess 
                WHERE StudentId = @StudentId";
            
            try
            {    
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int challengeId = reader.GetInt32(0);
                            accessibleChallengeIds.Add(challengeId);
                            _logger.LogInformation($"Found accessible challenge ID for student {studentId}: {challengeId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving accessible challenge IDs for transferee student {studentId}");
            }
            
            _logger.LogInformation($"Total accessible challenge IDs for student {studentId}: {accessibleChallengeIds.Count}");
            return accessibleChallengeIds;
        }
        
        private async Task<bool> IsStudentTransferee(SqlConnection connection, string studentId)
        {
            string query = "SELECT IsTransferee FROM StudentDetails WHERE IdNumber = @StudentId";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                var result = await command.ExecuteScalarAsync();
                return result != null && result != DBNull.Value && (bool)result;
            }
        }
        
        private async Task<List<string>> GetTransfereeAccessibleYears(SqlConnection connection, string studentId)
        {
            List<string> accessibleYears = new List<string>();
            
            // Check if TransfereeStudentAccess table exists
            if (!await TableExists(connection, "TransfereeStudentAccess"))
            {
                _logger.LogWarning($"TransfereeStudentAccess table does not exist for student {studentId}");
                return accessibleYears;
            }
            
            // Check which column name exists
            bool hasSchoolYearColumn = await ColumnExists(connection, "TransfereeStudentAccess", "SchoolYear");
            bool hasYearLevelColumn = await ColumnExists(connection, "TransfereeStudentAccess", "YearLevel");
            
            string columnName = hasSchoolYearColumn ? "SchoolYear" : (hasYearLevelColumn ? "YearLevel" : "SchoolYear");
            
            _logger.LogInformation($"GetTransfereeAccessibleYears for student {studentId} - Using column: {columnName}");
                
            string query = $@"
                SELECT {columnName} 
                FROM TransfereeStudentAccess 
                WHERE StudentId = @StudentId";
                
            try
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string year = reader.GetString(0);
                            accessibleYears.Add(year);
                            _logger.LogInformation($"Found accessible year for student {studentId}: {year}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving accessible years for transferee student {studentId}");
            }
            
            _logger.LogInformation($"Total accessible years for student {studentId}: {accessibleYears.Count}");
            return accessibleYears;
        }
        
        // Helper method to ensure the DeletedChallenges table exists
        private async Task EnsureDeletedChallengesTableExists(SqlConnection connection)
        {
            try
            {
                // Check if the table already exists
                bool tableExists = await TableExists(connection, "DeletedChallenges");
                
                if (!tableExists)
                {
                    _logger.LogInformation("Creating DeletedChallenges table...");
                    
                    // Create the table
                    string createTableSql = @"
                        CREATE TABLE DeletedChallenges (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            StudentId NVARCHAR(128) NOT NULL,
                            ChallengeId INT NOT NULL,
                            YearLevel NVARCHAR(50) NOT NULL,
                            DeletedDate DATETIME NOT NULL DEFAULT GETDATE(),
                            CONSTRAINT UC_StudentChallenge UNIQUE (StudentId, ChallengeId)
                        )";
                    
                    using (SqlCommand command = new SqlCommand(createTableSql, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("DeletedChallenges table created successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating DeletedChallenges table");
                // Continue execution even if there's an error
            }
        }

        // Helper method to recalculate scores for all students who submitted a specific challenge
        private async Task RecalculateScoresForChallengeSubmitters(int challengeId)
        {
            try
            {
                _logger.LogInformation($"Starting recalculation of scores for challenge {challengeId}");
                
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First, get the year level of this challenge
                    string yearLevel = null;
                    string getYearLevelQuery = "SELECT YearLevel FROM Challenges WHERE ChallengeId = @ChallengeId";
                    using (SqlCommand command = new SqlCommand(getYearLevelQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ChallengeId", challengeId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            yearLevel = result.ToString();
                        }
                    }
                    
                    if (string.IsNullOrEmpty(yearLevel))
                    {
                        _logger.LogWarning($"Could not determine year level for challenge {challengeId}");
                        return;
                    }
                    
                    _logger.LogInformation($"Challenge {challengeId} is from year level: {yearLevel}");
                    
                    // Get all students who have submitted any challenge in this year level
                    List<string> affectedStudentIds = new List<string>();
                    string query = @"
                        SELECT DISTINCT 
                            COALESCE(sd.IdNumber, u.Username) AS IdNumber
                        FROM ChallengeSubmissions cs
                        JOIN Challenges c ON cs.ChallengeId = c.ChallengeId
                        JOIN Users u ON cs.StudentId = u.UserId
                        LEFT JOIN StudentDetails sd ON u.UserId = sd.UserId
                        WHERE c.YearLevel = @YearLevel";
                    
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@YearLevel", yearLevel);
                        
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                // Get the student's ID number rather than the UserId
                                if (reader["IdNumber"] != DBNull.Value)
                                {
                                    string idNumber = reader["IdNumber"].ToString();
                                    affectedStudentIds.Add(idNumber);
                                    _logger.LogInformation($"Found student {idNumber} who submitted a challenge in year level {yearLevel}");
                                }
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {affectedStudentIds.Count} students who have submitted challenges in year level {yearLevel}");
                    
                    // Update each student's score
                    foreach (var studentId in affectedStudentIds)
                    {
                        try
                        {
                            _logger.LogInformation($"Updating CompletedChallengesScore for student {studentId}");
                            await UpdateCompletedChallengesScore(studentId);
                            
                            // Update badge color
                            await _badgeService.UpdateBadgeColor(studentId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating score for student {studentId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in RecalculateScoresForChallengeSubmitters for challenge {challengeId}");
            }
        }
    }
}
