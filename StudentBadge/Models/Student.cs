namespace StudentBadge.Models
{
    public class Student
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string IdNumber { get; set; }
        public string Course { get; set; }
        public string Section { get; set; }
        public int Score { get; set; }
        public int Achievements { get; set; } // New property for achievements
        public int Comments { get; set; } // New property for comments
        public string BadgeColor { get; set; } // New property for badge color
        public bool IsProfileVisible { get; set; }
    }
}

