using System.Text.Json.Serialization;

namespace RobloxChatLauncher.Models
{
    public class PolicyScoresDto
    {
        [JsonPropertyName("TOXICITY")]
        public PerspectiveAttributeScore? Toxicity { get; set; }

        [JsonPropertyName("INSULT")]
        public PerspectiveAttributeScore? Insult { get; set; }

        [JsonPropertyName("PROFANITY")]
        public PerspectiveAttributeScore? Profanity { get; set; }

        [JsonPropertyName("SEVERE_TOXICITY")]
        public PerspectiveAttributeScore? SevereToxicity { get; set; }

        [JsonPropertyName("IDENTITY_ATTACK")]
        public PerspectiveAttributeScore? IdentityAttack { get; set; }

        [JsonPropertyName("THREAT")]
        public PerspectiveAttributeScore? Threat { get; set; }

        [JsonPropertyName("SEXUALLY_EXPLICIT")]
        public PerspectiveAttributeScore? SexuallyExplicit { get; set; }
    }

    public class PerspectiveAttributeScore
    {
        [JsonPropertyName("summaryScore")]
        public PerspectiveScoreSummary? SummaryScore { get; set; }
    }

    public class PerspectiveScoreSummary
    {
        [JsonPropertyName("value")]
        public double Value { get; set; }
    }
}
