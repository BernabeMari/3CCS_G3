using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using StudentBadge.Services;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace StudentBadge.Controllers
{
    public class ProgrammingTestController : Controller
    {
        private readonly string _connectionString;
        private readonly CertificateService _certificateService;
        private readonly ILogger<ProgrammingTestController> _logger;
        private readonly BadgeService _badgeService;
        
        public ProgrammingTestController(IConfiguration configuration, CertificateService certificateService, 
            ILogger<ProgrammingTestController> logger, BadgeService badgeService)
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
            
            var tests = await GetTeacherProgrammingTests(teacherId);
            
            return View(tests);
        }
        
        [HttpGet]
        public IActionResult Create()
        {
            // Initialize a new test with an empty questions list to avoid validation errors
            return View(new ProgrammingTest 
            { 
                Questions = new List<ProgrammingQuestion>(),
                IsActive = true
            });
        }
        
        [HttpPost]
        public async Task<IActionResult> Create(ProgrammingTest programTest)
        {
            if (ModelState.IsValid)
            {
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Ensure TeacherId is set (as backup in case hidden field doesn't work)
                programTest.TeacherId = teacherId;
                programTest.CreatedDate = DateTime.Now;
                
                // Initialize empty Questions list
                programTest.Questions = new List<ProgrammingQuestion>();
                
                int testId = await CreateProgrammingTest(programTest);
                
                if (testId > 0)
                {
                    TempData["Success"] = "Programming test created successfully!";
                    return RedirectToAction("Edit", new { id = testId });
                }
                else
                {
                    ModelState.AddModelError("", "Failed to create the programming test.");
                }
            }
            
            return View(programTest);
        }
        
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            var existingTest = await GetProgrammingTest(id);
            
            if (existingTest == null || existingTest.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            // Load questions for this test
            existingTest.Questions = await GetProgrammingQuestions(id);
            
            return View(existingTest);
        }
        
        [HttpPost]
        public async Task<IActionResult> Edit(ProgrammingTest programTest)
        {
            if (ModelState.IsValid)
            {
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Verify this test belongs to the teacher
                var existingTest = await GetProgrammingTest(programTest.TestId);
                
                if (existingTest == null || existingTest.TeacherId != teacherId)
                {
                    return NotFound();
                }
                
                programTest.TeacherId = teacherId;
                programTest.LastUpdatedDate = DateTime.Now;
                
                bool success = await UpdateProgrammingTest(programTest);
                
                if (success)
                {
                    TempData["Success"] = "Programming test updated successfully!";
                    return RedirectToAction("Index");
                }
                else
                {
                    ModelState.AddModelError("", "Failed to update the programming test.");
                }
            }
            
            // Load questions for this test for the view
            programTest.Questions = await GetProgrammingQuestions(programTest.TestId);
            
            return View(programTest);
        }
        
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this test belongs to the teacher
            var existingTest = await GetProgrammingTest(id);
            
            if (existingTest == null || existingTest.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            var result = await DeleteProgrammingTest(id);
            
            if (result.Success)
            {
                TempData["Success"] = "Programming test deleted successfully!";
            }
            else
            {
                TempData["Error"] = $"Failed to delete the programming test: {result.ErrorMessage}";
            }
            
            return RedirectToAction("Index");
        }
        
        [HttpGet]
        public async Task<IActionResult> AddQuestion(int testId)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this test belongs to the teacher
            var parentTest = await GetProgrammingTest(testId);
            
            if (parentTest == null || parentTest.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            ViewBag.TestId = testId;
            ViewBag.TestName = parentTest.TestName;
            
            return View(new ProgrammingQuestion { TestId = testId });
        }
        
        [HttpPost]
        public async Task<IActionResult> AddQuestion(ProgrammingQuestion question)
        {
            // Remove Test from ModelState as it's a navigation property that isn't being sent with the form
            ModelState.Remove("Test");
            
            if (ModelState.IsValid)
            {
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Verify this test belongs to the teacher
                var existingTest = await GetProgrammingTest(question.TestId);
                
                if (existingTest == null || existingTest.TeacherId != teacherId)
                {
                    return NotFound();
                }
                
                // Ensure CodeSnippet is not null
                question.CodeSnippet = question.CodeSnippet ?? string.Empty;
                question.CreatedDate = DateTime.Now;
                
                int questionId = await CreateProgrammingQuestion(question);
                
                if (questionId > 0)
                {
                    TempData["Success"] = "Question added successfully!";
                    return RedirectToAction("Edit", new { id = question.TestId });
                }
                else
                {
                    ModelState.AddModelError("", "Failed to add the question.");
                }
            }
            
            ViewBag.TestId = question.TestId;
            
            // Get test name for display
            var parentTest = await GetProgrammingTest(question.TestId);
            ViewBag.TestName = parentTest?.TestName;
            
            return View(question);
        }
        
        [HttpPost]
        public async Task<IActionResult> DeleteQuestion(int id, int testId)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this test belongs to the teacher
            var parentTest = await GetProgrammingTest(testId);
            
            if (parentTest == null || parentTest.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            bool success = await DeleteProgrammingQuestion(id);
            
            if (success)
            {
                TempData["Success"] = "Question deleted successfully!";
            }
            else
            {
                TempData["Error"] = "Failed to delete the question.";
            }
            
            return RedirectToAction("Edit", new { id = testId });
        }
        
        // Edit Question GET Action
        [HttpGet]
        public async Task<IActionResult> EditQuestion(int id, int testId)
        {
            string teacherId = HttpContext.Session.GetString("TeacherId");
            
            if (string.IsNullOrEmpty(teacherId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Verify this test belongs to the teacher
            var existingTest = await GetProgrammingTest(testId);
            
            if (existingTest == null || existingTest.TeacherId != teacherId)
            {
                return NotFound();
            }
            
            // Get the question
            var question = await GetProgrammingQuestion(id);
            
            if (question == null || question.TestId != testId)
            {
                return NotFound();
            }
            
            ViewBag.TestId = testId;
            ViewBag.TestName = existingTest.TestName;
            
            return View(question);
        }
        
        // Edit Question POST Action
        [HttpPost]
        public async Task<IActionResult> EditQuestion(ProgrammingQuestion question)
        {
            // Remove Test from ModelState as it's a navigation property that isn't being sent with the form
            ModelState.Remove("Test");
            
            if (ModelState.IsValid)
            {
                string teacherId = HttpContext.Session.GetString("TeacherId");
                
                if (string.IsNullOrEmpty(teacherId))
                {
                    return RedirectToAction("Login", "Home");
                }
                
                // Verify this test belongs to the teacher
                var existingTest = await GetProgrammingTest(question.TestId);
                
                if (existingTest == null || existingTest.TeacherId != teacherId)
                {
                    return NotFound();
                }
                
                // Ensure CodeSnippet is not null
                question.CodeSnippet = question.CodeSnippet ?? string.Empty;
                question.LastUpdatedDate = DateTime.Now;
                
                bool success = await UpdateProgrammingQuestion(question);
                
                if (success)
                {
                    TempData["Success"] = "Question updated successfully!";
                    return RedirectToAction("Edit", new { id = question.TestId });
                }
                else
                {
                    ModelState.AddModelError("", "Failed to update the question.");
                }
            }
            
            ViewBag.TestId = question.TestId;
            
            // Get test name for display
            var parentTest = await GetProgrammingTest(question.TestId);
            ViewBag.TestName = parentTest?.TestName;
            
            return View(question);
        }
        
        // Get a programming question by ID
        private async Task<ProgrammingQuestion> GetProgrammingQuestion(int questionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT QuestionId, TestId, QuestionText, AnswerText, 
                           CodeSnippet, Points, CreatedDate, LastUpdatedDate
                    FROM ProgrammingQuestions
                    WHERE QuestionId = @QuestionId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@QuestionId", questionId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new ProgrammingQuestion
                            {
                                QuestionId = reader.GetInt32(0),
                                TestId = reader.GetInt32(1),
                                QuestionText = reader.GetString(2),
                                AnswerText = reader.IsDBNull(3) ? null : reader.GetString(3),
                                CodeSnippet = reader.IsDBNull(4) ? null : reader.GetString(4),
                                Points = reader.GetInt32(5),
                                CreatedDate = reader.GetDateTime(6),
                                LastUpdatedDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7)
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        
        // Update a programming question
        private async Task<bool> UpdateProgrammingQuestion(ProgrammingQuestion question)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    UPDATE ProgrammingQuestions 
                    SET QuestionText = @QuestionText,
                        AnswerText = @AnswerText,
                        CodeSnippet = @CodeSnippet,
                        Points = @Points,
                        LastUpdatedDate = @LastUpdatedDate
                    WHERE QuestionId = @QuestionId AND TestId = @TestId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@QuestionId", question.QuestionId);
                    command.Parameters.AddWithValue("@TestId", question.TestId);
                    command.Parameters.AddWithValue("@QuestionText", question.QuestionText);
                    command.Parameters.AddWithValue("@AnswerText", question.AnswerText ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CodeSnippet", question.CodeSnippet ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Points", question.Points);
                    command.Parameters.AddWithValue("@LastUpdatedDate", question.LastUpdatedDate);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        // Database operations for Programming Tests
        private async Task<List<ProgrammingTest>> GetTeacherProgrammingTests(string teacherId)
        {
            var tests = new List<ProgrammingTest>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT TestId, TeacherId, TestName, ProgrammingLanguage, 
                           Description, YearLevel, CreatedDate, LastUpdatedDate, IsActive
                    FROM ProgrammingTests
                    WHERE TeacherId = @TeacherId
                    ORDER BY CreatedDate DESC";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", teacherId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            tests.Add(new ProgrammingTest
                            {
                                TestId = reader.GetInt32(0),
                                TeacherId = reader.GetString(1),
                                TestName = reader.GetString(2),
                                ProgrammingLanguage = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                YearLevel = reader.GetInt32(5),
                                CreatedDate = reader.GetDateTime(6),
                                LastUpdatedDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                IsActive = reader.GetBoolean(8)
                            });
                        }
                    }
                }
                
                // Get question counts for each test
                foreach (var currentTest in tests)
                {
                    string countSql = @"
                        SELECT COUNT(*)
                        FROM ProgrammingQuestions
                        WHERE TestId = @TestId";
                        
                    using (var command = new SqlCommand(countSql, connection))
                    {
                        command.Parameters.AddWithValue("@TestId", currentTest.TestId);
                        currentTest.Questions = new List<ProgrammingQuestion>();
                        int count = (int)await command.ExecuteScalarAsync();
                        
                        // Create empty placeholder questions just to get the count right
                        for (int i = 0; i < count; i++)
                        {
                            currentTest.Questions.Add(new ProgrammingQuestion());
                        }
                    }
                }
            }
            
            return tests;
        }
        
        private async Task<ProgrammingTest> GetProgrammingTest(int testId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT TestId, TeacherId, TestName, ProgrammingLanguage, 
                           Description, YearLevel, CreatedDate, LastUpdatedDate, IsActive
                    FROM ProgrammingTests
                    WHERE TestId = @TestId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", testId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new ProgrammingTest
                            {
                                TestId = reader.GetInt32(0),
                                TeacherId = reader.GetString(1),
                                TestName = reader.GetString(2),
                                ProgrammingLanguage = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                YearLevel = reader.GetInt32(5),
                                CreatedDate = reader.GetDateTime(6),
                                LastUpdatedDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                IsActive = reader.GetBoolean(8)
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        
        private async Task<int> CreateProgrammingTest(ProgrammingTest programTest)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    INSERT INTO ProgrammingTests (TeacherId, TestName, ProgrammingLanguage, 
                                                Description, YearLevel, CreatedDate, IsActive)
                    VALUES (@TeacherId, @TestName, @ProgrammingLanguage, 
                            @Description, @YearLevel, @CreatedDate, @IsActive);
                    
                    SELECT SCOPE_IDENTITY();";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TeacherId", programTest.TeacherId);
                    command.Parameters.AddWithValue("@TestName", programTest.TestName);
                    command.Parameters.AddWithValue("@ProgrammingLanguage", programTest.ProgrammingLanguage);
                    command.Parameters.AddWithValue("@Description", programTest.Description ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@YearLevel", programTest.YearLevel);
                    command.Parameters.AddWithValue("@CreatedDate", programTest.CreatedDate);
                    command.Parameters.AddWithValue("@IsActive", programTest.IsActive);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                }
            }
            
            return 0;
        }
        
        private async Task<bool> UpdateProgrammingTest(ProgrammingTest programTest)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    UPDATE ProgrammingTests 
                    SET TestName = @TestName,
                        ProgrammingLanguage = @ProgrammingLanguage,
                        Description = @Description,
                        YearLevel = @YearLevel,
                        LastUpdatedDate = @LastUpdatedDate,
                        IsActive = @IsActive
                    WHERE TestId = @TestId AND TeacherId = @TeacherId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", programTest.TestId);
                    command.Parameters.AddWithValue("@TeacherId", programTest.TeacherId);
                    command.Parameters.AddWithValue("@TestName", programTest.TestName);
                    command.Parameters.AddWithValue("@ProgrammingLanguage", programTest.ProgrammingLanguage);
                    command.Parameters.AddWithValue("@Description", programTest.Description ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@YearLevel", programTest.YearLevel);
                    command.Parameters.AddWithValue("@LastUpdatedDate", programTest.LastUpdatedDate);
                    command.Parameters.AddWithValue("@IsActive", programTest.IsActive);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        private async Task<(bool Success, string ErrorMessage)> DeleteProgrammingTest(int testId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                try
                {
                    // First, check if there are any certificates for this test and delete them
                    await DeleteRelatedCertificates(testId);
                    
                    // Now delete the test itself
                    string sql = @"DELETE FROM ProgrammingTests WHERE TestId = @TestId";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@TestId", testId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        return (rowsAffected > 0, null);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error
                    _logger.LogError(ex, "Error deleting test");
                    return (false, ex.Message);
                }
            }
        }
        
        // Helper method to delete related certificates
        private async Task DeleteRelatedCertificates(int testId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the Certificates table exists
                    bool certificatesTableExists = false;
                    string checkTableSql = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'Certificates'";
                    
                    using (var checkCommand = new SqlCommand(checkTableSql, connection))
                    {
                        int tableCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        certificatesTableExists = (tableCount > 0);
                    }
                    
                    if (certificatesTableExists)
                    {
                        // Delete certificates related to this test
                        string deleteCertsSql = @"DELETE FROM Certificates WHERE TestId = @TestId";
                        using (var command = new SqlCommand(deleteCertsSql, connection))
                        {
                            command.Parameters.AddWithValue("@TestId", testId);
                            int certsDeleted = await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Deleted {certsDeleted} certificates for test ID {testId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting certificates for test ID {testId}");
                throw; // Re-throw to be handled by the calling method
            }
        }
        
        // Database operations for Programming Questions
        private async Task<List<ProgrammingQuestion>> GetProgrammingQuestions(int testId)
        {
            var questions = new List<ProgrammingQuestion>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT QuestionId, TestId, QuestionText, AnswerText, 
                           CodeSnippet, Points, CreatedDate, LastUpdatedDate
                    FROM ProgrammingQuestions
                    WHERE TestId = @TestId
                    ORDER BY QuestionId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", testId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            questions.Add(new ProgrammingQuestion
                            {
                                QuestionId = reader.GetInt32(0),
                                TestId = reader.GetInt32(1),
                                QuestionText = reader.GetString(2),
                                AnswerText = reader.IsDBNull(3) ? null : reader.GetString(3),
                                CodeSnippet = reader.IsDBNull(4) ? null : reader.GetString(4),
                                Points = reader.GetInt32(5),
                                CreatedDate = reader.GetDateTime(6),
                                LastUpdatedDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7)
                            });
                        }
                    }
                }
            }
            
            return questions;
        }
        
        private async Task<int> CreateProgrammingQuestion(ProgrammingQuestion question)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    INSERT INTO ProgrammingQuestions (TestId, QuestionText, AnswerText, 
                                                     CodeSnippet, Points, CreatedDate)
                    VALUES (@TestId, @QuestionText, @AnswerText, 
                            @CodeSnippet, @Points, @CreatedDate);
                    
                    SELECT SCOPE_IDENTITY();";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", question.TestId);
                    command.Parameters.AddWithValue("@QuestionText", question.QuestionText);
                    command.Parameters.AddWithValue("@AnswerText", question.AnswerText ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@CodeSnippet", question.CodeSnippet ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Points", question.Points);
                    command.Parameters.AddWithValue("@CreatedDate", question.CreatedDate);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                }
            }
            
            return 0;
        }
        
        private async Task<bool> DeleteProgrammingQuestion(int questionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"DELETE FROM ProgrammingQuestions WHERE QuestionId = @QuestionId";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@QuestionId", questionId);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }
        
        // Student methods to view and take programming tests
        public async Task<IActionResult> StudentTests()
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }

            // Get the student's year level 
            int studentYearLevel = await GetStudentYearLevel(studentId);
            
            // Get the list of tests this student has already completed with scores
            var completedTestScores = await GetCompletedTestScores(studentId);
            var completedTestIds = completedTestScores.Keys.ToList();
            
            // Get all completed tests directly from the database
            var completedTests = await GetCompletedTests(studentId);
            
            ViewBag.StudentYearLevel = studentYearLevel;
            ViewBag.StudentId = studentId;
            ViewBag.CompletedTestIds = completedTestIds;
            ViewBag.CompletedTestScores = completedTestScores;
            
            return View(completedTests);
        }
        
        // Get all tests completed by a student, regardless of year level
        private async Task<List<ProgrammingTest>> GetCompletedTests(string studentId)
        {
            var tests = new List<ProgrammingTest>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT pt.TestId, pt.TeacherId, pt.TestName, pt.ProgrammingLanguage, 
                           pt.Description, pt.YearLevel, pt.CreatedDate, pt.LastUpdatedDate, pt.IsActive
                    FROM ProgrammingTests pt
                    INNER JOIN TestSubmissions ts ON pt.TestId = ts.TestId
                    WHERE ts.StudentId = @StudentId
                    ORDER BY ts.SubmissionDate DESC";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            tests.Add(new ProgrammingTest
                            {
                                TestId = reader.GetInt32(0),
                                TeacherId = reader.GetString(1),
                                TestName = reader.GetString(2),
                                ProgrammingLanguage = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                YearLevel = reader.GetInt32(5),
                                CreatedDate = reader.GetDateTime(6),
                                LastUpdatedDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                IsActive = reader.GetBoolean(8)
                            });
                        }
                    }
                }
                
                // Get question counts for each test
                foreach (var currentTest in tests)
                {
                    string countSql = @"
                        SELECT COUNT(*)
                        FROM ProgrammingQuestions
                        WHERE TestId = @TestId";
                        
                    using (var command = new SqlCommand(countSql, connection))
                    {
                        command.Parameters.AddWithValue("@TestId", currentTest.TestId);
                        currentTest.Questions = new List<ProgrammingQuestion>();
                        int count = (int)await command.ExecuteScalarAsync();
                        
                        // Create empty placeholder questions just to get the count right
                        for (int i = 0; i < count; i++)
                        {
                            currentTest.Questions.Add(new ProgrammingQuestion());
                        }
                    }
                }
            }
            
            return tests;
        }
        
        [HttpGet]
        public async Task<IActionResult> TakeTest(int id)
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Check if the student has already completed this test
            bool hasCompleted = await HasCompletedTest(id, studentId);
            if (hasCompleted)
            {
                TempData["Error"] = "You have already completed this test.";
                return RedirectToAction("StudentTests");
            }
            
            // Verify this test exists
            var test = await GetProgrammingTest(id);
            
            if (test == null || !test.IsActive)
            {
                return NotFound();
            }
            
            // Get student's year level
            int studentYearLevel = await GetStudentYearLevel(studentId);
            
            // Check if student is a transferee
            bool isTransferee = await IsTransfereeStudent(studentId);
            
            // Remove year level restriction - allow students to take any level test
            // (previously restricted tests above student's year level)
            
            // Load questions for this test
            test.Questions = await GetProgrammingQuestions(id);
            
            ViewBag.StudentId = studentId;
            
            return View(test);
        }
        
        // Check if a student has already completed a test
        private async Task<bool> HasCompletedTest(int testId, string studentId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"SELECT COUNT(1) FROM TestSubmissions 
                              WHERE TestId = @TestId AND StudentId = @StudentId";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", testId);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    return count > 0;
                }
            }
        }
        
        [HttpPost]
        public async Task<IActionResult> SubmitTest(SubmitTestViewModel model)
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "You must be logged in to submit a test." });
            }
            
            // Ensure the student ID in the model matches the session
            if (model.StudentId != studentId)
            {
                return Json(new { success = false, message = "Invalid student ID." });
            }
            
            try
            {
                // Save test answers
                bool success = await SaveTestAnswers(model);
                
                if (success)
                {
                    int certificateId = 0;
                    string errorMessage = null;
                    
                    try
                    {
                        // Get test details for certificate generation
                        var test = await GetProgrammingTest(model.TestId);
                        
                        if (test != null)
                        {
                            // Get student name
                            string studentName = await GetStudentName(studentId);
                            
                            // Calculate test score
                            int score = await CalculateTestScore(model.TestId, studentId);
                            
                            // Get student grade level
                            int gradeLevel = await GetStudentYearLevel(studentId);
                            
                            _logger.LogInformation($"Generating certificate for student {studentId}, test {model.TestId}, score {score}");
                            
                            // Generate certificate
                            certificateId = await _certificateService.GenerateAndSaveCertificate(
                                studentId,
                                studentName,
                                model.TestId,
                                test.TestName,
                                test.ProgrammingLanguage,
                                gradeLevel,
                                score
                            );
                            
                            _logger.LogInformation($"Certificate generated with ID: {certificateId}");
                            
                            if (certificateId <= 0)
                            {
                                errorMessage = "Certificate was not created successfully";
                            }
                            else
                            {
                                // Update student's achievements with the completed test
                                await UpdateStudentAchievements(studentId, test.TestName);
                                
                                // Update the language-specific score
                                try
                                {
                                    await UpdateLanguageScore(studentId, test.ProgrammingLanguage);
                                    _logger.LogInformation($"Language score updated for student {studentId} after {test.ProgrammingLanguage} test completion");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error updating language score for student {studentId}: {ex.Message}");
                                }
                                
                                // Update the badge color automatically
                                try
                                {
                                    await _badgeService.UpdateBadgeColor(studentId);
                                    _logger.LogInformation($"BadgeColor updated for student {studentId} after test completion");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error updating badge color for student {studentId}");
                                }
                            }
                        }
                        else
                        {
                            // Test details not found
                            errorMessage = "Test submitted successfully, but test details not found for certificate generation.";
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the certificate generation error but still return success for the test submission
                        _logger.LogError(ex, $"Error generating certificate: {ex.Message}");
                        errorMessage = $"Test submitted successfully, but certificate generation failed: {ex.Message}";
                    }
                    
                    // Return appropriate response based on certificate generation success
                    if (certificateId > 0)
                    {
                        return Json(new { success = true, certificateId = certificateId });
                    }
                    else
                    {
                        return Json(new { success = true, message = errorMessage });
                    }
                }
                else
                {
                    return Json(new { success = false, message = "Failed to save test answers. You may have already submitted this test." });
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                _logger.LogError(ex, $"Error submitting test: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
        
        // Save student test answers to the database
        private async Task<bool> SaveTestAnswers(SubmitTestViewModel model)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if student has already submitted this test
                    var existingSubmission = await CheckTestSubmission(model.TestId, model.StudentId);
                    if (existingSubmission)
                    {
                        return false; // Student already submitted this test
                    }
                    
                    // Insert test submission record
                    DateTime submissionDate = DateTime.Now;
                    int submissionId = await CreateTestSubmission(connection, model.TestId, model.StudentId, submissionDate);
                    
                    if (submissionId <= 0)
                    {
                        return false;
                    }
                    
                    // Validate model.Answers is not null
                    if (model.Answers == null || !model.Answers.Any())
                    {
                        return false;
                    }
                    
                    // Insert answer records
                    foreach (var answer in model.Answers)
                    {
                        bool answerSaved = await SaveTestAnswer(connection, submissionId, answer.QuestionId, answer.Answer);
                        if (!answerSaved)
                        {
                            return false;
                        }
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Re-throw the exception to be handled by the calling method
                throw new Exception($"Error saving test answers: {ex.Message}", ex);
            }
        }
        
        private async Task<bool> CheckTestSubmission(int testId, string studentId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"SELECT COUNT(1) FROM TestSubmissions 
                              WHERE TestId = @TestId AND StudentId = @StudentId";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", testId);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    return count > 0;
                }
            }
        }
        
        private async Task<int> CreateTestSubmission(SqlConnection connection, int testId, string studentId, DateTime submissionDate)
        {
            string sql = @"INSERT INTO TestSubmissions (TestId, StudentId, SubmissionDate, IsGraded)
                          VALUES (@TestId, @StudentId, @SubmissionDate, 1);
                          SELECT SCOPE_IDENTITY();";
            
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TestId", testId);
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@SubmissionDate", submissionDate);
                
                var result = await command.ExecuteScalarAsync();
                
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
                
                return 0;
            }
        }
        
        private async Task<bool> SaveTestAnswer(SqlConnection connection, int submissionId, int questionId, string answer)
        {
            // Get the correct answer and possible points for this question
            string questionSql = @"SELECT AnswerText, Points FROM ProgrammingQuestions WHERE QuestionId = @QuestionId";
            string correctAnswer = "";
            int possiblePoints = 0;
            
            using (var command = new SqlCommand(questionSql, connection))
            {
                command.Parameters.AddWithValue("@QuestionId", questionId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        correctAnswer = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        possiblePoints = reader.GetInt32(1);
                    }
                }
            }
            
            // Simple grading logic: award points if student answer matches expected answer
            // In a real application, you might want more sophisticated grading logic
            int earnedPoints = 0;
            if (!string.IsNullOrWhiteSpace(answer) && 
                !string.IsNullOrWhiteSpace(correctAnswer) && 
                answer.Trim().Equals(correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                earnedPoints = possiblePoints;
            }
            
            // Get the StudentId from the TestSubmissions table
            string getStudentIdSql = "SELECT StudentId FROM TestSubmissions WHERE SubmissionId = @SubmissionId";
            string studentId = "";
            
            using (var studentIdCmd = new SqlCommand(getStudentIdSql, connection))
            {
                studentIdCmd.Parameters.AddWithValue("@SubmissionId", submissionId);
                var studentIdResult = await studentIdCmd.ExecuteScalarAsync();
                if (studentIdResult != null && studentIdResult != DBNull.Value)
                {
                    studentId = studentIdResult.ToString();
                }
            }
            
            // Save the answer with earned points, including StudentId and TotalPoints
            string sql = @"INSERT INTO TestAnswers (SubmissionId, QuestionId, AnswerText, Points, StudentId, TotalPoints)
                          VALUES (@SubmissionId, @QuestionId, @AnswerText, @Points, @StudentId, @TotalPoints);";
            
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@SubmissionId", submissionId);
                command.Parameters.AddWithValue("@QuestionId", questionId);
                command.Parameters.AddWithValue("@AnswerText", answer ?? string.Empty);
                command.Parameters.AddWithValue("@Points", earnedPoints);
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@TotalPoints", possiblePoints);
                
                int rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
        }
        
        // Get a student's year level from the database
        private async Task<int> GetStudentYearLevel(string studentId)
        {
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
                        return Convert.ToInt32(result);
                    }
                }
            }
            
            // Default to 1st year if not found
            return 1;
        }
        
        // Get tests appropriate for a specific year level
        private async Task<List<ProgrammingTest>> GetTestsByYearLevel(int yearLevel)
        {
            var tests = new List<ProgrammingTest>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT TestId, TeacherId, TestName, ProgrammingLanguage, 
                           Description, YearLevel, CreatedDate, LastUpdatedDate, IsActive
                    FROM ProgrammingTests
                    WHERE YearLevel = @YearLevel AND IsActive = 1
                    ORDER BY CreatedDate DESC";
                    
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@YearLevel", yearLevel);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            tests.Add(new ProgrammingTest
                            {
                                TestId = reader.GetInt32(0),
                                TeacherId = reader.GetString(1),
                                TestName = reader.GetString(2),
                                ProgrammingLanguage = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                YearLevel = reader.GetInt32(5),
                                CreatedDate = reader.GetDateTime(6),
                                LastUpdatedDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                                IsActive = reader.GetBoolean(8)
                            });
                        }
                    }
                }
                
                // Get question counts for each test
                foreach (var currentTest in tests)
                {
                    string countSql = @"
                        SELECT COUNT(*)
                        FROM ProgrammingQuestions
                        WHERE TestId = @TestId";
                        
                    using (var command = new SqlCommand(countSql, connection))
                    {
                        command.Parameters.AddWithValue("@TestId", currentTest.TestId);
                        currentTest.Questions = new List<ProgrammingQuestion>();
                        int count = (int)await command.ExecuteScalarAsync();
                        
                        // Create empty placeholder questions just to get the count right
                        for (int i = 0; i < count; i++)
                        {
                            currentTest.Questions.Add(new ProgrammingQuestion());
                        }
                    }
                }
            }
            
            return tests;
        }
        
        // Get the list of test IDs that the student has already completed
        private async Task<List<int>> GetCompletedTestIds(string studentId)
        {
            var completedTestIds = new List<int>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"SELECT TestId FROM TestSubmissions 
                              WHERE StudentId = @StudentId";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            completedTestIds.Add(reader.GetInt32(0));
                        }
                    }
                }
            }
            
            return completedTestIds;
        }
        
        // Get the list of completed tests with scores for a student
        private async Task<Dictionary<int, TestScore>> GetCompletedTestScores(string studentId)
        {
            var completedTests = new Dictionary<int, TestScore>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Get test submissions
                string sql = @"SELECT ts.SubmissionId, ts.TestId 
                              FROM TestSubmissions ts
                              WHERE ts.StudentId = @StudentId";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int submissionId = reader.GetInt32(0);
                            int testId = reader.GetInt32(1);
                            
                            completedTests.Add(testId, new TestScore 
                            { 
                                TestId = testId,
                                SubmissionId = submissionId,
                                EarnedPoints = 0,
                                TotalPoints = 0
                            });
                        }
                    }
                }
                
                // Get scores for each test submission
                foreach (var testScore in completedTests.Values)
                {
                    // Get earned points (sum of points from answers)
                    string pointsSql = @"SELECT COALESCE(SUM(Points), 0) 
                                        FROM TestAnswers 
                                        WHERE SubmissionId = @SubmissionId";
                    
                    using (var command = new SqlCommand(pointsSql, connection))
                    {
                        command.Parameters.AddWithValue("@SubmissionId", testScore.SubmissionId);
                        var earnedPoints = await command.ExecuteScalarAsync();
                        testScore.EarnedPoints = Convert.ToInt32(earnedPoints);
                    }
                    
                    // Get total possible points for this test
                    string totalSql = @"SELECT COALESCE(SUM(Points), 0) 
                                       FROM ProgrammingQuestions 
                                       WHERE TestId = @TestId";
                    
                    using (var command = new SqlCommand(totalSql, connection))
                    {
                        command.Parameters.AddWithValue("@TestId", testScore.TestId);
                        var totalPoints = await command.ExecuteScalarAsync();
                        testScore.TotalPoints = Convert.ToInt32(totalPoints);
                    }
                }
            }
            
            return completedTests;
        }

        // Show tests available for a student to take
        public async Task<IActionResult> AvailableTests()
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }

            // Get the student's year level 
            int studentYearLevel = await GetStudentYearLevel(studentId);
            
            // Check if the student is a graduate - redirect them to dashboard 
            if (studentYearLevel == 5)
            {
                TempData["Warning"] = "Graduates cannot access available tests.";
                return RedirectToAction("StudentDashboard", "Dashboard");
            }
            
            // Check if the student is a transferee
            bool isTransferee = await IsTransfereeStudent(studentId);

            // Get all available programming languages
            var languages = await GetAvailableProgrammingLanguages(studentYearLevel);
            
            // For each language, count how many mastery tests the student has taken
            var languageStatistics = new Dictionary<string, int>();
            foreach (var language in languages)
            {
                int testsTaken = await CountMasteryTestsTakenByLanguage(studentId, language);
                languageStatistics[language] = testsTaken;
            }
            
            ViewBag.StudentYearLevel = studentYearLevel;
            ViewBag.StudentId = studentId;
            ViewBag.IsTransferee = isTransferee;
            ViewBag.LanguageStatistics = languageStatistics;
            
            return View(languages);
        }

        // New action method to start a mastery take for a specific language
        [HttpGet]
        public async Task<IActionResult> StartMasteryTake(string language)
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }

            // Get the student's year level 
            int studentYearLevel = await GetStudentYearLevel(studentId);
            
            // Get the list of tests this student has already completed
            var completedTestScores = await GetCompletedTestScores(studentId);
            var completedTestIds = completedTestScores.Keys.ToList();
            
            // Dictionary to store tests organized by year level
            Dictionary<int, List<ProgrammingTest>> testsByYearLevel = new Dictionary<int, List<ProgrammingTest>>();
            
            // Show tests for ALL year levels (1-4) regardless of student's current year level
            for (int yearLevel = 1; yearLevel <= 4; yearLevel++)
            {
                var yearTests = await GetTestsByYearLevel(yearLevel);
                var filteredTests = yearTests.Where(t => t.ProgrammingLanguage.Equals(language, StringComparison.OrdinalIgnoreCase)).ToList();
                
                // Only add year level if it has tests for this language
                if (filteredTests.Any())
                {
                    testsByYearLevel[yearLevel] = filteredTests;
                }
            }
            
            // Filter all tests to remove completed ones
            Dictionary<int, List<ProgrammingTest>> availableTestsByYearLevel = new Dictionary<int, List<ProgrammingTest>>();
            
            foreach (var kvp in testsByYearLevel)
            {
                int yearLevel = kvp.Key;
                var availableTests = kvp.Value.Where(t => !completedTestIds.Contains(t.TestId)).ToList();
                
                // Only add year level if it has available tests
                if (availableTests.Any())
                {
                    availableTestsByYearLevel[yearLevel] = availableTests;
                }
            }
            
            ViewBag.StudentYearLevel = studentYearLevel;
            ViewBag.StudentId = studentId;
            ViewBag.ProgrammingLanguage = language;
            ViewBag.TestsByYearLevel = availableTestsByYearLevel;
            
            // Count tests taken for this language
            int testsTaken = await CountMasteryTestsTakenByLanguage(studentId, language);
            ViewBag.TestsTaken = testsTaken;
            
            // Flatten the tests for the model
            List<ProgrammingTest> allAvailableTests = availableTestsByYearLevel.Values.SelectMany(v => v).ToList();
            
            return View("LanguageTests", allAvailableTests);
        }

        // Get student name by ID
        private async Task<string> GetStudentName(string studentId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Join StudentDetails with Users table to get the student's full name
                string sql = @"
                    SELECT u.FullName 
                    FROM StudentDetails sd
                    JOIN Users u ON sd.UserId = u.UserId
                    WHERE sd.IdNumber = @StudentId";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString();
                    }
                    
                    return "Student";
                }
            }
        }
        
        // Calculate test score
        private async Task<int> CalculateTestScore(int testId, string studentId)
        {
            Console.WriteLine($"Calculating test score for student {studentId}, test {testId}");
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Get the most recent submission for this test
                string submissionSql = @"
                    SELECT TOP 1 SubmissionId 
                    FROM TestSubmissions 
                    WHERE TestId = @TestId AND StudentId = @StudentId
                    ORDER BY SubmissionDate DESC";
                
                int submissionId = 0;
                
                using (var command = new SqlCommand(submissionSql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", testId);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        submissionId = Convert.ToInt32(result);
                        Console.WriteLine($"Found submissionId: {submissionId}");
                    }
                    else
                    {
                        Console.WriteLine("No submission found for this test and student");
                    }
                }
                
                if (submissionId <= 0)
                {
                    return 0;
                }
                
                // Directly query total points earned for this submission
                string pointsSql = @"SELECT COALESCE(SUM(Points), 0) FROM TestAnswers WHERE SubmissionId = @SubmissionId";
                int earnedPoints = 0;
                
                using (var command = new SqlCommand(pointsSql, connection))
                {
                    command.Parameters.AddWithValue("@SubmissionId", submissionId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        earnedPoints = Convert.ToInt32(result);
                        Console.WriteLine($"Earned points: {earnedPoints}");
                    }
                }
                
                // Directly query total possible points for this test
                string totalSql = @"SELECT COALESCE(SUM(Points), 0) FROM ProgrammingQuestions WHERE TestId = @TestId";
                int totalPoints = 0;
                
                using (var command = new SqlCommand(totalSql, connection))
                {
                    command.Parameters.AddWithValue("@TestId", testId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        totalPoints = Convert.ToInt32(result);
                        Console.WriteLine($"Total possible points: {totalPoints}");
                    }
                }
                
                if (totalPoints > 0)
                {
                    int score = (earnedPoints * 100) / totalPoints;
                    Console.WriteLine($"Calculated score: {score}%");
                    return score;
                }
                
                return 0;
            }
        }

        // New endpoint to view certificates
        [HttpGet]
        public async Task<IActionResult> Certificates()
        {
            // Get current student information from session
            string studentId = HttpContext.Session.GetString("IdNumber");
            
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }
            
            try
            {
                // Get the student's name for the view
                string studentName = await GetStudentName(studentId);
                
                // Get student certificates
                var certificates = await _certificateService.GetStudentCertificates(studentId);
                
                // Get the student's year level
                int studentYearLevel = await GetStudentYearLevel(studentId);
                
                // Add data to ViewBag
                ViewBag.StudentId = studentId;
                ViewBag.FullName = studentName;
                ViewBag.StudentYearLevel = studentYearLevel;
                
                return View(certificates);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving certificates: {ex.Message}");
                ViewBag.ErrorMessage = "Could not retrieve certificates due to an error.";
                return View(Array.Empty<Certificate>());
            }
        }
        
        // New endpoint to view a specific certificate
        [HttpGet]
        public async Task<IActionResult> ViewCertificate(int id)
        {
            // Get current user info from session
            string role = HttpContext.Session.GetString("Role");
            string studentId = HttpContext.Session.GetString("IdNumber");
            string employerId = HttpContext.Session.GetString("EmployerId");
            string userId = HttpContext.Session.GetString("UserId");
            
            // Log authorization info for debugging
            _logger.LogInformation($"ViewCertificate called for certificate ID: {id}");
            _logger.LogInformation($"User Role: {role}, StudentId: {studentId}, EmployerId: {employerId}, UserId: {userId}");
            
            // Allow students, employers, and teachers to view certificates
            bool isAuthorizedUser = !string.IsNullOrEmpty(role) && 
                (role == "student" || role == "employer" || role == "teacher" || role == "admin");
            
            if (!isAuthorizedUser)
            {
                _logger.LogWarning($"Unauthorized access attempt to certificate {id}");
                return RedirectToAction("Login", "Home");
            }
            
            try
            {
                // Get certificate
                var certificate = await _certificateService.GetCertificateById(id);
                
                if (certificate == null)
                {
                    _logger.LogWarning($"Certificate with ID {id} not found");
                    return NotFound();
                }
                
                // Only check student permission if it's a student viewing (not an employer or teacher)
                if (role == "student" && !string.IsNullOrEmpty(studentId) && certificate.StudentId != studentId)
                {
                    _logger.LogWarning($"Student {studentId} attempted to access certificate {id} belonging to {certificate.StudentId}");
                    return NotFound();
                }
                
                // Check if we have image data
                if (certificate.CertificateData != null && certificate.CertificateData.Length > 0)
                {
                    _logger.LogInformation($"Returning certificate {id} as image");
                    // Return the certificate as an image
                    return File(certificate.CertificateData, certificate.CertificateContentType ?? "image/png");
                }
                else if (!string.IsNullOrEmpty(certificate.CertificateContent))
                {
                    _logger.LogInformation($"Returning certificate {id} as HTML");
                    // Fall back to HTML content if available
                    return Content(certificate.CertificateContent, "text/html");
                }
                else
                {
                    _logger.LogWarning($"Certificate {id} has no content");
                    // No certificate content available
                    return Content("<html><body><h1>Certificate Not Found</h1><p>The certificate could not be displayed.</p></body></html>", "text/html");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving certificate {id}");
                return Content($"<html><body><h1>Error</h1><p>An error occurred: {ex.Message}</p></body></html>", "text/html");
            }
        }

        // New API endpoint for getting student certificates
        [HttpGet]
        public async Task<IActionResult> GetStudentCertificates(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                _logger.LogWarning("GetStudentCertificates called without studentId");
                return BadRequest("Student ID is required");
            }
            
            string role = HttpContext.Session.GetString("Role");
            string userId = HttpContext.Session.GetString("UserId");
            string currentStudentId = HttpContext.Session.GetString("IdNumber");
            string employerId = HttpContext.Session.GetString("EmployerId");
            
            _logger.LogInformation($"GetStudentCertificates called for studentId: {studentId}");
            _logger.LogInformation($"User Role: {role}, UserId: {userId}, CurrentStudentId: {currentStudentId}, EmployerId: {employerId}");
            
            // Check if user is authorized to view these certificates
            bool isAuthorized = !string.IsNullOrEmpty(role) && (
                role == "admin" || 
                role == "teacher" || 
                role == "employer" || 
                (role == "student" && studentId == currentStudentId)
            );
            
            if (!isAuthorized)
            {
                _logger.LogWarning($"Unauthorized access attempt to certificates for student {studentId}");
                return Unauthorized("You are not authorized to view these certificates");
            }
            
            try
            {
                // Get student certificates
                var certificates = await _certificateService.GetStudentCertificates(studentId);
                _logger.LogInformation($"Found {certificates.Length} certificates for student {studentId}");
                
                // Return as JSON with camelCase property names for JavaScript
                return Json(certificates, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving certificates for student {studentId}");
                return StatusCode(500, "An error occurred while retrieving certificates");
            }
        }

        // Add new method to update student achievements
        private async Task<bool> UpdateStudentAchievements(string studentId, string testName)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First get existing achievements
                    string getAchievementsSql = @"
                        SELECT Achievements FROM StudentDetails
                        WHERE IdNumber = @StudentId";
                    
                    string currentAchievements = string.Empty;
                    
                    using (var command = new SqlCommand(getAchievementsSql, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            currentAchievements = result.ToString();
                        }
                    }
                    
                    // Check if this test is already in achievements
                    string testAchievement = $"Test: {testName}";
                    string[] achievementEntries = currentAchievements.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    
                    bool achievementsUpdated = false;
                    string newAchievements = currentAchievements;
                    
                    // If test is not already in achievements, add it
                    if (!achievementEntries.Any(a => a.Trim().Equals(testAchievement, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Add the new achievement
                        newAchievements = string.IsNullOrEmpty(currentAchievements) 
                            ? testAchievement 
                            : $"{currentAchievements}|{testAchievement}";
                        
                        achievementsUpdated = true;
                    }
                    
                    // Calculate new mastery score
                    // Get total test scores and items from all year levels 1-4
                    string testScoresQuery = @"
                        SELECT 
                            COALESCE(SUM(ta.Points), 0) AS TotalScore
                        FROM TestSubmissions ts
                        JOIN TestAnswers ta ON ts.SubmissionId = ta.SubmissionId
                        JOIN ProgrammingTests pt ON ts.TestId = pt.TestId
                        WHERE ts.StudentId = @StudentId
                        AND pt.YearLevel BETWEEN 1 AND 4";
                    
                    decimal totalTestScore = 0;
                    
                    using (var command = new SqlCommand(testScoresQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            totalTestScore = Convert.ToDecimal(result);
                        }
                    }

                    // Add debugging output
                    Console.WriteLine($"Test scores query result: {totalTestScore} for student {studentId}");
                    
                    // Now get total possible points from ALL tests across all year levels
                    string totalTestItemsQuery = @"
                        SELECT COALESCE(SUM(pq.Points), 0) AS TotalItems
                        FROM ProgrammingTests pt
                        JOIN ProgrammingQuestions pq ON pt.TestId = pq.TestId
                        WHERE pt.YearLevel BETWEEN 1 AND 4
                        AND pt.IsActive = 1";
                    
                    decimal totalTestItems = 0;
                    
                    using (var command = new SqlCommand(totalTestItemsQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            totalTestItems = Convert.ToDecimal(result);
                            Console.WriteLine($"Total possible points from ALL tests: {totalTestItems}");
                        }
                    }
                    
                    // Get total possible points from tests this student has actually submitted
                    // This is a more realistic measure than all tests in the system
                    string studentTestItemsQuery = @"
                        SELECT COALESCE(SUM(pq.Points), 0) AS StudentTotalItems
                        FROM ProgrammingTests pt
                        JOIN ProgrammingQuestions pq ON pt.TestId = pq.TestId
                        JOIN TestSubmissions ts ON pt.TestId = ts.TestId
                        WHERE ts.StudentId = @StudentId";
                        
                    decimal studentTestItems = 0;
                    
                    using (var command = new SqlCommand(studentTestItemsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            studentTestItems = Convert.ToDecimal(result);
                        }
                    }
                    
                    // We will NOT override with student-specific items
                    // Per user's requirement, we must use ALL tests from 1st to 4th year
                    Console.WriteLine($"Using total items from ALL tests: {totalTestItems} per requirement");
                    
                    // Calculate mastery score
                    decimal masteryScore = 0;
                    
                    // Debug info
                    Console.WriteLine($"Student ID: {studentId}");
                    Console.WriteLine($"Total Test Score: {totalTestScore}");
                    Console.WriteLine($"Total Test Items: {totalTestItems}");
                    
                    // Make sure we have some test items
                    if (totalTestItems <= 0)
                    {
                        masteryScore = 0;
                        Console.WriteLine("Total test items is zero, setting MasteryScore to 0");
                    }
                    else
                    {
                        // Get the current mastery score weight from the ScoreWeights table
                        decimal masteryWeight = 20.0m; // Default to 20% if not found
                        string weightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'Mastery'";
                        using (var weightCmd = new SqlCommand(weightQuery, connection))
                        {
                            var weightResult = await weightCmd.ExecuteScalarAsync();
                            if (weightResult != null && weightResult != DBNull.Value)
                            {
                                masteryWeight = Convert.ToDecimal(weightResult);
                                Console.WriteLine($"Retrieved current Mastery weight from database: {masteryWeight}%");
                            }
                            else
                            {
                                Console.WriteLine("Using default Mastery weight of 20%");
                            }
                        }

                        // Get the programming language for this test
                        string programmingLanguage = "";
                        string languageQuery = @"
                            SELECT ProgrammingLanguage
                            FROM ProgrammingTests
                            WHERE TestName = @TestName";
                        using (var langCmd = new SqlCommand(languageQuery, connection))
                        {
                            langCmd.Parameters.AddWithValue("@TestName", testName);
                            var langResult = await langCmd.ExecuteScalarAsync();
                            if (langResult != null && langResult != DBNull.Value)
                            {
                                programmingLanguage = langResult.ToString();
                                Console.WriteLine($"Test programming language: {programmingLanguage}");
                            }
                        }

                        // Count how many mastery tests the student has taken for this language
                        int testsTaken = await CountMasteryTestsTakenByLanguage(studentId, programmingLanguage);
                        Console.WriteLine($"Student has taken {testsTaken} tests for {programmingLanguage}");

                        // Calculate the multiplier based on number of tests taken
                        decimal multiplier = 0.25m; // Default for 1 test
                        
                        if (testsTaken >= 4)
                        {
                            multiplier = 1.00m;
                            Console.WriteLine("Using multiplier: 1.00 (4+ tests taken)");
                        }
                        else if (testsTaken == 3)
                        {
                            multiplier = 0.75m;
                            Console.WriteLine("Using multiplier: 0.75 (3 tests taken)");
                        }
                        else if (testsTaken == 2)
                        {
                            multiplier = 0.50m;
                            Console.WriteLine("Using multiplier: 0.50 (2 tests taken)");
                        }
                        else
                        {
                            Console.WriteLine("Using multiplier: 0.25 (1 test taken)");
                        }
                        
                        // We no longer calculate mastery score directly here
                        // Instead, we need to query all language scores and calculate their average
                        // Get all 8 language scores for this student
                        string languageScoresQuery = @"
                            SELECT 
                                COALESCE(HTMLScore, 0) as HTMLScore,
                                COALESCE(VisualBasicScore, 0) as VisualBasicScore,
                                COALESCE(JavaScriptScore, 0) as JavaScriptScore,
                                COALESCE(PythonScore, 0) as PythonScore,
                                COALESCE(CSharpScore, 0) as CSharpScore,
                                COALESCE(CScore, 0) as CScore,
                                COALESCE(MySQLScore, 0) as MySQLScore,
                                COALESCE(PHPScore, 0) as PHPScore
                            FROM StudentDetails
                            WHERE IdNumber = @StudentId";
                        
                        decimal htmlScore = 0, vbScore = 0, jsScore = 0, pythonScore = 0;
                        decimal csharpScore = 0, CScore = 0, MySQLScore = 0, phpScore = 0;
                        
                        using (var langCmd = new SqlCommand(languageScoresQuery, connection))
                        {
                            langCmd.Parameters.AddWithValue("@StudentId", studentId);
                            using (var reader = await langCmd.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    htmlScore = reader.GetDecimal(0);
                                    vbScore = reader.GetDecimal(1);
                                    jsScore = reader.GetDecimal(2);
                                    pythonScore = reader.GetDecimal(3);
                                    csharpScore = reader.GetDecimal(4);
                                    CScore = reader.GetDecimal(5);
                                    MySQLScore = reader.GetDecimal(6);
                                    phpScore = reader.GetDecimal(7);
                                }
                            }
                        }
                        
                        // Calculate mastery score as average of all 8 language scores * (weight/100)
                        decimal totalLanguageScores = htmlScore + vbScore + jsScore + pythonScore + csharpScore + CScore + MySQLScore + phpScore;
                        masteryScore = (totalLanguageScores / 8) * (masteryWeight / 100.0m);
                        Console.WriteLine($"*** USING MASTERY FORMULA: ({totalLanguageScores} / 8) * ({masteryWeight}%) = {masteryScore} ***");
                        
                        // Update the mastery score in the database
                        string updateMasteryScoreQuery = @"
                            UPDATE StudentDetails
                            SET MasteryScore = @MasteryScore
                            WHERE IdNumber = @StudentId";
                        
                        using (var updateMasteryCmd = new SqlCommand(updateMasteryScoreQuery, connection))
                        {
                            updateMasteryCmd.Parameters.AddWithValue("@MasteryScore", masteryScore);
                            updateMasteryCmd.Parameters.AddWithValue("@StudentId", studentId);
                            
                            int rowsAffected = await updateMasteryCmd.ExecuteNonQueryAsync();
                            if (rowsAffected > 0)
                            {
                                Console.WriteLine($"Successfully updated MasteryScore to {masteryScore} for student {studentId}");
                                
                                // After calculating mastery score, recalculate the overall score by adding all 5 categories
                                try
                                {
                                    // Get all category scores from the database
                                    string categoryScoresQuery = @"
                                        SELECT 
                                            COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                                            COALESCE(CompletedChallengesScore, 0) as CompletedChallengesScore,
                                            COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                                            COALESCE(ExtracurricularScore, 0) as ExtracurricularScore
                                        FROM StudentDetails
                                        WHERE IdNumber = @StudentId";
                                    
                                    decimal AcademicGradesScore = 0, CompletedChallengesScore = 0, SeminarsWebinarsScore = 0, ExtracurricularScore = 0;
                                    
                                    using (var scoreCmd = new SqlCommand(categoryScoresQuery, connection))
                                    {
                                        scoreCmd.Parameters.AddWithValue("@StudentId", studentId);
                                        using (var reader = await scoreCmd.ExecuteReaderAsync())
                                        {
                                            if (await reader.ReadAsync())
                                            {
                                                AcademicGradesScore = reader.GetDecimal(0);
                                                CompletedChallengesScore = reader.GetDecimal(1);
                                                SeminarsWebinarsScore = reader.GetDecimal(2);
                                                ExtracurricularScore = reader.GetDecimal(3);
                                            }
                                        }
                                    }
                                    
                                    // Calculate overall score by adding all 5 categories
                                    decimal overallScore = AcademicGradesScore + CompletedChallengesScore + SeminarsWebinarsScore + ExtracurricularScore + masteryScore;
                                    
                                    // Update overall score in the database - using Score as the column name
                                    string updateOverallScoreQuery = @"
                                        UPDATE StudentDetails
                                        SET Score = @Score
                                        WHERE IdNumber = @StudentId";
                                    
                                    using (var updateCmd = new SqlCommand(updateOverallScoreQuery, connection))
                                    {
                                        updateCmd.Parameters.AddWithValue("@Score", overallScore);
                                        updateCmd.Parameters.AddWithValue("@StudentId", studentId);
                                        int rows = await updateCmd.ExecuteNonQueryAsync();
                                        
                                        if (rows > 0)
                                        {
                                            Console.WriteLine($"*** OVERALL SCORE UPDATED: {AcademicGradesScore} + {CompletedChallengesScore} + {SeminarsWebinarsScore} + {ExtracurricularScore} + {masteryScore} = {overallScore} ***");
                                        }
                                        else
                                        {
                                            Console.WriteLine("Failed to update OverallScore");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error recalculating overall score: {ex.Message}");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Failed to update MasteryScore for student {studentId}");
                            }
                        }
                        
                        // Return true after setting the mastery score, even if overall score calculation failed
                        return true;
                    }
                    
                    Console.WriteLine($"Final calculated MasteryScore: {masteryScore}");
                    
                    // Update achievements and mastery score in database
                    string updateSql = @"
                        UPDATE StudentDetails
                        SET Achievements = @Achievements,
                            MasteryScore = @MasteryScore
                        WHERE IdNumber = @StudentId";
                    
                    using (var updateCmd = new SqlCommand(updateSql, connection))
                    {
                        updateCmd.Parameters.AddWithValue("@Achievements", newAchievements);
                        updateCmd.Parameters.AddWithValue("@MasteryScore", masteryScore);
                        updateCmd.Parameters.AddWithValue("@StudentId", studentId);
                        
                        int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"Rows affected by update: {rowsAffected}");
                        
                        if (rowsAffected > 0)
                        {
                            // We're updating achievements and mastery score directly
                            // No need to call ScoreController as we're already handling mastery score calculation
                            
                            return true;
                        }
                        
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating student achievements: {ex.Message}");
                return false;
            }
        }

        // Check if a student is a transferee
        private async Task<bool> IsTransfereeStudent(string studentId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if we're using the new database structure
                bool usingNewTables = await TableExists(connection, "StudentDetails");
                
                string sql;
                if (usingNewTables)
                {
                    sql = @"SELECT IsTransferee FROM StudentDetails WHERE IdNumber = @StudentId";
                }
                else
                {
                    sql = @"SELECT IsTransferee FROM Students WHERE IdNumber = @StudentId";
                }
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    var result = await command.ExecuteScalarAsync();
                    
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToBoolean(result);
                    }
                }
            }
            
            return false;
        }
        
        // Helper method to check if a table exists
        private async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string sql = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
                
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }

        // Get all unique programming languages for available tests
        private async Task<List<string>> GetAvailableProgrammingLanguages(int studentYearLevel)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT DISTINCT ProgrammingLanguage 
                    FROM ProgrammingTests
                    WHERE YearLevel <= @YearLevel
                    AND IsActive = 1
                    ORDER BY ProgrammingLanguage";
                
                List<string> languages = new List<string>();
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@YearLevel", studentYearLevel);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            languages.Add(reader["ProgrammingLanguage"].ToString());
                        }
                    }
                }
                
                return languages;
            }
        }

        // Count the number of mastery tests taken by a student for a specific language
        private async Task<int> CountMasteryTestsTakenByLanguage(string studentId, string programmingLanguage)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                string sql = @"
                    SELECT COUNT(DISTINCT ts.TestId) 
                    FROM TestSubmissions ts
                    JOIN ProgrammingTests pt ON ts.TestId = pt.TestId
                    WHERE ts.StudentId = @StudentId
                    AND pt.ProgrammingLanguage = @ProgrammingLanguage";
                
                using (var command = new SqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    command.Parameters.AddWithValue("@ProgrammingLanguage", programmingLanguage);
                    
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                    
                    return 0;
                }
            }
        }
        
        // Update the language-specific score in StudentDetails
        private async Task<bool> UpdateLanguageScore(string studentId, string programmingLanguage)
        {
            _logger.LogInformation($"Updating {programmingLanguage} score for student {studentId}");
            
            // Map programming language to the exact column name in the database
            string columnName;
            
            // Handle special cases and exact mapping to database columns
            if (programmingLanguage.Equals("HTML", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "HTMLScore";
            }
            else if (programmingLanguage.Equals("CSS", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "HTMLScore"; // CSS still maps to HTMLScore as per the database structure
            }
            else if (programmingLanguage.Equals("HTML/CSS", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "HTMLScore"; // Combined HTML/CSS also maps to HTMLScore
            }
            else if (programmingLanguage.Equals("C#", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "CSharpScore";
            }
            else if (programmingLanguage.Equals("Visual Basic", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "VisualBasicScore";
            }
            else if (programmingLanguage.Equals("JavaScript", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "JavaScriptScore";
            }
            else if (programmingLanguage.Equals("Python", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "PythonScore";
            }
            else if (programmingLanguage.Equals("Java", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "CScore";
            }
            else if (programmingLanguage.Equals("SQL", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "MySQLScore";
            }
            else if (programmingLanguage.Equals("PHP", StringComparison.OrdinalIgnoreCase))
            {
                columnName = "PHPScore";
            }
            else
            {
                // Default normalization for any other languages
                string normalizedLanguage = programmingLanguage.Replace("/", "").Replace(" ", "").Replace("-", "");
                columnName = $"{normalizedLanguage}Score";
            }
            
            _logger.LogInformation($"Using column name: {columnName}");
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                try
                {
                    // 1. Count the number of tests taken for this language
                    int testsTaken = await CountMasteryTestsTakenByLanguage(studentId, programmingLanguage);
                    
                    // 2. Calculate multiplier based on tests taken
                    decimal multiplier = 0.25m; // Default is 25% for 1 test
                    if (testsTaken >= 4)
                    {
                        multiplier = 1.0m; // 100% for 4+ tests
                    }
                    else if (testsTaken == 3)
                    {
                        multiplier = 0.75m; // 75% for 3 tests
                    }
                    else if (testsTaken == 2)
                    {
                        multiplier = 0.5m; // 50% for 2 tests
                    }
                    else if (testsTaken <= 0)
                    {
                        multiplier = 0; // 0% if no tests taken
                    }
                    
                    _logger.LogInformation($"Tests taken for {programmingLanguage}: {testsTaken}, multiplier: {multiplier}");
                    
                    // 3. Get total points earned and total possible points
                    decimal earnedPoints = 0;
                    decimal totalPoints = 0;
                    
                    // First query: Get earned points from the answers the student has submitted
                    string earnedPointsQuery = @"
                        SELECT 
                            COALESCE(SUM(ta.Points), 0) AS EarnedPoints
                        FROM TestSubmissions ts
                        JOIN ProgrammingTests pt ON ts.TestId = pt.TestId
                        JOIN TestAnswers ta ON ts.SubmissionId = ta.SubmissionId
                        WHERE ts.StudentId = @StudentId 
                        AND pt.ProgrammingLanguage = @Language";
                    
                    // Second query: Get total possible points from ALL questions in ALL tests for this language
                    string totalPointsQuery = @"
                        SELECT 
                            COALESCE(SUM(pq.Points), 0) AS TotalPoints
                        FROM ProgrammingTests pt
                        JOIN ProgrammingQuestions pq ON pq.TestId = pt.TestId
                        WHERE pt.ProgrammingLanguage = @Language
                        AND pt.IsActive = 1";
                    
                    // Get earned points
                    using (var earnedCmd = new SqlCommand(earnedPointsQuery, connection))
                    {
                        earnedCmd.Parameters.AddWithValue("@StudentId", studentId);
                        earnedCmd.Parameters.AddWithValue("@Language", programmingLanguage);
                        
                        var result = await earnedCmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            earnedPoints = Convert.ToDecimal(result);
                        }
                    }
                    
                    // Get total possible points from ALL tests for this language
                    using (var totalCmd = new SqlCommand(totalPointsQuery, connection))
                    {
                        totalCmd.Parameters.AddWithValue("@Language", programmingLanguage);
                        
                        var result = await totalCmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            totalPoints = Convert.ToDecimal(result);
                        }
                        
                        _logger.LogInformation($"Total possible points for ALL {programmingLanguage} tests: {totalPoints}");
                    }
                    
                    _logger.LogInformation($"Points for {programmingLanguage}: earned = {earnedPoints}, total = {totalPoints}");
                    
                    // 4. Calculate language score with the formula
                    decimal languageScore = 0;
                    if (totalPoints > 0)
                    {
                        // Formula: (earnedPoints / totalPoints) * 100 * multiplier
                        languageScore = (earnedPoints / totalPoints) * 100 * multiplier;
                        _logger.LogInformation($"Calculated {programmingLanguage} score: ({earnedPoints}/{totalPoints}) * 100 * {multiplier} = {languageScore}");
                    }
                    
                    // 5. Update the specific language score column
                    
                    // First verify the column exists
                    bool columnExists = false;
                    string checkColumnSql = @"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'StudentDetails' 
                        AND COLUMN_NAME = @ColumnName";
                    
                    using (var checkCmd = new SqlCommand(checkColumnSql, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@ColumnName", columnName);
                        int columnCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                        columnExists = (columnCount > 0);
                        
                        if (!columnExists)
                        {
                            _logger.LogError($"Column {columnName} does not exist in StudentDetails table");
                            return false;
                        }
                    }
                    
                    string updateQuery = $"UPDATE StudentDetails SET {columnName} = @Score WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Score", languageScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Updated {columnName} for student {studentId} to {languageScore}");
                            
                            // Directly recalculate mastery score instead of calling ScoreController
                            try
                            {
                                // Get all 8 language scores for this student
                                string languageScoresQuery = @"
                                    SELECT 
                                        COALESCE(HTMLScore, 0) as HTMLScore,
                                        COALESCE(VisualBasicScore, 0) as VisualBasicScore,
                                        COALESCE(JavaScriptScore, 0) as JavaScriptScore,
                                        COALESCE(PythonScore, 0) as PythonScore,
                                        COALESCE(CSharpScore, 0) as CSharpScore,
                                        COALESCE(CScore, 0) as CScore,
                                        COALESCE(MySQLScore, 0) as MySQLScore,
                                        COALESCE(PHPScore, 0) as PHPScore
                                    FROM StudentDetails
                                    WHERE IdNumber = @StudentId";
                                
                                decimal htmlScore = 0, vbScore = 0, jsScore = 0, pythonScore = 0;
                                decimal csharpScore = 0, CScore = 0, MySQLScore = 0, phpScore = 0;
                                decimal masteryWeight = 30.0m; // Default value
                                
                                // Get the current mastery weight
                                string weightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'Mastery'";
                                using (var weightCmd = new SqlCommand(weightQuery, connection))
                                {
                                    var weightResult = await weightCmd.ExecuteScalarAsync();
                                    if (weightResult != null && weightResult != DBNull.Value)
                                    {
                                        masteryWeight = Convert.ToDecimal(weightResult);
                                    }
                                }
                                
                                using (var langCmd = new SqlCommand(languageScoresQuery, connection))
                                {
                                    langCmd.Parameters.AddWithValue("@StudentId", studentId);
                                    using (var reader = await langCmd.ExecuteReaderAsync())
                                    {
                                        if (await reader.ReadAsync())
                                        {
                                            htmlScore = reader.GetDecimal(0);
                                            vbScore = reader.GetDecimal(1);
                                            jsScore = reader.GetDecimal(2);
                                            pythonScore = reader.GetDecimal(3);
                                            csharpScore = reader.GetDecimal(4);
                                            CScore = reader.GetDecimal(5);
                                            MySQLScore = reader.GetDecimal(6);
                                            phpScore = reader.GetDecimal(7);
                                        }
                                    }
                                }
                                
                                // Calculate mastery score as average of all 8 language scores * (weight/100)
                                decimal totalLanguageScores = htmlScore + vbScore + jsScore + pythonScore + csharpScore + CScore + MySQLScore + phpScore;
                                decimal masteryScore = (totalLanguageScores / 8) * (masteryWeight / 100.0m);
                                
                                _logger.LogInformation($"Recalculated mastery score: ({totalLanguageScores} / 8) * ({masteryWeight}%) = {masteryScore}");
                                
                                // Update mastery score
                                string updateScoreQuery = @"
                                    UPDATE StudentDetails
                                    SET MasteryScore = @MasteryScore
                                    WHERE IdNumber = @StudentId";
                                
                                using (var updateCmd = new SqlCommand(updateScoreQuery, connection))
                                {
                                    updateCmd.Parameters.AddWithValue("@MasteryScore", masteryScore);
                                    updateCmd.Parameters.AddWithValue("@StudentId", studentId);
                                    
                                    int masteryRowsAffected = await updateCmd.ExecuteNonQueryAsync();
                                    
                                    if (masteryRowsAffected > 0)
                                    {
                                        _logger.LogInformation($"Successfully updated mastery score to {masteryScore} for student {studentId}");
                                        
                                        // Now recalculate the overall score
                                        try
                                        {
                                            // Get all category scores
                                            string categoryScoresQuery = @"
                                                SELECT 
                                                    COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                                                    COALESCE(CompletedChallengesScore, 0) as CompletedChallengesScore,
                                                    COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                                                    COALESCE(ExtracurricularScore, 0) as ExtracurricularScore
                                                FROM StudentDetails
                                                WHERE IdNumber = @StudentId";
                                            
                                            decimal academicGradesScore = 0, completedChallengesScore = 0;
                                            decimal seminarsWebinarsScore = 0, extracurricularScore = 0;
                                            
                                            using (var scoreCmd = new SqlCommand(categoryScoresQuery, connection))
                                            {
                                                scoreCmd.Parameters.AddWithValue("@StudentId", studentId);
                                                using (var reader = await scoreCmd.ExecuteReaderAsync())
                                                {
                                                    if (await reader.ReadAsync())
                                                    {
                                                        academicGradesScore = reader.GetDecimal(0);
                                                        completedChallengesScore = reader.GetDecimal(1);
                                                        seminarsWebinarsScore = reader.GetDecimal(2);
                                                        extracurricularScore = reader.GetDecimal(3);
                                                    }
                                                }
                                            }
                                            
                                            // Calculate overall score
                                            decimal overallScore = academicGradesScore + completedChallengesScore + 
                                                              seminarsWebinarsScore + extracurricularScore + masteryScore;
                                            
                                            // Update overall score
                                            string updateOverallQuery = @"
                                                UPDATE StudentDetails
                                                SET Score = @Score
                                                WHERE IdNumber = @StudentId";
                                            
                                            using (var updateOverallCmd = new SqlCommand(updateOverallQuery, connection))
                                            {
                                                updateOverallCmd.Parameters.AddWithValue("@Score", overallScore);
                                                updateOverallCmd.Parameters.AddWithValue("@StudentId", studentId);
                                                
                                                int overallRowsAffected = await updateOverallCmd.ExecuteNonQueryAsync();
                                                
                                                if (overallRowsAffected > 0)
                                                {
                                                    _logger.LogInformation($"Successfully updated overall score to {overallScore} for student {studentId}");
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, $"Error updating overall score: {ex.Message}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error recalculating mastery score: {ex.Message}");
                            }
                            
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning($"No rows affected when updating {columnName} for student {studentId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating language score: {ex.Message}");
                }
                
                return false;
            }
        }
        
        // New endpoint to get EduBadge certificate information
        [HttpGet]
        public async Task<IActionResult> GetEduBadgeCertificate(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                studentId = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(studentId))
                {
                    return Json(new { hasCertificate = false, message = "Student ID is required" });
                }
            }
            
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if student has a 4th-year grade assigned
                    string checkGradeSql = "SELECT FourthYearGrade FROM StudentDetails WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(checkGradeSql, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var fourthYearGrade = await command.ExecuteScalarAsync();
                        
                        // Only provide certificate if student has a 4th-year grade
                        bool hasFourthYearGrade = (fourthYearGrade != null && fourthYearGrade != DBNull.Value);
                        if (!hasFourthYearGrade)
                        {
                            return Json(new { hasCertificate = false, message = "Certificate will be available after 4th-year grade is assigned" });
                        }
                        
                        // Get student's name
                        string studentName = await GetStudentName(studentId);
                        
                        // Get student's achievements (from Achievements column)
                        string achievementsSql = "SELECT Achievements FROM StudentDetails WHERE IdNumber = @StudentId";
                        string achievementsData = "";
                        
                        using (var achievementsCmd = new SqlCommand(achievementsSql, connection))
                        {
                            achievementsCmd.Parameters.AddWithValue("@StudentId", studentId);
                            var result = await achievementsCmd.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                achievementsData = result.ToString();
                            }
                        }
                        
                        // Parse achievements data
                        var achievements = new List<object>();
                        if (!string.IsNullOrEmpty(achievementsData))
                        {
                            string[] achievementItems = achievementsData.Split('|', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var item in achievementItems)
                            {
                                achievements.Add(new { name = item.Trim(), rank = "" });
                            }
                        }
                        
                        // Get masteries data (programming tests completed)
                        var masteries = new List<object>();
                        string masteriesSql = @"
                            SELECT t.TestName, t.ProgrammingLanguage
                            FROM TestSubmissions s
                            JOIN ProgrammingTests t ON s.TestId = t.TestId
                            WHERE s.StudentId = @StudentId
                            ORDER BY t.TestName";
                        
                        try 
                        {
                            using (var masteriesCmd = new SqlCommand(masteriesSql, connection))
                            {
                                masteriesCmd.Parameters.AddWithValue("@StudentId", studentId);
                                
                                using (var reader = await masteriesCmd.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
                                    {
                                        string testName = reader["TestName"]?.ToString() ?? "";
                                        string language = reader["ProgrammingLanguage"]?.ToString() ?? "";
                                        masteries.Add(new { subject = $"{testName} ({language})" });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error retrieving masteries data. Using empty masteries list.");
                            // Continue with empty masteries rather than failing entirely
                        }
                        
                        // Check if AttendanceRecords table exists and get webinars data
                        var webinars = new List<object>();
                        if (await TableExists(connection, "AttendanceRecords"))
                        {
                            string userId = null;
                            
                            // Get student's UserID
                            string userIdSql = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var userIdCmd = new SqlCommand(userIdSql, connection))
                            {
                                userIdCmd.Parameters.AddWithValue("@StudentId", studentId);
                                var userIdResult = await userIdCmd.ExecuteScalarAsync();
                                if (userIdResult != null && userIdResult != DBNull.Value)
                                {
                                    userId = userIdResult.ToString();
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(userId))
                            {
                                string webinarsSql = @"
                                    SELECT EventName, EventDate
                                    FROM AttendanceRecords
                                    WHERE StudentId = @UserId AND IsVerified = 1
                                    ORDER BY EventDate DESC";
                                
                                using (var webinarsCmd = new SqlCommand(webinarsSql, connection))
                                {
                                    webinarsCmd.Parameters.AddWithValue("@UserId", userId);
                                    
                                    using (var reader = await webinarsCmd.ExecuteReaderAsync())
                                    {
                                        while (await reader.ReadAsync())
                                        {
                                            string eventName = reader["EventName"]?.ToString() ?? "";
                                            DateTime eventDate = Convert.ToDateTime(reader["EventDate"]);
                                            webinars.Add(new { 
                                                name = $"{eventName} ({eventDate.ToString("MMM dd, yyyy")})" 
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Check if ExtraCurricularActivities table exists and get extracurricular data
                        var extracurricular = new List<object>();
                        if (await TableExists(connection, "ExtraCurricularActivities"))
                        {
                            string userId = null;
                            
                            // Get student's UserID if not already retrieved
                            if (userId == null)
                            {
                                string userIdSql = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                                using (var userIdCmd = new SqlCommand(userIdSql, connection))
                                {
                                    userIdCmd.Parameters.AddWithValue("@StudentId", studentId);
                                    var userIdResult = await userIdCmd.ExecuteScalarAsync();
                                    if (userIdResult != null && userIdResult != DBNull.Value)
                                    {
                                        userId = userIdResult.ToString();
                                    }
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(userId))
                            {
                                string extracurricularSql = @"
                                    SELECT ActivityName, ActivityCategory, Rank
                                    FROM ExtraCurricularActivities
                                    WHERE StudentId = @UserId AND IsVerified = 1
                                    ORDER BY ActivityDate DESC";
                                
                                using (var extraCmd = new SqlCommand(extracurricularSql, connection))
                                {
                                    extraCmd.Parameters.AddWithValue("@UserId", userId);
                                    
                                    using (var reader = await extraCmd.ExecuteReaderAsync())
                                    {
                                        while (await reader.ReadAsync())
                                        {
                                            string activity = reader["ActivityName"]?.ToString() ?? "";
                                            string category = reader["ActivityCategory"]?.ToString() ?? "";
                                            string rank = reader["Rank"] != DBNull.Value ? reader["Rank"].ToString() : "";
                                            
                                            if (!string.IsNullOrEmpty(category))
                                            {
                                                activity = $"{activity} ({category})";
                                            }
                                            
                                            extracurricular.Add(new { activity = activity, rank = rank });
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Get badge URL if available
                        string badgeImageUrl = await _badgeService.GetStudentBadgeUrl(studentId);
                        
                        // Get badge color from database
                        string badgeColor = "Unknown";
                        string badgeColorSql = "SELECT BadgeColor FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var badgeColorCmd = new SqlCommand(badgeColorSql, connection))
                        {
                            badgeColorCmd.Parameters.AddWithValue("@StudentId", studentId);
                            var badgeColorResult = await badgeColorCmd.ExecuteScalarAsync();
                            if (badgeColorResult != null && badgeColorResult != DBNull.Value)
                            {
                                badgeColor = badgeColorResult.ToString();
                            }
                        }
                        
                        // Create certificate object
                        var certificate = new
                        {
                            hasCertificate = true,
                            studentId = studentId,
                            studentName = studentName,
                            achievements = achievements,
                            masteries = masteries,
                            webinars = webinars,
                            extracurricular = extracurricular,
                            badgeImageUrl = badgeImageUrl,
                            badgeColor = badgeColor,
                            issueDate = DateTime.Now.ToString("yyyy-MM-dd")
                        };
                        
                        return Json(certificate);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving EduBadge certificate for student {studentId}");
                return Json(new { hasCertificate = false, message = "An error occurred while retrieving the certificate" });
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> ViewEduBadgeCertificate(string studentId)
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
                // Reuse the GetEduBadgeCertificate logic to get certificate data
                var certificateResult = await GetEduBadgeCertificate(studentId) as JsonResult;
                if (certificateResult == null)
                {
                    return NotFound();
                }
                
                var certificateData = certificateResult.Value;
                
                // Check if certificate exists
                bool hasCertificate = (bool)((dynamic)certificateData).hasCertificate;
                if (!hasCertificate)
                {
                    return Content("<html><body><h1>Certificate Not Available</h1><p>Your EduBadge certificate will be generated when your teacher assigns your 4th-year grade.</p></body></html>", "text/html");
                }
                
                // Get student details
                string studentName = ((dynamic)certificateData).studentName;
                var achievements = ((dynamic)certificateData).achievements;
                var masteries = ((dynamic)certificateData).masteries;
                var webinars = ((dynamic)certificateData).webinars;
                var extracurricular = ((dynamic)certificateData).extracurricular;
                string badgeImageUrl = ((dynamic)certificateData).badgeImageUrl;
                string badgeColor = ((dynamic)certificateData).badgeColor;
                string issueDate = ((dynamic)certificateData).issueDate;
                
                // Build HTML certificate
                string certificateHtml = BuildEduBadgeCertificateHtml(studentId, studentName, achievements, masteries, webinars, extracurricular, badgeImageUrl, badgeColor, issueDate);
                
                return Content(certificateHtml, "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error viewing EduBadge certificate for student {studentId}");
                return Content("<html><body><h1>Error</h1><p>An error occurred while retrieving the certificate.</p></body></html>", "text/html");
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> DownloadEduBadgeCertificate(string studentId)
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
                // Reuse the GetEduBadgeCertificate logic to get certificate data
                var certificateResult = await GetEduBadgeCertificate(studentId) as JsonResult;
                if (certificateResult == null)
                {
                    return NotFound();
                }
                
                var certificateData = certificateResult.Value;
                
                // Check if certificate exists
                bool hasCertificate = (bool)((dynamic)certificateData).hasCertificate;
                if (!hasCertificate)
                {
                    return NotFound();
                }
                
                // Get student details for filename
                string studentName = ((dynamic)certificateData).studentName;
                string safeFileName = studentName.Replace(" ", "_");
                
                // Build HTML certificate (reusing the same method as ViewEduBadgeCertificate)
                string certificateHtml = BuildEduBadgeCertificateHtml(
                    studentId,
                    ((dynamic)certificateData).studentName, 
                    ((dynamic)certificateData).achievements,
                    ((dynamic)certificateData).masteries,
                    ((dynamic)certificateData).webinars,
                    ((dynamic)certificateData).extracurricular,
                    ((dynamic)certificateData).badgeImageUrl,
                    ((dynamic)certificateData).badgeColor,
                    ((dynamic)certificateData).issueDate
                );
                
                // Return as a downloadable file
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(certificateHtml);
                return File(bytes, "text/html", $"EduBadge_Certificate_{safeFileName}.html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading EduBadge certificate for student {studentId}");
                return NotFound();
            }
        }
        
        private string BuildEduBadgeCertificateHtml(string studentId, string studentName, dynamic achievements, dynamic masteries, dynamic webinars, dynamic extracurricular, string badgeImageUrl, string badgeColor, string issueDate)
        {
            string certificateHtml = $@"
            <!DOCTYPE html>
            <html lang='en'>
            <head>
                <meta charset='UTF-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <title>EduBadge Certificate - {studentName}</title>
                <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/5.15.4/css/all.min.css'>
                <link href='https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&display=swap' rel='stylesheet'>
                <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css'>
                <style>
                    body {{
                        font-family: 'Inter', sans-serif;
                        background: #f9f9f9;
                        color: #333;
                    }}
                    .certificate-container {{
                        max-width: 800px;
                        margin: 30px auto;
                        background: white;
                        border-radius: 15px;
                        box-shadow: 0 10px 30px rgba(0,0,0,0.1);
                        overflow: hidden;
                        border: 5px solid #e74c3c;
                    }}
                    .certificate-header {{
                        text-align: center;
                        padding: 40px 20px;
                        background: linear-gradient(135deg, #e74c3c, #c0392b);
                        color: white;
                    }}
                    .certificate-header h1 {{
                        font-size: 36px;
                        font-weight: 700;
                        margin-bottom: 10px;
                    }}
                    .certificate-body {{
                        padding: 40px;
                    }}
                    .certificate-body h2 {{
                        text-align: center;
                        font-size: 24px;
                        margin-bottom: 30px;
                        color: #333;
                    }}
                    .certificate-name {{
                        font-size: 30px;
                        font-weight: 700;
                        text-align: center;
                        color: #e74c3c;
                        margin-bottom: 30px;
                    }}
                    .certificate-section {{
                        margin-bottom: 30px;
                    }}
                    .certificate-section h3 {{
                        font-size: 18px;
                        color: #c0392b;
                        border-bottom: 2px solid #eee;
                        padding-bottom: 10px;
                        margin-bottom: 15px;
                    }}
                    .badge-item {{
                        display: flex;
                        align-items: center;
                        margin-bottom: 10px;
                    }}
                    .badge-rank {{
                        display: inline-block;
                        padding: 3px 10px;
                        background: #c0392b;
                        color: white;
                        border-radius: 15px;
                        font-size: 12px;
                        margin-left: 10px;
                    }}
                    .certificate-footer {{
                        text-align: center;
                        padding: 20px;
                        background: #f5f5f5;
                        border-top: 1px solid #eee;
                    }}
                    .badge-image {{
                        max-width: 180px;
                        margin: 0 auto 20px;
                        display: block;
                        border-radius: 50%;
                        box-shadow: 0 5px 15px rgba(0,0,0,0.2);
                        border: 4px solid #e74c3c;
                    }}
                    .badge-container {{
                        text-align: center;
                        margin-bottom: 30px;
                        position: relative;
                    }}
                    .badge-title {{
                        font-size: 16px;
                        font-weight: 600;
                        color: #666;
                        margin-top: 10px;
                    }}
                    .graduate-badge {{
                        position: relative;
                        display: inline-block;
                    }}
                    .graduate-badge::after {{
                        content: '★ GRADUATE ★';
                        position: absolute;
                        bottom: -5px;
                        left: 50%;
                        transform: translateX(-50%);
                        background: #e74c3c;
                        color: white;
                        padding: 5px 15px;
                        border-radius: 20px;
                        font-size: 14px;
                        font-weight: 700;
                        white-space: nowrap;
                    }}
                    .signature-line {{
                        width: 200px;
                        height: 2px;
                        background: #333;
                        margin: 10px auto;
                    }}
                </style>
            </head>
            <body>
                <div class='certificate-container'>
                    <div class='certificate-header'>
                        <h1>EduBadge Achievement Certificate</h1>
                        <p>Student Excellence Recognition</p>
                    </div>
                    
                    <div class='certificate-body'>
                        <h2>This certifies that</h2>
                        <div class='certificate-name'>{studentName}</div>
                        
                        <p class='text-center mb-4'>Has successfully fulfilled all requirements and demonstrated excellence as a graduate student, earning a {badgeColor} Badge in recognition of this achievement.</p>
                        
                        <!-- Badge Color indicator instead of badge image -->
                        <div class='badge-container'>
                            <div class='badge-color-display' style='background-color: {GetBadgeColorHex(badgeColor)};'>
                                <span>{badgeColor}</span>
                            </div>
                        </div>
                        
                        <div class='row'>
                            <div class='col-md-6'>
                                <div class='certificate-section'>
                                    <h3><i class='fas fa-star'></i> Achievements</h3>
                                    <ul class='list-group'>";
            
            // Add achievements
            bool hasAchievements = false;
            foreach (var achievement in achievements)
            {
                hasAchievements = true;
                string name = achievement.name;
                string rank = achievement.rank;
                
                certificateHtml += $@"
                                        <li class='list-group-item d-flex justify-content-between align-items-center'>
                                            {name}
                                            {(!string.IsNullOrEmpty(rank) ? $"<span class='badge bg-primary rounded-pill'>{rank}</span>" : "")}
                                        </li>";
            }
            
            if (!hasAchievements)
            {
                certificateHtml += @"
                                        <li class='list-group-item text-muted'>No achievements recorded</li>";
            }
            
            certificateHtml += $@"
                                    </ul>
                                </div>
                            </div>
                            
                            <div class='col-md-6'>
                                <div class='certificate-section'>
                                    <h3><i class='fas fa-graduation-cap'></i> Masteries</h3>
                                    <ul class='list-group'>";
            
            // Add masteries
            bool hasMasteries = false;
            foreach (var mastery in masteries)
            {
                hasMasteries = true;
                string subject = mastery.subject;
                
                certificateHtml += $@"
                                        <li class='list-group-item'>{subject}</li>";
            }
            
            if (!hasMasteries)
            {
                certificateHtml += @"
                                        <li class='list-group-item text-muted'>No masteries recorded</li>";
            }
            
            certificateHtml += $@"
                                    </ul>
                                </div>
                            </div>
                        </div>
                        
                        <div class='row'>
                            <div class='col-md-6'>
                                <div class='certificate-section'>
                                    <h3><i class='fas fa-calendar-check'></i> Webinars/Seminars</h3>
                                    <ul class='list-group'>";
            
            // Add webinars
            bool hasWebinars = false;
            foreach (var webinar in webinars)
            {
                hasWebinars = true;
                string name = webinar.name;
                
                certificateHtml += $@"
                                        <li class='list-group-item'>{name}</li>";
            }
            
            if (!hasWebinars)
            {
                certificateHtml += @"
                                        <li class='list-group-item text-muted'>No webinars/seminars recorded</li>";
            }
            
            certificateHtml += $@"
                                    </ul>
                                </div>
                            </div>
                            
                            <div class='col-md-6'>
                                <div class='certificate-section'>
                                    <h3><i class='fas fa-trophy'></i> Extracurricular Activities</h3>
                                    <ul class='list-group'>";
            
            // Add extracurricular activities
            bool hasExtracurricular = false;
            foreach (var activity in extracurricular)
            {
                hasExtracurricular = true;
                string activityName = activity.activity;
                string rank = activity.rank;
                
                certificateHtml += $@"
                                        <li class='list-group-item d-flex justify-content-between align-items-center'>
                                            {activityName}
                                            {(!string.IsNullOrEmpty(rank) ? $"<span class='badge bg-success rounded-pill'>{rank}</span>" : "")}
                                        </li>";
            }
            
            if (!hasExtracurricular)
            {
                certificateHtml += @"
                                        <li class='list-group-item text-muted'>No extracurricular activities recorded</li>";
            }
            
            certificateHtml += $@"
                                    </ul>
                                </div>
                            </div>
                        </div>
                        
                    </div>
                    
                    <div class='certificate-footer'>
                        <div class='row'>
                            <div class='col-md-6 text-center'>
                                <div class='signature-line'></div>
                                <p>School Principal</p>
                            </div>
                            <div class='col-md-6 text-center'>
                                <div class='signature-line'></div>
                                <p>Date Issued: {DateTime.Parse(issueDate).ToString("MMMM dd, yyyy")}</p>
                            </div>
                        </div>
                        <p class='mt-4'>Certificate ID: {Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}-{studentId}</p>
                    </div>
                </div>
            </body>
            </html>";
            
            return certificateHtml;
        }

        // Helper method to convert badge color name to hex code
        private string GetBadgeColorHex(string badgeColor)
        {
            switch (badgeColor.ToLower())
            {
                case "gold":
                    return "#FFD700";
                case "silver":
                    return "#C0C0C0";
                case "bronze":
                    return "#CD7F32";
                case "platinum":
                    return "#E5E4E2";
                case "diamond":
                    return "#B9F2FF";
                case "red":
                    return "#FF5733";
                case "blue":
                    return "#3498DB";
                case "green":
                    return "#2ECC71";
                case "purple":
                    return "#9B59B6";
                default:
                    return "#808080"; // Default gray
            }
        }
    }
} 