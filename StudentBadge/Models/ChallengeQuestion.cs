using System;
using System.ComponentModel.DataAnnotations;

namespace StudentBadge.Models
{
    public class ChallengeQuestion
    {
        public int QuestionId { get; set; }
        
        [Required]
        public int ChallengeId { get; set; }
        
        [Required]
        public string QuestionText { get; set; }
        
        [Required]
        public string AnswerText { get; set; }
        
        // Make CodeSnippet optional
        public string CodeSnippet { get; set; }
        
        [Required]
        [Range(1, 100)]
        public int Points { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? LastUpdatedDate { get; set; }
        
        // Navigation property for the challenge (not required for form submission)
        public Challenge Challenge { get; set; }
    }
} 