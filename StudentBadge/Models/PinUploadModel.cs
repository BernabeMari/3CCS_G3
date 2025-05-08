using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace StudentBadge.Models
{
    public class PinUploadModel
    {
        [Required(ErrorMessage = "Please select a file to upload")]
        public IFormFile? SpreadsheetFile { get; set; }
        
        [Required]
        [Range(1, 365, ErrorMessage = "Expiry days must be between 1 and 365")]
        public int ExpiryDays { get; set; } = 30;
        
        [Required(ErrorMessage = "Please specify a user type")]
        public string? UserType { get; set; } // "Student" or "Teacher"
    }
    
    public class PinGenerationResult
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Pin { get; set; }
        public bool EmailSent { get; set; }
        public string? ErrorMessage { get; set; }
    }
} 