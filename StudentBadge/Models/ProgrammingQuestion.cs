using System;
using System.ComponentModel.DataAnnotations;

namespace StudentBadge.Models
{
    public class ProgrammingQuestion
    {
        public int QuestionId { get; set; }
        
        [Required]
        public int TestId { get; set; }
        
        [Required]
        public string QuestionText { get; set; } = string.Empty;
        
        [Required]
        public string AnswerText { get; set; } = string.Empty;
        
        // Make CodeSnippet optional
        public string CodeSnippet { get; set; } = string.Empty;
        
        [Required]
        [Range(1, 100)]
        public int Points { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? LastUpdatedDate { get; set; }
        
        // Navigation property for the test (not required for form submission)
        public ProgrammingTest? Test { get; set; }
    }
} 