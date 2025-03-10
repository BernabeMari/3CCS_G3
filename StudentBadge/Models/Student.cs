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
        public int Achievements { get; set; }
        public int Comments { get; set; } 
        public string BadgeColor { get; set; } 
        public bool IsProfileVisible { get; set; }

        public byte[] ProfilePicture { get; set; }
    }
}

