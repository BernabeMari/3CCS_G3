using System;
using System.Collections.Generic;

namespace StudentBadge.Models
{
    public class StudentViewModel
    {
        public string IdNumber { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string Course { get; set; }
        public string Section { get; set; }
        public int Score { get; set; }
        public string Achievements { get; set; }
        public string Comments { get; set; }
        public string BadgeColor { get; set; }
        public string BadgeName { get; set; }
        public bool IsProfileVisible { get; set; }
        public bool IsResumeVisible { get; set; }
        public string ProfilePicturePath { get; set; }
        public string ResumePath { get; set; }
        
        // These properties help with display
        public string FullCourse => $"{Course} - {Section}";
        public string BadgeDisplayName => GetBadgeDisplayName();
        public string BadgeColorClass => GetBadgeColorClass();
        
        private string GetBadgeDisplayName()
        {
            return BadgeColor?.ToLower() switch
            {
                "platinum" => "Platinum",
                "gold" => "Gold",
                "silver" => "Silver",
                "bronze" => "Bronze",
                "green" => "Leaf",
                "red" => "Starting", 
                _ => "Unknown"
            };
        }
        
        private string GetBadgeColorClass()
        {
            return BadgeColor?.ToLower() switch
            {
                "platinum" => "badge-platinum",
                "gold" => "badge-gold",
                "silver" => "badge-silver",
                "bronze" => "badge-bronze",
                "green" => "badge-green",
                "red" => "badge-red",
                _ => "badge-light"
            };
        }
        
        // Helper method to create a StudentViewModel from a database record
        public static StudentViewModel FromDatabaseRecord(dynamic record)
        {
            if (record == null) return null;
            
            return new StudentViewModel
            {
                IdNumber = record["IdNumber"]?.ToString() ?? "",
                FullName = record["FullName"]?.ToString() ?? "",
                Username = record["Username"]?.ToString() ?? "",
                Course = record["Course"]?.ToString() ?? "",
                Section = record["Section"]?.ToString() ?? "",
                Score = record["Score"] != DBNull.Value ? Convert.ToInt32(record["Score"]) : 0,
                Achievements = record["Achievements"]?.ToString() ?? "",
                Comments = record["Comments"]?.ToString() ?? "",
                BadgeColor = record["BadgeColor"]?.ToString() ?? "green",
                IsProfileVisible = record["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(record["IsProfileVisible"]),
                IsResumeVisible = record["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(record["IsResumeVisible"]),
                ProfilePicturePath = record["ProfilePicturePath"]?.ToString(),
                ResumePath = record["ResumeFileName"]?.ToString()
            };
        }
    }
} 