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
        
        public int Score { get; set; }
        public string Achievements { get; set; }
        public string Comments { get; set; }
        public string BadgeColor { get; set; }
    }
}