# Read the file content
$content = Get-Content DashboardController.cs -Raw

# Replace the second ApplyYearLevelBonus method
$content = $content -replace '(?s)    // Helper method to apply year-level bonuses to student scores\r?\n    \[NonAction\]\r?\n    private int ApplyYearLevelBonus\(int baseScore, int gradeLevel\)\r?\n    \{.*?\r?\n        // Apply multiplier to score\r?\n        return \(int\)\(baseScore \* multiplier\);\r?\n    \}', '    // Score-related functionality has been moved to ScoreController'

# Remove the UpdateStudentScoreFromCategories method
$content = $content -replace '(?s)    // Method to recalculate and update a student''s overall score based on category scores\r?\n    \[HttpPost\]\r?\n    public async Task<IActionResult> UpdateStudentScoreFromCategories\(string studentId\).*?catch \(Exception ex\).*?return Json\(new \{ success = false, message = "An error occurred: " \+ ex.Message \}\);\r?\n        \}\r?\n    \}', '    // Method to recalculate and update a student''s overall score
    // Moved to ScoreController.RecalculateScore
    [HttpPost]
    public async Task<IActionResult> UpdateStudentScoreFromCategories(string studentId)
    {
        // Redirect to ScoreController
        _logger.LogInformation($"Redirecting UpdateStudentScoreFromCategories to ScoreController for studentId={studentId}");
        return RedirectToAction("RecalculateScore", "Score", new { studentId });
    }'

# Save the modified content
Set-Content DashboardController.cs $content

Write-Output "Score-related methods have been updated in DashboardController.cs"
