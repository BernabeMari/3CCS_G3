using System;

namespace StudentBadge.Models
{
    public class MarkedStudent
    {
        public int Id { get; set; }
        public string EmployerId { get; set; }
        public string StudentId { get; set; }
        public DateTime DateMarked { get; set; }
        public string Notes { get; set; }
        
        // Additional student properties for display
        public string StudentName { get; set; }
        public string Course { get; set; }
        public string Section { get; set; }
        public double Score { get; set; }
        public string BadgeColor { get; set; }
        public string ProfilePicturePath { get; set; }
    }
} 