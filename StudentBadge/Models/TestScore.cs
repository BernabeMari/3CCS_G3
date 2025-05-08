namespace StudentBadge.Models
{
    public class TestScore
    {
        public int TestId { get; set; }
        public int SubmissionId { get; set; }
        public int EarnedPoints { get; set; }
        public int TotalPoints { get; set; }
        
        // Calculate percentage
        public int Percentage => TotalPoints > 0 ? (EarnedPoints * 100) / TotalPoints : 0;
    }
} 