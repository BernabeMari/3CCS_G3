namespace StudentBadge.Models
{
    public class Student
    {
        public string IdNumber { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Course { get; set; }
        public string Section { get; set; }
        public bool IsProfileVisible { get; set; }
        public bool IsResumeVisible { get; set; }
        public decimal Score { get; set; }
        public string Achievements { get; set; }
        public string Comments { get; set; }
        public string BadgeColor { get; set; }

        // Grade properties
        public decimal? FirstYearGrade { get; set; }
        public decimal? SecondYearGrade { get; set; }
        public decimal? ThirdYearGrade { get; set; }
        public decimal? FourthYearGrade { get; set; }
        public decimal? AchievementScore { get; set; }

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

        // Method to calculate overall score based on grades and achievement entries
        public decimal CalculateOverallScore()
        {
            // Check if at least one non-zero grade exists
            bool hasGrades = (FirstYearGrade.HasValue && FirstYearGrade.Value > 0) || 
                             (SecondYearGrade.HasValue && SecondYearGrade.Value > 0) || 
                             (ThirdYearGrade.HasValue && ThirdYearGrade.Value > 0) || 
                             (FourthYearGrade.HasValue && FourthYearGrade.Value > 0);

            if (!hasGrades)
            {
                // If no valid grades, return 0
                return 0;
            }

            // Calculate grade average from available non-zero grades
            int gradeCount = 0;
            decimal gradeSum = 0;

            if (FirstYearGrade.HasValue && FirstYearGrade.Value > 0)
            {
                gradeSum += FirstYearGrade.Value;
                gradeCount++;
            }

            if (SecondYearGrade.HasValue && SecondYearGrade.Value > 0)
            {
                gradeSum += SecondYearGrade.Value;
                gradeCount++;
            }

            if (ThirdYearGrade.HasValue && ThirdYearGrade.Value > 0)
            {
                gradeSum += ThirdYearGrade.Value;
                gradeCount++;
            }

            if (FourthYearGrade.HasValue && FourthYearGrade.Value > 0)
            {
                gradeSum += FourthYearGrade.Value;
                gradeCount++;
            }

            // Calculate base score (100% from grades)
            decimal baseScore = gradeCount > 0 ? gradeSum / gradeCount : 0;
            
            // Count achievements and add bonus (0.10 per achievement)
            decimal achievementBonus = 0;
            if (!string.IsNullOrEmpty(Achievements))
            {
                // Split by pipe to get each achievement entry
                string[] achievementEntries = Achievements.Split('|', StringSplitOptions.RemoveEmptyEntries);
                
                // Add 0.10 for each valid achievement entry
                foreach (var entry in achievementEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entry))
                    {
                        achievementBonus += 0.10m;
                    }
                }
            }
            
            // Add achievement bonus to base score
            decimal finalScore = baseScore + achievementBonus;
            
            // Cap the score at 100
            if (finalScore > 100)
            {
                finalScore = 100;
            }
            
            // Round to 2 decimal places
            return Math.Round(finalScore, 2);
        }
    }
}