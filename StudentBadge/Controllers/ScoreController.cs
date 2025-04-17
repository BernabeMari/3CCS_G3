using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class ScoreController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ScoreController> _logger;

        public ScoreController(IConfiguration configuration, ILogger<ScoreController> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost]
        [Route("CalculateSeminarsScoreFromAttendance")]
        public async Task<IActionResult> CalculateSeminarsScoreFromAttendance(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateSeminarsScoreFromAttendance");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating seminar score for student {studentId} based on attendance records");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                        // We'll keep using studentId for queries, but we have resolvedUserId if needed
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Ensure the StudentDetails table has a SeminarsWebinarsScore column
                    if (!await ColumnExists(connection, "StudentDetails", "SeminarsWebinarsScore"))
                    {
                        _logger.LogWarning("SeminarsWebinarsScore column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            "ALTER TABLE StudentDetails ADD SeminarsWebinarsScore decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created SeminarsWebinarsScore column with default value 0");
                        }
                    }

                    // Check if AttendanceRecords table exists
                    if (!await TableExists(connection, "AttendanceRecords"))
                    {
                        _logger.LogWarning("AttendanceRecords table does not exist. Setting seminar score to 0.");
                        await UpdateSeminarScoreValue(connection, studentId, 0);
                        return Json(new { success = true, message = "No attendance records. Seminar score set to 0." });
                    }

                    // First try with the original studentId
                    string countCheckQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE StudentId = @StudentId";
                    int recordCount = 0;
                    
                    using (var command = new SqlCommand(countCheckQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        recordCount = (int)await command.ExecuteScalarAsync();
                        _logger.LogInformation($"Found {recordCount} attendance records with direct StudentId {studentId}");
                    }
                    
                    // If no records found, try with the resolved UserId if available
                    if (recordCount == 0 && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        using (var command = new SqlCommand(countCheckQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", resolvedUserId);
                            recordCount = (int)await command.ExecuteScalarAsync();
                            _logger.LogInformation($"Found {recordCount} attendance records with resolved UserId {resolvedUserId}");
                            
                            // If we found records with the resolved ID, use that for further queries
                            if (recordCount > 0)
                            {
                                studentId = resolvedUserId;
                            }
                        }
                    }
                    
                    if (recordCount == 0)
                    {
                        _logger.LogWarning($"No attendance records found for student {studentId}. Setting seminar score to 0.");
                        await UpdateSeminarScoreValue(connection, studentId, 0);
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { success = true, message = "No attendance records found. Seminar score set to 0." });
                    }

                    // Check if Score column exists in AttendanceRecords
                    if (!await ColumnExists(connection, "AttendanceRecords", "Score"))
                    {
                        _logger.LogWarning("Score column does not exist in AttendanceRecords. Using default value of 100.");
                        
                        // Get the total count of attendance records for the student
                        string countQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE StudentId = @StudentId";
                        int totalAttendance = 0;
                        
                        using (var command = new SqlCommand(countQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            totalAttendance = (int)await command.ExecuteScalarAsync();
                        }
                        
                        // Calculate seminar score: 1 point for every attendance, max 10 points
                        decimal seminarScore = Math.Min(totalAttendance, 10);
                        
                        // Update the score
                        bool updateSuccess = await UpdateSeminarScoreValue(connection, studentId, seminarScore);
                        
                        if (!updateSuccess)
                        {
                            _logger.LogWarning($"Failed to update seminar score for student {studentId}");
                            return Json(new { success = false, message = "Failed to update seminar score." });
                        }
                        
                        _logger.LogInformation($"Updated seminar score for student {studentId} to {seminarScore} based on attendance count");
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { 
                            success = true, 
                            message = $"Seminar score updated to {seminarScore} based on {totalAttendance} attendance records." 
                        });
                    }
                    else
                    {
                        // Sum up all scores from attendance records
                        string sumQuery = "SELECT COALESCE(SUM(Score), 0) FROM AttendanceRecords WHERE StudentId = @StudentId";
                        decimal totalScore = 0;
                        
                        using (var command = new SqlCommand(sumQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                totalScore = Convert.ToDecimal(result);
                                _logger.LogInformation($"Total attendance score for student {studentId}: {totalScore}");
                            }
                            else
                            {
                                _logger.LogWarning($"No attendance scores found for student {studentId}");
                            }
                        }
                        
                        // Calculate seminar score: 1 point for every 100 points, max 10 points
                        decimal seminarScore = Math.Min(Math.Floor(totalScore / 100), 10);
                        
                        // Update the score
                        bool updateSuccess = await UpdateSeminarScoreValue(connection, studentId, seminarScore);
                        
                        if (!updateSuccess)
                        {
                            _logger.LogWarning($"Failed to update seminar score for student {studentId}");
                            return Json(new { success = false, message = "Failed to update seminar score." });
                        }
                        
                        _logger.LogInformation($"Updated seminar score for student {studentId} to {seminarScore} based on total attendance score of {totalScore}");
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { 
                            success = true, 
                            message = $"Seminar score updated to {seminarScore} based on total attendance score of {totalScore}." 
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating seminar score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        // Helper method to resolve UserId from StudentId
        private async Task<string> ResolveUserIdFromStudentId(SqlConnection connection, string studentId)
        {
            try
            {
                // First check if StudentId is already a UserId
                string checkUserIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @StudentId";
                using (var command = new SqlCommand(checkUserIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int count = (int)await command.ExecuteScalarAsync();
                    if (count > 0)
                    {
                        // studentId is already a valid UserId
                        return studentId;
                    }
                }
                
                // Otherwise, try to get the UserId from IdNumber
                string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                using (var command = new SqlCommand(getUserIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString();
                    }
                }
                
                // If we couldn't resolve it, return empty string
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resolving UserId for StudentId {studentId}");
                return string.Empty;
            }
        }

        [HttpPost]
        [Route("CalculateExtracurricularScoreFromActivities")]
        public async Task<IActionResult> CalculateExtracurricularScoreFromActivities(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateExtracurricularScoreFromActivities");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating extracurricular score for student {studentId} based on activities");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                        // We'll keep using studentId for queries, but we have resolvedUserId if needed
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Ensure the StudentDetails table has a ExtracurricularScore column
                    if (!await ColumnExists(connection, "StudentDetails", "ExtracurricularScore"))
                    {
                        _logger.LogWarning("ExtracurricularScore column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            "ALTER TABLE StudentDetails ADD ExtracurricularScore decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created ExtracurricularScore column with default value 0");
                        }
                    }

                    // Check if ExtraCurricularActivities table exists
                    if (!await TableExists(connection, "ExtraCurricularActivities"))
                    {
                        _logger.LogWarning("ExtraCurricularActivities table does not exist. Setting extracurricular score to 0.");
                        await UpdateExtracurricularScoreValue(connection, studentId, 0);
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { success = true, message = "No extracurricular activities. Score set to 0." });
                    }

                    // First try with the original studentId
                    string countQuery = "SELECT COUNT(*) FROM ExtraCurricularActivities WHERE StudentId = @StudentId";
                    int activityCount = 0;
                    
                    using (var command = new SqlCommand(countQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        activityCount = (int)await command.ExecuteScalarAsync();
                        _logger.LogInformation($"Found {activityCount} extracurricular activities with direct StudentId {studentId}");
                    }
                    
                    // If no activities found, try with the resolved UserId if available
                    if (activityCount == 0 && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        using (var command = new SqlCommand(countQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", resolvedUserId);
                            activityCount = (int)await command.ExecuteScalarAsync();
                            _logger.LogInformation($"Found {activityCount} extracurricular activities with resolved UserId {resolvedUserId}");
                            
                            // If we found activities with the resolved ID, use that for further queries
                            if (activityCount > 0)
                            {
                                studentId = resolvedUserId;
                            }
                        }
                    }
                    
                    // Calculate extracurricular score based on average of activity scores * 20%
                    decimal activityScore = 0;
                    
                    if (activityCount > 0)
                    {
                        // Sum up all scores from extracurricular activities
                        string sumQuery = "SELECT COALESCE(SUM(Score), 0) FROM ExtraCurricularActivities WHERE StudentId = @StudentId";
                        decimal totalScore = 0;
                        
                        using (var command = new SqlCommand(sumQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                totalScore = Convert.ToDecimal(result);
                                _logger.LogInformation($"Total extracurricular score for student {studentId}: {totalScore}");
                                
                                // Calculate average score and apply 20% weighting
                                decimal averageScore = totalScore / activityCount;
                                activityScore = averageScore * 0.2m;
                                
                                // No cap for extracurricular scores
                                // activityScore = Math.Min(activityScore, 10);
                                
                                _logger.LogInformation($"Average score: {averageScore}, After 20% weighting: {activityScore}");
                            }
                            else
                            {
                                _logger.LogWarning($"No extracurricular scores found for student {studentId}");
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Calculated extracurricular score for student {studentId} to {activityScore} based on {activityCount} activities");
                    
                    // Update the score
                    bool updateSuccess = await UpdateExtracurricularScoreValue(connection, studentId, activityScore);
                    
                    if (!updateSuccess)
                    {
                        _logger.LogWarning($"Failed to update extracurricular score for student {studentId}");
                        return Json(new { success = false, message = "Failed to update extracurricular score." });
                    }
                    
                    // Update the overall student score
                    await RecalculateScore(connection, studentId);
                    
                    return Json(new { 
                        success = true, 
                        message = $"Extracurricular score updated to {activityScore} based on {activityCount} activities." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating extracurricular score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("CalculateAllScoresForStudent")]
        public async Task<IActionResult> CalculateAllScoresForStudent(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateAllScoresForStudent");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating all scores for student {studentId}");
                
                bool anySuccess = false;
                string results = "";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if student exists with UserId or IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Calculate academic score
                    try
                    {
                        var academicResult = await CalculateAcademicGradesScore(studentId);
                        if (academicResult is JsonResult jsonAcademicResult && 
                            jsonAcademicResult.Value is System.Dynamic.ExpandoObject expandoAcademic)
                        {
                            IDictionary<string, object> resultDict = expandoAcademic as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Academic: {message}; ";
                            _logger.LogInformation($"Academic score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating academic score for student {StudentId}", studentId);
                        results += "Academic: Error; ";
                    }
                    
                    // Calculate seminar score
                    try
                    {
                        var seminarResult = await CalculateSeminarsScoreFromAttendance(studentId);
                        if (seminarResult is JsonResult jsonSeminarResult && 
                            jsonSeminarResult.Value is System.Dynamic.ExpandoObject expandoSeminar)
                        {
                            IDictionary<string, object> resultDict = expandoSeminar as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Seminar: {message}; ";
                            _logger.LogInformation($"Seminar score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating seminar score for student {StudentId}", studentId);
                        results += "Seminar: Error; ";
                    }
                    
                    // Calculate extracurricular score
                    try
                    {
                        var extracurricularResult = await CalculateExtracurricularScoreFromActivities(studentId);
                        if (extracurricularResult is JsonResult jsonExtraResult && 
                            jsonExtraResult.Value is System.Dynamic.ExpandoObject expandoExtra)
                        {
                            IDictionary<string, object> resultDict = expandoExtra as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Extracurricular: {message}; ";
                            _logger.LogInformation($"Extracurricular score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating extracurricular score for student {StudentId}", studentId);
                        results += "Extracurricular: Error; ";
                    }
                    
                    // Ensure the overall score is recalculated
                    if (anySuccess)
                    {
                        try
                        {
                            await RecalculateScore(connection, studentId);
                            _logger.LogInformation($"Successfully recalculated overall score for student {studentId}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error recalculating overall score for student {StudentId}", studentId);
                        }
                    }
                    
                    // Return the result
                    return Json(new { 
                        success = anySuccess, 
                        message = anySuccess ? 
                            $"Scores calculated for student {studentId}: {results}" : 
                            $"Failed to calculate any scores for student {studentId}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating all scores for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpPost]
        [Route("UpdateAttendanceScores")]
        public async Task<IActionResult> UpdateAttendanceScores(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for UpdateAttendanceScores");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Updating attendance-based scores for student {studentId}");
                
                // This method combines seminar score calculation from attendance
                // with academic grades score calculation
                
                // First update the seminar score
                var seminarResult = await CalculateSeminarsScoreFromAttendance(studentId);
                
                // Debug current state of academic score before update
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string getScoreQuery = "SELECT COALESCE(AcademicGradesScore, 0) AS CurrentScore FROM StudentDetails WHERE UserId = @StudentId";
                    decimal currentScore = 0;
                    
                    using (var command = new SqlCommand(getScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            currentScore = Convert.ToDecimal(result);
                            _logger.LogInformation($"Current AcademicGradesScore before update: {currentScore}");
                        }
                        else
                        {
                            _logger.LogWarning($"No current AcademicGradesScore found for student {studentId}");
                        }
                    }
                    
                    // Check if we have any year grades that could contribute to the score
                    string getYearGradesQuery = @"
                        SELECT 
                            COALESCE(FirstYearGrade, 0) as FirstYearGrade,
                            COALESCE(SecondYearGrade, 0) as SecondYearGrade,
                            COALESCE(ThirdYearGrade, 0) as ThirdYearGrade,
                            COALESCE(FourthYearGrade, 0) as FourthYearGrade
                        FROM StudentDetails 
                        WHERE UserId = @StudentId";
                        
                    using (var command = new SqlCommand(getYearGradesQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                decimal firstYearGrade = reader.GetDecimal(reader.GetOrdinal("FirstYearGrade"));
                                decimal secondYearGrade = reader.GetDecimal(reader.GetOrdinal("SecondYearGrade"));
                                decimal thirdYearGrade = reader.GetDecimal(reader.GetOrdinal("ThirdYearGrade"));
                                decimal fourthYearGrade = reader.GetDecimal(reader.GetOrdinal("FourthYearGrade"));
                                
                                _logger.LogInformation($"Year grades found: Y1: {firstYearGrade}, " +
                                                     $"Y2: {secondYearGrade}, " +
                                                     $"Y3: {thirdYearGrade}, " +
                                                     $"Y4: {fourthYearGrade}");
                            }
                            else
                            {
                                _logger.LogWarning($"No year grade data found for student {studentId}");
                            }
                        }
                    }
                }
                
                // Update academic grades score
                var academicResult = await CalculateAcademicGradesScore(studentId);
                
                // Check if the update was successful
                bool academicSuccess = false;
                if (academicResult is JsonResult jsonResult && 
                    jsonResult.Value is System.Dynamic.ExpandoObject expandoObj)
                {
                    IDictionary<string, object> resultDict = expandoObj as IDictionary<string, object>;
                    if (resultDict != null && resultDict.ContainsKey("success"))
                    {
                        academicSuccess = Convert.ToBoolean(resultDict["success"]);
                        _logger.LogInformation($"Academic score update result: {academicSuccess}, Message: {resultDict["message"]}");
                    }
                }
                
                // Update the overall student score
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    await RecalculateScore(connection, studentId);
                    
                    // Debug final state of academic score after update
                    string getScoreQuery = "SELECT COALESCE(AcademicGradesScore, 0) AS UpdatedScore FROM StudentDetails WHERE UserId = @StudentId";
                    decimal updatedScore = 0;
                    
                    using (var command = new SqlCommand(getScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            updatedScore = Convert.ToDecimal(result);
                            _logger.LogInformation($"Updated AcademicGradesScore after update: {updatedScore}");
                        }
                    }
                }
                
                return Json(new { 
                    success = true, 
                    message = $"Attendance and academic scores updated successfully for student {studentId}." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating attendance scores for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("CalculateAcademicGradesScore")]
        public async Task<IActionResult> CalculateAcademicGradesScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateAcademicGradesScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating academic grades score for student {studentId}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                        // We'll keep using studentId for queries, but we have resolvedUserId if needed
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        // Try with the resolved UserId as a last resort
                        string checkResolvedIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @ResolvedId";
                        using (var command = new SqlCommand(checkResolvedIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@ResolvedId", resolvedUserId);
                            int count = (int)await command.ExecuteScalarAsync();
                            if (count > 0)
                            {
                                studentExists = true;
                                studentId = resolvedUserId; // Use resolved ID for subsequent queries
                                _logger.LogInformation($"Using resolved UserId {resolvedUserId} for academic grade calculations");
                            }
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Ensure the StudentDetails table has an AcademicGradesScore column
                    if (!await ColumnExists(connection, "StudentDetails", "AcademicGradesScore"))
                    {
                        _logger.LogWarning("AcademicGradesScore column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            "ALTER TABLE StudentDetails ADD AcademicGradesScore decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created AcademicGradesScore column with default value 0");
                        }
                    }

                    // Get the student's test scores from the StudentDetails table
                    decimal firstYearScore = 0;
                    decimal secondYearScore = 0;
                    decimal thirdYearScore = 0;
                    decimal fourthYearScore = 0;
                    
                    string getTestScoresQuery = @"
                        SELECT 
                            COALESCE(FirstYearGrade, 0) as FirstYearGrade,
                            COALESCE(SecondYearGrade, 0) as SecondYearGrade,
                            COALESCE(ThirdYearGrade, 0) as ThirdYearGrade,
                            COALESCE(FourthYearGrade, 0) as FourthYearGrade
                        FROM StudentDetails 
                        WHERE UserId = @StudentId";
                    
                    // First try with direct student ID
                    using (var command = new SqlCommand(getTestScoresQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Check if all the required columns exist
                                bool hasFirstYearGrade = reader.GetOrdinal("FirstYearGrade") >= 0;
                                bool hasSecondYearGrade = reader.GetOrdinal("SecondYearGrade") >= 0;
                                bool hasThirdYearGrade = reader.GetOrdinal("ThirdYearGrade") >= 0;
                                bool hasFourthYearGrade = reader.GetOrdinal("FourthYearGrade") >= 0;
                                
                                if (hasFirstYearGrade) firstYearScore = reader.GetDecimal(reader.GetOrdinal("FirstYearGrade"));
                                if (hasSecondYearGrade) secondYearScore = reader.GetDecimal(reader.GetOrdinal("SecondYearGrade"));
                                if (hasThirdYearGrade) thirdYearScore = reader.GetDecimal(reader.GetOrdinal("ThirdYearGrade"));
                                if (hasFourthYearGrade) fourthYearScore = reader.GetDecimal(reader.GetOrdinal("FourthYearGrade"));
                            }
                        }
                    }
                    
                    // If no data found, try again with IdNumber query
                    if ((firstYearScore == 0 && secondYearScore == 0 && thirdYearScore == 0 && fourthYearScore == 0))
                    {
                        string getScoresByIdQuery = @"
                            SELECT 
                                COALESCE(FirstYearGrade, 0) as FirstYearGrade,
                                COALESCE(SecondYearGrade, 0) as SecondYearGrade,
                                COALESCE(ThirdYearGrade, 0) as ThirdYearGrade,
                                COALESCE(FourthYearGrade, 0) as FourthYearGrade
                            FROM StudentDetails 
                            WHERE IdNumber = @StudentId";
                        
                        using (var command = new SqlCommand(getScoresByIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // We found the student with IdNumber
                                    _logger.LogInformation($"Found grade data using IdNumber {studentId}");
                                    
                                    bool hasFirstYearGrade = reader.GetOrdinal("FirstYearGrade") >= 0;
                                    bool hasSecondYearGrade = reader.GetOrdinal("SecondYearGrade") >= 0;
                                    bool hasThirdYearGrade = reader.GetOrdinal("ThirdYearGrade") >= 0;
                                    bool hasFourthYearGrade = reader.GetOrdinal("FourthYearGrade") >= 0;
                                    
                                    if (hasFirstYearGrade) firstYearScore = reader.GetDecimal(reader.GetOrdinal("FirstYearGrade"));
                                    if (hasSecondYearGrade) secondYearScore = reader.GetDecimal(reader.GetOrdinal("SecondYearGrade"));
                                    if (hasThirdYearGrade) thirdYearScore = reader.GetDecimal(reader.GetOrdinal("ThirdYearGrade"));
                                    if (hasFourthYearGrade) fourthYearScore = reader.GetDecimal(reader.GetOrdinal("FourthYearGrade"));
                                }
                            }
                        }
                    }
                    
                    // Calculate the academic grades score
                    decimal academicScore = 0;
                    
                    // Use the correct formula: (sum of all year grades / 4) * 30%
                    decimal totalScore = firstYearScore + secondYearScore + thirdYearScore + fourthYearScore;
                    decimal averageScore = totalScore / 4; // Average of all grades
                    academicScore = averageScore * 0.3m; // Apply 30% weighting
                    
                    _logger.LogInformation($"Student {studentId}: First year: {firstYearScore}, Second year: {secondYearScore}, " +
                                          $"Third year: {thirdYearScore}, Fourth year: {fourthYearScore}, " +
                                          $"Total: {totalScore}, Average: {averageScore}, Academic score (30%): {academicScore}");
                    
                    // Update the StudentDetails table - try with all possible ID variants
                    bool updated = false;
                    
                    // First try with UserId
                    string updateQuery = "UPDATE StudentDetails SET AcademicGradesScore = @AcademicScore WHERE UserId = @StudentId";
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@AcademicScore", academicScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated AcademicGradesScore for student with UserId {studentId}: {academicScore}");
                        }
                    }
                    
                    // If not updated, try with IdNumber
                    if (!updated)
                    {
                        string updateByIdQuery = "UPDATE StudentDetails SET AcademicGradesScore = @AcademicScore WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(updateByIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@AcademicScore", academicScore);
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            
                            if (rowsAffected > 0)
                            {
                                updated = true;
                                _logger.LogInformation($"Updated AcademicGradesScore by IdNumber for student {studentId}: {academicScore}");
                            }
                        }
                    }
                    
                    // If both failed and we have a resolved ID, try that
                    if (!updated && !string.IsNullOrEmpty(resolvedUserId) && resolvedUserId != studentId)
                    {
                        string updateByResolvedIdQuery = "UPDATE StudentDetails SET AcademicGradesScore = @AcademicScore WHERE UserId = @ResolvedId";
                        using (var command = new SqlCommand(updateByResolvedIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@AcademicScore", academicScore);
                            command.Parameters.AddWithValue("@ResolvedId", resolvedUserId);
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            
                            if (rowsAffected > 0)
                            {
                                updated = true;
                                _logger.LogInformation($"Updated AcademicGradesScore using resolved UserId {resolvedUserId}: {academicScore}");
                            }
                        }
                    }
                    
                    if (!updated)
                    {
                        _logger.LogWarning($"Failed to update AcademicGradesScore for student {studentId}. No matching record found.");
                        return Json(new { success = false, message = "Failed to update academic score." });
                    }
                    
                    // Update the overall student score
                    await RecalculateScore(connection, studentId);
                    
                    return Json(new { 
                        success = true, 
                        message = $"Academic grades score updated to {academicScore} based on {totalScore} year grades.",
                        academicScore = academicScore
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating academic grades score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("AddTestScore")]
        public async Task<IActionResult> AddTestScore(string studentId, int year, decimal score)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for AddTestScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                if (year < 1 || year > 4)
                {
                    _logger.LogError($"Invalid year value: {year}. Must be between 1 and 4.");
                    return Json(new { success = false, message = "Year must be between 1 and 4." });
                }

                if (score < 0 || score > 10)
                {
                    _logger.LogError($"Invalid score value: {score}. Must be between 0 and 10.");
                    return Json(new { success = false, message = "Score must be between 0 and 10." });
                }

                _logger.LogInformation($"Adding test score for student {studentId}, year {year}, score {score}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if student exists
                    if (!await StudentExists(connection, studentId))
                    {
                        _logger.LogWarning($"Student {studentId} not found in database");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    string yearColumn = "";
                    
                    switch (year)
                    {
                        case 1:
                            yearColumn = "FirstYearGrade";
                            break;
                        case 2:
                            yearColumn = "SecondYearGrade";
                            break;
                        case 3:
                            yearColumn = "ThirdYearGrade";
                            break;
                        case 4:
                            yearColumn = "FourthYearGrade";
                            break;
                    }

                    // Ensure columns exist
                    if (!await ColumnExists(connection, "StudentDetails", yearColumn))
                    {
                        _logger.LogWarning($"{yearColumn} column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            $"ALTER TABLE StudentDetails ADD {yearColumn} decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Created {yearColumn} column with default value 0");
                        }
                    }

                    // Update the test score
                    string updateQuery = $"UPDATE StudentDetails SET {yearColumn} = @Score WHERE UserId = @StudentId";
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Score", score);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        _logger.LogInformation($"Updated {yearColumn} for student {studentId} to {score} (affected rows: {rowsAffected})");
                        
                        // If no rows were affected, try using IdNumber
                        if (rowsAffected == 0)
                        {
                            string updateByIdQuery = $"UPDATE StudentDetails SET {yearColumn} = @Score WHERE IdNumber = @StudentId";
                            using (var idCommand = new SqlCommand(updateByIdQuery, connection))
                            {
                                idCommand.Parameters.AddWithValue("@Score", score);
                                idCommand.Parameters.AddWithValue("@StudentId", studentId);
                                rowsAffected = await idCommand.ExecuteNonQueryAsync();
                                
                                _logger.LogInformation($"Updated {yearColumn} by IdNumber for student {studentId} to {score} (affected rows: {rowsAffected})");
                            }
                        }
                    }
                    
                    // Recalculate academic score
                    var academicResult = await CalculateAcademicGradesScore(studentId);
                    
                    return Json(new { 
                        success = true, 
                        message = $"Test score for year {year} updated to {score} and academic score recalculated." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding test score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Recalculate the overall score for the student
        private async Task RecalculateScore(SqlConnection connection, string studentId)
        {
            try
            {
                _logger.LogInformation($"Starting to recalculate overall score for student {studentId}");
                
                // First, ensure all required columns exist
                await EnsureScoreColumnsExist(connection);
                
                // Get all score components from StudentDetails with correct column names
                string query = @"
                    SELECT 
                        COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                        COALESCE(ExtracurricularScore, 0) as ExtracurricularScore, 
                        COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                        COALESCE(MasteryScore, 0) as MasteryScore,
                        COALESCE(CompletedChallengesScore, 0) as CompletedChallengesScore
                    FROM StudentDetails 
                    WHERE UserId = @StudentId";
                
                decimal academicScore = 0;
                decimal extracurricularScore = 0;
                decimal seminarScore = 0;
                decimal masteryScore = 0;
                decimal challengesScore = 0;
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            academicScore = Convert.ToDecimal(reader["AcademicGradesScore"]);
                            extracurricularScore = Convert.ToDecimal(reader["ExtracurricularScore"]);
                            seminarScore = Convert.ToDecimal(reader["SeminarsWebinarsScore"]);
                            masteryScore = Convert.ToDecimal(reader["MasteryScore"]);
                            challengesScore = Convert.ToDecimal(reader["CompletedChallengesScore"]);
                        }
                    }
                }
                
                // Calculate overall score with appropriate weightings:
                // Each component score already has its respective weighting applied:
                // - AcademicGradesScore: already has 30% weighting
                // - ExtracurricularScore: already has 20% weighting
                // - SeminarsWebinarsScore: already has 10% weighting
                // - MasteryScore: already has 20% weighting
                // - CompletedChallengesScore: already has 20% weighting
                decimal overallScore = 
                    academicScore +
                    extracurricularScore +
                    seminarScore +
                    masteryScore +
                    challengesScore;
                
                _logger.LogInformation($"Overall score calculation: Academic({academicScore}) + " +
                                      $"Extracurricular({extracurricularScore}) + " +
                                      $"Seminars({seminarScore}) + " +
                                      $"Mastery({masteryScore}) + " +
                                      $"Challenges({challengesScore}) = {overallScore}");
                
                // Update the overall score
                bool updated = false;
                
                // First try with UserId
                string updateQuery = "UPDATE StudentDetails SET Score = @Score WHERE UserId = @StudentId";
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@Score", overallScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        updated = true;
                        _logger.LogInformation($"Updated overall score for student with UserId {studentId} to {overallScore}");
                    }
                }
                
                // If not updated, try with IdNumber
                if (!updated)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET Score = @Score WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Score", overallScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated overall score for student with IdNumber {studentId} to {overallScore}");
                        }
                    }
                }
                
                if (!updated)
                {
                    _logger.LogWarning($"Failed to update overall score for student {studentId}. No matching record found.");
                }
                else
                {
                    _logger.LogInformation($"Successfully recalculated overall score for student {studentId}: {overallScore}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating overall score for student {StudentId}", studentId);
                // Don't rethrow to prevent cascading failures
            }
        }
        
        // Ensure all required score columns exist in the database
        private async Task EnsureScoreColumnsExist(SqlConnection connection)
        {
            try
            {
                // Check and create AcademicGradesScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "AcademicGradesScore"))
                {
                    _logger.LogWarning("AcademicGradesScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD AcademicGradesScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created AcademicGradesScore column");
                    }
                }
                
                // Check and create ExtracurricularScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "ExtracurricularScore"))
                {
                    _logger.LogWarning("ExtracurricularScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD ExtracurricularScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created ExtracurricularScore column");
                    }
                }
                
                // Check and create SeminarsWebinarsScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "SeminarsWebinarsScore"))
                {
                    _logger.LogWarning("SeminarsWebinarsScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD SeminarsWebinarsScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created SeminarsWebinarsScore column");
                    }
                }
                
                // Check and create MasteryScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "MasteryScore"))
                {
                    _logger.LogWarning("MasteryScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD MasteryScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created MasteryScore column");
                    }
                }
                
                // Check and create CompletedChallengesScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "CompletedChallengesScore"))
                {
                    _logger.LogWarning("CompletedChallengesScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD CompletedChallengesScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created CompletedChallengesScore column");
                    }
                }
                
                // Check and create Score column if needed
                if (!await ColumnExists(connection, "StudentDetails", "Score"))
                {
                    _logger.LogWarning("Score column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD Score decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created Score column");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring score columns exist");
                // Continue execution despite errors to allow partial functionality
            }
        }

        // Helper method to check if a table exists
        private async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                int count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
        }

        // Helper method to check if a column exists in a table
        private async Task<bool> ColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                int count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
        }

        // Helper method to check if a student exists
        private async Task<bool> StudentExists(SqlConnection connection, string studentId)
        {
            string query = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @StudentId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                int count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
        }

        // Helper method to get row count with a specific value
        private async Task<int> GetRowCount(SqlConnection connection, string tableName, string columnName, string value)
        {
            string query = $"SELECT COUNT(*) FROM {tableName} WHERE {columnName} = @Value";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Value", value);
                return (int)await command.ExecuteScalarAsync();
            }
        }

        [HttpGet]
        [Route("GetStudentScoreBreakdown")]
        public async Task<IActionResult> GetStudentScoreBreakdown(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for GetStudentScoreBreakdown");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Getting score breakdown for student {studentId}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if student exists
                    if (!await StudentExists(connection, studentId))
                    {
                        _logger.LogWarning($"Student {studentId} not found in database");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Get the student's scores from StudentDetails
                    decimal academicScore = 0;
                    decimal extracurricularScore = 0;
                    decimal seminarScore = 0;
                    decimal masteryScore = 0;
                    decimal challengesScore = 0;
                    decimal overallScore = 0;
                    
                    string query = @"
                        SELECT 
                            COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                            COALESCE(ExtracurricularScore, 0) as ExtracurricularScore, 
                            COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                            COALESCE(MasteryScore, 0) as MasteryScore,
                            COALESCE(CompletedChallengesScore, 0) as CompletedChallengesScore,
                            COALESCE(Score, 0) as Score
                        FROM StudentDetails 
                        WHERE UserId = @StudentId";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Check if all the required columns exist
                                bool hasAcademicScore = reader.GetOrdinal("AcademicGradesScore") >= 0;
                                bool hasExtracurricularScore = reader.GetOrdinal("ExtracurricularScore") >= 0;
                                bool hasSeminarScore = reader.GetOrdinal("SeminarsWebinarsScore") >= 0;
                                bool hasMasteryScore = reader.GetOrdinal("MasteryScore") >= 0;
                                bool hasChallengesScore = reader.GetOrdinal("CompletedChallengesScore") >= 0;
                                bool hasOverallScore = reader.GetOrdinal("Score") >= 0;
                                
                                if (hasAcademicScore) academicScore = reader.GetDecimal(reader.GetOrdinal("AcademicGradesScore"));
                                if (hasExtracurricularScore) extracurricularScore = reader.GetDecimal(reader.GetOrdinal("ExtracurricularScore"));
                                if (hasSeminarScore) seminarScore = reader.GetDecimal(reader.GetOrdinal("SeminarsWebinarsScore"));
                                if (hasMasteryScore) masteryScore = reader.GetDecimal(reader.GetOrdinal("MasteryScore"));
                                if (hasChallengesScore) challengesScore = reader.GetDecimal(reader.GetOrdinal("CompletedChallengesScore"));
                                if (hasOverallScore) overallScore = reader.GetDecimal(reader.GetOrdinal("Score"));
                            }
                        }
                    }
                    
                    // Calculate the weighted contribution of each component to the overall score
                    decimal academicWeighted = academicScore * 0.3m;
                    decimal extracurricularWeighted = extracurricularScore * 0.2m;
                    decimal seminarWeighted = seminarScore * 0.1m;
                    decimal masteryWeighted = masteryScore * 0.2m;
                    decimal challengesWeighted = challengesScore * 0.2m;
                    
                    return Json(new { 
                        success = true,
                        studentId = studentId,
                        scores = new {
                            academic = new {
                                raw = academicScore,
                                weighted = academicWeighted,
                                percentage = academicScore * 10, // Convert to percentage (0-100)
                                weight = 30
                            },
                            extracurricular = new {
                                raw = extracurricularScore,
                                weighted = extracurricularWeighted,
                                percentage = extracurricularScore * 10,
                                weight = 20
                            },
                            seminars = new {
                                raw = seminarScore,
                                weighted = seminarWeighted,
                                percentage = seminarScore * 10,
                                weight = 10
                            },
                            mastery = new {
                                raw = masteryScore,
                                weighted = masteryWeighted,
                                percentage = masteryScore * 10,
                                weight = 20
                            },
                            challenges = new {
                                raw = challengesScore,
                                weighted = challengesWeighted,
                                percentage = challengesScore * 10,
                                weight = 20
                            }
                        },
                        overall = new {
                            score = overallScore,
                            percentage = overallScore * 10
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting score breakdown for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("UpdateSeminarsScore")]
        public async Task<IActionResult> UpdateSeminarsScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for UpdateSeminarsScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Updating seminar score for student {studentId}");
                
                // Just call our existing method to calculate seminar score from attendance
                return await CalculateSeminarsScoreFromAttendance(studentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating seminar score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("UpdateExtracurricularScore")]
        public async Task<IActionResult> UpdateExtracurricularScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for UpdateExtracurricularScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Updating extracurricular score for student {studentId}");
                
                // Call our existing method to calculate extracurricular score from activities
                return await CalculateExtracurricularScoreFromActivities(studentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating extracurricular score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to update the seminar score value with proper error handling
        private async Task<bool> UpdateSeminarScoreValue(SqlConnection connection, string studentId, decimal seminarScore)
        {
            try
            {
                bool updated = false;
                
                // First try to update by UserId
                string updateQuery = "UPDATE StudentDetails SET SeminarsWebinarsScore = @SeminarScore WHERE UserId = @StudentId";
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        updated = true;
                        _logger.LogInformation($"Updated SeminarsWebinarsScore to {seminarScore} for student with UserId {studentId}");
                    }
                }
                
                // If no rows were affected, try using IdNumber
                if (!updated)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET SeminarsWebinarsScore = @SeminarScore WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated SeminarsWebinarsScore to {seminarScore} for student with IdNumber {studentId}");
                        }
                    }
                }
                
                if (!updated)
                {
                    _logger.LogWarning($"Failed to update SeminarsWebinarsScore for student {studentId}. No matching record found.");
                }
                
                return updated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating SeminarsWebinarsScore for student {studentId}");
                return false;
            }
        }

        // Helper method to update the extracurricular score value with proper error handling
        private async Task<bool> UpdateExtracurricularScoreValue(SqlConnection connection, string studentId, decimal extracurricularScore)
        {
            try
            {
                bool updated = false;
                
                // First try to update by UserId
                string updateQuery = "UPDATE StudentDetails SET ExtracurricularScore = @ExtracurricularScore WHERE UserId = @StudentId";
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@ExtracurricularScore", extracurricularScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        updated = true;
                        _logger.LogInformation($"Updated ExtracurricularScore to {extracurricularScore} for student with UserId {studentId}");
                    }
                }
                
                // If no rows were affected, try using IdNumber
                if (!updated)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET ExtracurricularScore = @ExtracurricularScore WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ExtracurricularScore", extracurricularScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated ExtracurricularScore to {extracurricularScore} for student with IdNumber {studentId}");
                        }
                    }
                }
                
                if (!updated)
                {
                    _logger.LogWarning($"Failed to update ExtracurricularScore for student {studentId}. No matching record found.");
                }
                
                return updated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating ExtracurricularScore for student {studentId}");
                return false;
            }
        }
    }
}
