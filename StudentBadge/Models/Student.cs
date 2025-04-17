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
        private decimal _score;
        public decimal Score 
        { 
            get 
            {
                // Return the sum of all category scores
                return AcademicGradesScore + CompletedChallengesScore + 
                       MasteryScore + SeminarsWebinarsScore + ExtracurricularScore;
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
            if (Score >= 95)
            {
                BadgeName = "Platinum Scholar";
                BadgeDescription = "Outstanding academic & achievement record";
                BadgeColor = "platinum";
            }
            else if (Score >= 85)
            {
                BadgeName = "Gold Achiever";
                BadgeDescription = "Excellent performance in both academics & achievements";
                BadgeColor = "gold";
            }
            else if (Score >= 75)
            {
                BadgeName = "Silver Performer";
                BadgeDescription = "Strong academic foundation with notable achievements";
                BadgeColor = "silver";
            }
            else if (Score >= 65)
            {
                BadgeName = "Bronze Learner";
                BadgeDescription = "Decent academic performance with some achievements";
                BadgeColor = "bronze";
            }
            else if (Score >= 50)
            {
                BadgeName = "Rising Star";
                BadgeDescription = "Needs improvement but shows potential";
                BadgeColor = "rising-star";
            }
            else
            {
                BadgeName = "Needs Improvement";
                BadgeDescription = "At risk, requires academic support";
                BadgeColor = "warning";
            }
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
            // Initialize scores for each category
            AcademicGradesScore = 0;
            CompletedChallengesScore = 0;
            MasteryScore = 0;
            SeminarsWebinarsScore = 0;
            ExtracurricularScore = 0;
            
            // Calculate Academic Grades Score (30% of total)
            CalculateAcademicGradesScore();
            
            // Calculate Completed Challenges Score (20% of total)
            CalculateChallengesScore();
            
            // Calculate Mastery Score (20% of total)
            CalculateMasteryScore();
            
            // Calculate Seminars & Webinars Score (10% of total)
            CalculateSeminarsScore();
            
            // Calculate Extracurricular Activities Score (20% of total)
            CalculateExtracurricularScore();
            
            // Return the sum of all category scores (Score property will calculate this automatically)
            return AcademicGradesScore + CompletedChallengesScore + 
                   MasteryScore + SeminarsWebinarsScore + ExtracurricularScore;
        }
        
        // Calculate Academic Grades Score (30% of total)
        private void CalculateAcademicGradesScore()
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

            // Calculate average grade percentage (out of 100)
            decimal gradePercentage = (totalPointsEarned / (yearCount * 100)) * 100;
            
            // Apply weight (30%)
            AcademicGradesScore = gradePercentage * 0.3m;
        }
        
        // Calculate Completed Challenges Score (20% of total)
        private void CalculateChallengesScore()
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
            
            // Calculate category score: (total score / total items of ALL challenges) * 100 * 0.2
            CompletedChallengesScore = (totalPointsEarned / TotalAvailableChallengePoints) * 100 * 0.2m;
        }
        
        // Calculate Mastery Score (20% of total)
        private void CalculateMasteryScore()
        {
            if (TotalMasteryItemsAllYears <= 0)
            {
                MasteryScore = 0;
                return;
            }
            
            // Formula: (total scores from ALL year levels 1-4 / total items from ALL year levels 1-4) * 100 * 0.2
            // This ensures that even a 1st year student's score is calculated as a percentage of ALL tests from year 1-4
            MasteryScore = (TotalMasteryScoreAllYears / TotalMasteryItemsAllYears) * 100 * 0.2m;
        }
        
        // Calculate Seminars & Webinars Score (10% of total)
        private void CalculateSeminarsScore()
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
            percentageEarned = Math.Min(seminarCount * POINTS_PER_SEMINAR, 100.0m);
            
            // Apply weight (10%)
            SeminarsWebinarsScore = percentageEarned * 0.1m;
        }
        
        // Calculate Extracurricular Activities Score (20% of total)
        private void CalculateExtracurricularScore()
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
            percentageEarned = Math.Min(activityCount * POINTS_PER_ACTIVITY, 100.0m);
            
            // Apply weight (20%)
            ExtracurricularScore = percentageEarned * 0.2m;
        }
    }
}