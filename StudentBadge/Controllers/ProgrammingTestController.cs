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

namespace StudentBadge.Controllers
{
    public class ProgrammingTestController : Controller
    {
        private readonly string _connectionString;
        private readonly CertificateService _certificateService;
        private readonly ILogger<ProgrammingTestController> _logger;
        
        public ProgrammingTestController(IConfiguration configuration, CertificateService certificateService, ILogger<ProgrammingTestController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _certificateService = certificateService;
            _logger = logger;
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
            
            // Check if test is appropriate for this student's year level
            if (test.YearLevel != studentYearLevel)
            {
                TempData["Error"] = "You don't have access to this test.";
                return RedirectToAction("StudentTests");
            }
            
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
                            
                            Console.WriteLine($"Generating certificate for student {studentId}, test {model.TestId}, score {score}");
                            
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
                            
                            Console.WriteLine($"Certificate generated with ID: {certificateId}");
                            
                            if (certificateId <= 0)
                            {
                                errorMessage = "Certificate was not created successfully";
                            }
                            else
                            {
                                // Update student's achievements with the completed test
                                await UpdateStudentAchievements(studentId, test.TestName);
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
                        Console.WriteLine($"Error generating certificate: {ex.Message}");
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
                Console.WriteLine($"Error submitting test: {ex.Message}");
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
            
            // Save the answer with earned points
            string sql = @"INSERT INTO TestAnswers (SubmissionId, QuestionId, AnswerText, Points)
                          VALUES (@SubmissionId, @QuestionId, @AnswerText, @Points);";
            
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@SubmissionId", submissionId);
                command.Parameters.AddWithValue("@QuestionId", questionId);
                command.Parameters.AddWithValue("@AnswerText", answer ?? string.Empty);
                command.Parameters.AddWithValue("@Points", earnedPoints);
                
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
            
            // Get the list of tests this student has already completed
            var completedTestScores = await GetCompletedTestScores(studentId);
            var completedTestIds = completedTestScores.Keys.ToList();
            
            // Get available tests for this student's year level
            var allTests = await GetTestsByYearLevel(studentYearLevel);
            
            // Filter tests to only show available (not completed) ones
            var availableTests = allTests.Where(t => !completedTestIds.Contains(t.TestId)).ToList();
            
            ViewBag.StudentYearLevel = studentYearLevel;
            ViewBag.StudentId = studentId;
            
            return View(availableTests);
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
                
                // Add data to ViewBag
                ViewBag.StudentId = studentId;
                ViewBag.FullName = studentName;
                
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
            // Get current user info
            string studentId = HttpContext.Session.GetString("IdNumber");
            string employerId = HttpContext.Session.GetString("EmployerId");
            
            // Allow both students and employers to view certificates
            bool isAuthorizedUser = !string.IsNullOrEmpty(studentId) || !string.IsNullOrEmpty(employerId);
            
            if (!isAuthorizedUser)
            {
                return RedirectToAction("Login", "Home");
            }
            
            try
            {
                // Get certificate
                var certificate = await _certificateService.GetCertificateById(id);
                
                if (certificate == null)
                {
                    return NotFound();
                }
                
                // Only check student permission if it's a student viewing (not an employer)
                if (!string.IsNullOrEmpty(studentId) && certificate.StudentId != studentId)
                {
                    return NotFound();
                }
                
                // Check if we have image data
                if (certificate.CertificateData != null && certificate.CertificateData.Length > 0)
                {
                    // Return the certificate as an image
                    return File(certificate.CertificateData, certificate.CertificateContentType ?? "image/png");
                }
                else if (!string.IsNullOrEmpty(certificate.CertificateContent))
                {
                    // Fall back to HTML content if available
                    return Content(certificate.CertificateContent, "text/html");
                }
                else
                {
                    // No certificate content available
                    return Content("<html><body><h1>Certificate Not Found</h1><p>The certificate could not be displayed.</p></body></html>", "text/html");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving certificate: {ex.Message}");
                return Content($"<html><body><h1>Error</h1><p>An error occurred: {ex.Message}</p></body></html>", "text/html");
            }
        }

        // New API endpoint for getting student certificates
        [HttpGet]
        public async Task<IActionResult> GetStudentCertificates(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return BadRequest("Student ID is required");
            }
            
            try
            {
                // Get student certificates
                var certificates = await _certificateService.GetStudentCertificates(studentId);
                
                // Return as JSON
                return Json(certificates);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving certificates: {ex.Message}");
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
                        // Calculate mastery score using the formula: (total score / total items) * 100 * 0.2
                        masteryScore = (totalTestScore / totalTestItems) * 100 * 0.2m;
                        Console.WriteLine($"*** USING CORRECT FORMULA: ({totalTestScore} / {totalTestItems}) * 100 * 0.2 = {masteryScore} ***");
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
                        
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating student achievements: {ex.Message}");
                return false;
            }
        }
    }
} 