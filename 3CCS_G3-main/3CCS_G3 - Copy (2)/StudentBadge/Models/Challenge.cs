using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StudentBadge.Models
{
    public class Challenge
    {
        public int ChallengeId { get; set; }
        
        [Required]
        public string TeacherId { get; set; }
        
        [Required]
        public string ChallengeName { get; set; }
        
        [Required]
        public string ProgrammingLanguage { get; set; }
        
        public string Description { get; set; }
        
        [Required]
        public int YearLevel { get; set; } // 1, 2, 3, or 4 for the different year levels
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? LastUpdatedDate { get; set; }
        
        public bool IsActive { get; set; }
        
        // Navigation property for questions - initialize to empty list
        public List<ChallengeQuestion> Questions { get; set; } = new List<ChallengeQuestion>();
    }
} 