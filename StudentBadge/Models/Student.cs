namespace StudentBadge.Models
{
    public class Student
    {
        public string IdNumber { get; set; }
        public string FullName { get; set; }
        public string Course { get; set; }
        public string Section { get; set; }
        public bool IsProfileVisible { get; set; }
        public bool IsResumeVisible { get; set; }
        public string ProfilePicturePath { get; set; }
        public string ResumePath { get; set; }
        public int Score { get; set; }
        public string Achievements { get; set; }
        public string Comments { get; set; }
        public string BadgeColor { get; set; }
    }
}