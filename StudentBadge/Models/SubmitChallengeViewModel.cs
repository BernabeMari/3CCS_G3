using System.Collections.Generic;

namespace StudentBadge.Models
{
    public class SubmitChallengeViewModel
    {
        public int ChallengeId { get; set; }
        public string StudentId { get; set; }
        public Dictionary<int, string> Answers { get; set; } = new Dictionary<int, string>();
        public string ChallengeName { get; set; }
    }
} 