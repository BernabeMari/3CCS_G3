using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace StudentBadge.Models
{
    public class Student
    {
        public string IdNumber { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string Course { get; set; }
        public string Section { get; set; }
        public bool IsProfileVisible { get; set; }
        public bool IsResumeVisible { get; set; }
        public int YearLevel { get; set; }
        public string PhotoUrl { get; set; }
        public string Address { get; set; }
        public string ContactNumber { get; set; }
        public string GuardianName { get; set; }
        public string GuardianContact { get; set; }
        public DateTime? CreatedDate { get; set; }
        public bool IsTransferee { get; set; } = false;
        public string PreviousSchool { get; set; }
        private decimal _score;
        public decimal Score 
        { 
            get 
            {
                try
                {
                    // Get weights from the database
                    Dictionary<string, decimal> weights = GetAllCategoryWeights();
                    
                    // Calculate weighted scores for each category
                    decimal academicWeighted = AcademicGradesScore * (weights["AcademicGrades"] / 100.0m);
                    decimal challengesWeighted = CompletedChallengesScore * (weights["CompletedChallenges"] / 100.0m);
                    decimal masteryWeighted = MasteryScore * (weights["Mastery"] / 100.0m);
                    decimal seminarsWeighted = SeminarsWebinarsScore * (weights["SeminarsWebinars"] / 100.0m);
                    decimal extracurricularWeighted = ExtracurricularScore * (weights["Extracurricular"] / 100.0m);
                    
                    // Return the sum of all weighted category scores
                    return academicWeighted + challengesWeighted + masteryWeighted + 
                           seminarsWeighted + extracurricularWeighted;
                }
                catch (Exception ex)
                {
                    // If anything fails, return the stored score or 0
                    Console.WriteLine($"Error calculating score: {ex.Message}");
                    return _score;
                }
            }
            set
            {
                _score = value;
            }
        }
        public string Achievements { get; set; }
        public string Comments { get; set; }
        public string BadgeColor { get; set; }

        // Grade properties
        public decimal? FirstYearGrade { get; set; }
        public decimal? SecondYearGrade { get; set; }
        public decimal? ThirdYearGrade { get; set; }
        public decimal? FourthYearGrade { get; set; }
        public decimal? AchievementScore { get; set; }
        public int? GradeLevel { get; set; }

        // New properties for scoring categories
        public decimal AcademicGradesScore { get; set; }
        public decimal CompletedChallengesScore { get; set; }
        public decimal MasteryScore { get; set; }
        public decimal SeminarsWebinarsScore { get; set; }
        public decimal ExtracurricularScore { get; set; }
        
        // New properties for challenge score calculation
        public List<ChallengeSubmission> CompletedChallenges { get; set; }
        public int TotalAvailableChallenges { get; set; }
        public decimal TotalAvailableChallengePoints { get; set; }
        public int TotalAvailableMasteryCertifications { get; set; }
        public decimal TotalMasteryScoreAllYears { get; set; } // Student's total test score from year levels 1-4
        public decimal TotalMasteryItemsAllYears { get; set; } // Total test items from year levels 1-4

        // New properties for Badge
        public string BadgeName { get; set; }
        public string BadgeDescription { get; set; }

        // Store profile picture directly in the database
        public byte[] ProfilePictureData { get; set; }
        public string ProfilePictureContentType { get; set; }
        
        // Store resume directly in the database
        public byte[] ResumeData { get; set; }
        public string ResumeContentType { get; set; }
        public string ResumeFileName { get; set; }
        
        // Keep these fields for backward compatibility during transition
        public string ProfilePicturePath { get; set; }
        public string ResumePath { get; set; }
        
        // Method to assign badge based on score
        public void AssignBadge()
        {
            // Set badge name and description based on score
            if (Score >= 95)
            {
                BadgeName = "Platinum Scholar";
                BadgeDescription = "Outstanding academic & achievement record";
            }
            else if (Score >= 85)
            {
                BadgeName = "Gold Achiever";
                BadgeDescription = "Excellent performance in both academics & achievements";
            }
            else if (Score >= 75)
            {
                BadgeName = "Silver Performer";
                BadgeDescription = "Strong academic foundation with notable achievements";
            }
            else if (Score >= 65)
            {
                BadgeName = "Bronze Learner";
                BadgeDescription = "Decent academic performance with some achievements";
            }
            else if (Score >= 50)
            {
                BadgeName = "Rising Star";
                BadgeDescription = "Needs improvement but shows potential";
            }
            else
            {
                BadgeName = "Needs Improvement";
                BadgeDescription = "At risk, requires academic support";
            }
            
            // Set badge color using the consistent pattern
            BadgeColor = Score >= 95 ? "platinum" : 
                         Score >= 85 ? "gold" : 
                         Score >= 75 ? "silver" : 
                         Score >= 65 ? "bronze" : 
                         Score >= 50 ? "rising-star" : 
                         Score >= 1 ? "needs" : "none";
        }

        // Method to calculate grade level based on available grades
        public int CalculateGradeLevel()
        {
            // If the student has a value in FourthYearGrade, they're graduated (level 5)
            if (FourthYearGrade.HasValue && FourthYearGrade.Value > 0)
            {
                return 5; // Graduated
            }
            // If the student has a value in ThirdYearGrade, they're in 4th year
            else if (ThirdYearGrade.HasValue && ThirdYearGrade.Value > 0)
            {
                return 4; // 4th year
            }
            // If the student has a value in SecondYearGrade, they're in 3rd year
            else if (SecondYearGrade.HasValue && SecondYearGrade.Value > 0)
            {
                return 3; // 3rd year
            }
            // If the student has a value in FirstYearGrade, they're in 2nd year
            else if (FirstYearGrade.HasValue && FirstYearGrade.Value > 0)
            {
                return 2; // 2nd year
            }
            // If the student has no grades, assign as 1st year student
            else
            {
                return 1; // 1st year
            }
        }

        // Method to calculate overall score based on grades and achievement entries
        public decimal CalculateOverallScore()
        {
            // Get all category weights at once to avoid multiple database calls
            Dictionary<string, decimal> weights = GetAllCategoryWeights();
            
            // Initialize scores for each category
            AcademicGradesScore = 0;
            CompletedChallengesScore = 0;
            MasteryScore = 0;
            SeminarsWebinarsScore = 0;
            ExtracurricularScore = 0;
            
            // Calculate raw scores for each category (0-100 scale)
            CalculateRawAcademicGradesScore();
            CalculateRawChallengesScore();
            CalculateRawMasteryScore();
            CalculateRawSeminarsScore();
            CalculateRawExtracurricularScore();
            
            // Apply weights to raw scores
            AcademicGradesScore = AcademicGradesScore * (weights["AcademicGrades"] / 100.0m);
            CompletedChallengesScore = CompletedChallengesScore * (weights["CompletedChallenges"] / 100.0m);
            MasteryScore = MasteryScore * (weights["Mastery"] / 100.0m);
            SeminarsWebinarsScore = SeminarsWebinarsScore * (weights["SeminarsWebinars"] / 100.0m);
            ExtracurricularScore = ExtracurricularScore * (weights["Extracurricular"] / 100.0m);
            
            // Return the sum of all weighted category scores (Score property will calculate this automatically)
            return AcademicGradesScore + CompletedChallengesScore + 
                   MasteryScore + SeminarsWebinarsScore + ExtracurricularScore;
        }
        
        // Calculate Raw Academic Grades Score (0-100 scale, without applying weight)
        private void CalculateRawAcademicGradesScore()
        {
            // Check if at least one non-zero grade exists
            bool hasGrades = (FirstYearGrade.HasValue && FirstYearGrade.Value > 0) || 
                             (SecondYearGrade.HasValue && SecondYearGrade.Value > 0) || 
                             (ThirdYearGrade.HasValue && ThirdYearGrade.Value > 0) || 
                             (FourthYearGrade.HasValue && FourthYearGrade.Value > 0);

            if (!hasGrades)
            {
                AcademicGradesScore = 0;
                return;
            }

            // Calculate total points earned (sum of all grades)
            decimal totalPointsEarned = 0;
            int yearCount = 0;

            if (FirstYearGrade.HasValue)
            {
                totalPointsEarned += FirstYearGrade.Value;
                yearCount++;
            }

            if (SecondYearGrade.HasValue)
            {
                totalPointsEarned += SecondYearGrade.Value;
                yearCount++;
            }

            if (ThirdYearGrade.HasValue)
            {
                totalPointsEarned += ThirdYearGrade.Value;
                yearCount++;
            }

            if (FourthYearGrade.HasValue)
            {
                totalPointsEarned += FourthYearGrade.Value;
                yearCount++;
            }

            // Calculate average grade (0-100 scale)
            AcademicGradesScore = (totalPointsEarned / (yearCount * 100)) * 100;
        }
        
        // Calculate Raw Completed Challenges Score (0-100 scale, without applying weight)
        private void CalculateRawChallengesScore()
        {
            if (CompletedChallenges == null || TotalAvailableChallengePoints <= 0)
            {
                CompletedChallengesScore = 0;
                return;
            }
            
            // Calculate total points earned from challenges
            decimal totalPointsEarned = 0;
            
            foreach (var challenge in CompletedChallenges)
            {
                totalPointsEarned += challenge.PointsEarned;
            }
            
            // Calculate score as percentage of available points (0-100 scale)
            CompletedChallengesScore = (totalPointsEarned / TotalAvailableChallengePoints) * 100;
        }
        
        // Calculate Raw Mastery Score (0-100 scale, without applying weight)
        private void CalculateRawMasteryScore()
        {
            if (TotalMasteryItemsAllYears <= 0)
            {
                MasteryScore = 0;
                return;
            }
            
            // Calculate score as percentage of mastery items (0-100 scale)
            MasteryScore = (TotalMasteryScoreAllYears / TotalMasteryItemsAllYears) * 100;
        }
        
        // Calculate Raw Seminars & Webinars Score (0-100 scale, without applying weight)
        private void CalculateRawSeminarsScore()
        {
            // For this implementation, each seminar/webinar attendance gives 10% toward the total score
            const decimal POINTS_PER_SEMINAR = 10.0m;
            
            // Start with 0% and add 10% for each seminar, up to 100%
            decimal percentageEarned = 0;
            int seminarCount = 0;
            
            if (!string.IsNullOrEmpty(Achievements))
            {
                // Split by pipe to get each achievement entry
                string[] achievementEntries = Achievements.Split('|', StringSplitOptions.RemoveEmptyEntries);
                
                // Look for seminar or webinar achievements
                foreach (var entry in achievementEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entry) && 
                        (entry.Contains("Seminar", StringComparison.OrdinalIgnoreCase) || 
                         entry.Contains("Webinar", StringComparison.OrdinalIgnoreCase) ||
                         entry.Contains("Workshop", StringComparison.OrdinalIgnoreCase) ||
                         entry.Contains("Conference", StringComparison.OrdinalIgnoreCase) ||
                         entry.Contains("Talk", StringComparison.OrdinalIgnoreCase) ||
                         entry.Contains("Lecture", StringComparison.OrdinalIgnoreCase)))
                    {
                        seminarCount++;
                    }
                }
            }
            
            // Calculate percentage: each seminar is worth 10%, up to a maximum of 100%
            SeminarsWebinarsScore = Math.Min(seminarCount * POINTS_PER_SEMINAR, 100.0m);
        }
        
        // Calculate Raw Extracurricular Activities Score (0-100 scale, without applying weight)
        private void CalculateRawExtracurricularScore()
        {
            // For this implementation, each extracurricular activity gives 20% toward the total score
            const decimal POINTS_PER_ACTIVITY = 20.0m;
            
            // Start with 0% and add 20% for each activity, up to 100%
            decimal percentageEarned = 0;
            int activityCount = 0;
            
            if (!string.IsNullOrEmpty(Achievements))
            {
                // Split by pipe to get each achievement entry
                string[] achievementEntries = Achievements.Split('|', StringSplitOptions.RemoveEmptyEntries);
                
                // Count extracurricular activities (those that don't fall in other categories)
                foreach (var entry in achievementEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entry) && 
                        !entry.Contains("Certificate", StringComparison.OrdinalIgnoreCase) && 
                        !entry.Contains("Certification", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Test", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Exam", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Skill", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Mastery", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Seminar", StringComparison.OrdinalIgnoreCase) && 
                        !entry.Contains("Webinar", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Workshop", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Conference", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Talk", StringComparison.OrdinalIgnoreCase) &&
                        !entry.Contains("Lecture", StringComparison.OrdinalIgnoreCase))
                    {
                        activityCount++;
                    }
                }
            }
            
            // Calculate percentage: each activity is worth 20%, up to a maximum of 100%
            ExtracurricularScore = Math.Min(activityCount * POINTS_PER_ACTIVITY, 100.0m);
        }

        // Helper method to get weight for a category from the database
        private decimal GetCategoryWeight(string categoryName)
        {
            // Default weights if database lookup fails
            Dictionary<string, decimal> defaultWeights = new Dictionary<string, decimal>
            {
                { "AcademicGrades", 30.0m },
                { "CompletedChallenges", 20.0m },
                { "Mastery", 20.0m },
                { "SeminarsWebinars", 10.0m },
                { "Extracurricular", 20.0m }
            };
            
            try
            {
                // Try to get connection string from configuration
                // Note: In an actual implementation, you'd inject this through a service
                var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();
                
                string connectionString = configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
                {
                    connection.Open();
                    
                    // Check if ScoreWeights table exists
                    string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ScoreWeights'";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(checkTableQuery, connection))
                    {
                        int tableExists = Convert.ToInt32(cmd.ExecuteScalar());
                        if (tableExists == 0)
                        {
                            // Table doesn't exist, use default weights
                            return defaultWeights[categoryName];
                        }
                    }
                    
                    // Query the weight from database
                    string query = "SELECT Weight FROM ScoreWeights WHERE CategoryName = @CategoryName";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@CategoryName", categoryName);
                        var result = cmd.ExecuteScalar();
                        
                        if (result != null)
                        {
                            return Convert.ToDecimal(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error (in a real-world application)
                Console.WriteLine($"Error getting category weight: {ex.Message}");
            }
            
            // Return default weight if any error or not found
            return defaultWeights[categoryName];
        }

        // Helper method to get all weights at once to avoid multiple database calls
        private Dictionary<string, decimal> GetAllCategoryWeights()
        {
            // Default weights if database lookup fails
            Dictionary<string, decimal> weights = new Dictionary<string, decimal>
            {
                { "AcademicGrades", 30.0m },
                { "CompletedChallenges", 20.0m },
                { "Mastery", 20.0m },
                { "SeminarsWebinars", 10.0m },
                { "Extracurricular", 20.0m }
            };
            
            try
            {
                // Try to get connection string from configuration
                var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();
                
                string connectionString = configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
                {
                    connection.Open();
                    
                    // Check if ScoreWeights table exists
                    string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ScoreWeights'";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(checkTableQuery, connection))
                    {
                        int tableExists = Convert.ToInt32(cmd.ExecuteScalar());
                        if (tableExists == 0)
                        {
                            // Table doesn't exist, use default weights
                            return weights;
                        }
                    }
                    
                    // Query all weights from database at once
                    string query = "SELECT CategoryName, Weight FROM ScoreWeights";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(query, connection))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string categoryName = reader.GetString(0);
                                decimal weight = reader.GetDecimal(1);
                                
                                // Update weight if category exists in our dictionary
                                if (weights.ContainsKey(categoryName))
                                {
                                    weights[categoryName] = weight;
                                }
                            }
                        }
                    }
                    
                    // Ensure weights add up to 100%
                    decimal totalWeight = weights.Values.Sum();
                    if (Math.Abs(totalWeight - 100.0m) > 0.1m)
                    {
                        // Normalize weights
                        foreach (var key in weights.Keys.ToList())
                        {
                            weights[key] = (weights[key] / totalWeight) * 100.0m;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error (in a real-world application)
                Console.WriteLine($"Error getting all category weights: {ex.Message}");
            }
            
            return weights;
        }
    }
}