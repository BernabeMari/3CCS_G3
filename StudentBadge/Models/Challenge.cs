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
        public string YearLevel { get; set; } // School year in format "2023-2024"
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? LastUpdatedDate { get; set; }
        
        public DateTime? VisibleFromDate { get; set; }
        
        public DateTime? ExpirationDate { get; set; }
        
        public bool IsActive { get; set; }
        
        // Navigation property for questions - initialize to empty list
        public List<ChallengeQuestion> Questions { get; set; } = new List<ChallengeQuestion>();
    }
} 