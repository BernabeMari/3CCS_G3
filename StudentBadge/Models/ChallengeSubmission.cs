using System;

namespace StudentBadge.Models
{
    public class ChallengeSubmission
    {
        public int SubmissionId { get; set; }
        public int ChallengeId { get; set; }
        public string StudentId { get; set; }
        public DateTime SubmissionDate { get; set; }
        
        // Change the property name but keep the same backing property to maintain compatibility
        public int PercentageScore { get; set; } // This will be populated from the Score column
        
        // These might not be in the database table, but we'll keep them for compatibility
        // with the view which might be using them
        public int PointsEarned { get; set; }
        public int TotalPoints { get; set; }
        
        // Properties for display purposes (not stored in the ChallengeSubmissions table)
        public string ChallengeName { get; set; }
        public string ProgrammingLanguage { get; set; }
        public string Description { get; set; }
    }
} 