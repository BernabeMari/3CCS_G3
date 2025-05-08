using System;
using System.ComponentModel.DataAnnotations;

namespace StudentBadge.Models
{
    public class StudentProfile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string IdNumber { get; set; }

        [Required]
        public string FullName { get; set; }

        public string Course { get; set; }
        public string Section { get; set; }
        public int Score { get; set; }
        public string BadgeColor { get; set; }

        // Store resume directly in the database as byte array
        public byte[] ResumeData { get; set; }
        public string ResumeContentType { get; set; }
        public string ResumeFileName { get; set; }

        // Store profile picture in the database
        public byte[] ProfilePictureData { get; set; }
        public string ProfilePictureContentType { get; set; }

        // Additional profile information
        public string Skills { get; set; }
        public string Achievements { get; set; }
        public string Experience { get; set; }
        public string Education { get; set; }
        public string Projects { get; set; }
        public string Certifications { get; set; }
        
        public DateTime LastUpdated { get; set; }
        public string Comments { get; set; }
    }
} 