using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobloxChatLauncher.Models;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.Services
{
    /// <summary>
    ///  Provides functionality for managing and applying message filtering preferences based on predefined policy
    /// thresholds.
    /// </summary>
    /// <remarks>This service determines whether messages should be hidden according to user-selected filter
    /// preferences, such as 'strict', 'default', or 'relaxed'. It validates and normalizes filter preferences, and
    /// applies corresponding policy thresholds to message scores. Intended for internal use within the application to
    /// support local content moderation features.</remarks>
    public class MessageFilterService
    {
        public const string DefaultFilterPreference = "default";
        public static readonly System.Collections.Generic.HashSet<string> validFilterPreferences = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "strict",
            "default",
            "relaxed"
        };

        public static PolicyScoresDto? ParsePolicyScores(JsonNode? node)
        {
            if (node == null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PolicyScoresDto>(node.ToJsonString());
            }
            catch
            {
                return null;
            }
        }

        public static bool ShouldHideMessageByFilter(PolicyScoresDto scores)
        {
            string preference = GetCurrentFilterPreference();
            FilterPresetThresholds thresholds = preference switch
            {
                "strict" => new FilterPresetThresholds
                {
                    Toxicity = 0.60,
                    Insult = 0.65,
                    Profanity = 0.55,
                    SevereToxicity = 0.30,
                    IdentityAttack = 0.30,
                    Threat = 0.65,
                    SexuallyExplicit = 0.30,
                },
                "relaxed" => new FilterPresetThresholds
                {
                    Toxicity = 0.95,
                    Insult = 0.95,
                    Profanity = 0.95,
                    SevereToxicity = 0.70,
                    IdentityAttack = 0.60,
                    Threat = 0.80,
                    SexuallyExplicit = 0.70,
                },
                _ => new FilterPresetThresholds
                {
                    Toxicity = 0.85,
                    Insult = 0.85,
                    Profanity = 0.70,
                    SevereToxicity = 0.50,
                    IdentityAttack = 0.40,
                    Threat = 0.80,
                    SexuallyExplicit = 0.50,
                },
            };

            return
                GetScoreValue(scores.Toxicity) >= thresholds.Toxicity ||
                GetScoreValue(scores.Insult) >= thresholds.Insult ||
                GetScoreValue(scores.Profanity) >= thresholds.Profanity ||
                GetScoreValue(scores.SevereToxicity) >= thresholds.SevereToxicity ||
                GetScoreValue(scores.IdentityAttack) >= thresholds.IdentityAttack ||
                GetScoreValue(scores.Threat) >= thresholds.Threat ||
                GetScoreValue(scores.SexuallyExplicit) >= thresholds.SexuallyExplicit;
        }

        private static double GetScoreValue(PerspectiveAttributeScore? score)
        {
            return score?.SummaryScore?.Value ?? 0;
        }

        public static string GetCurrentFilterPreference()
        {
            string current = Properties.Settings1.Default.MessageFilterPreference;
            if (string.IsNullOrWhiteSpace(current))
            {
                current = DefaultFilterPreference;
                Properties.Settings1.Default.MessageFilterPreference = current;
                Properties.Settings1.Default.Save();
            }

            string normalized = current.Trim().ToLowerInvariant();
            if (!validFilterPreferences.Contains(normalized))
            {
                normalized = DefaultFilterPreference;
                Properties.Settings1.Default.MessageFilterPreference = normalized;
                Properties.Settings1.Default.Save();
            }

            return normalized;
        }

        private sealed class FilterPresetThresholds
        {
            public double Toxicity
            {
                get; init;
            }
            public double Insult
            {
                get; init;
            }
            public double Profanity
            {
                get; init;
            }
            public double SevereToxicity
            {
                get; init;
            }
            public double IdentityAttack
            {
                get; init;
            }
            public double Threat
            {
                get; init;
            }
            public double SexuallyExplicit
            {
                get; init;
            }
        }
    }
}
