using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DMotion.Editor.UnityControllerBridge.Core
{
    /// <summary>
    /// Comprehensive report generated after Unity AnimatorController conversion.
    /// Provides detailed analytics, feature usage, and recommendations.
    /// </summary>
    public class ConversionReport
    {
        public ConversionResult Result { get; set; }
        public ConversionStatistics Statistics { get; set; } = new();
        public FeatureUsageInfo FeatureUsage { get; set; } = new();
        public List<ConversionRecommendation> Recommendations { get; set; } = new();

        /// <summary>
        /// Generates an HTML report with styling and detailed breakdown.
        /// </summary>
        public string ExportToHtml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"    <title>DMotion Conversion Report - {Result.ControllerName}</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine(GetHtmlStyles());
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine($"    <h1>DMotion Conversion Report</h1>");
            sb.AppendLine($"    <h2>{Result.ControllerName}</h2>");

            // Summary section
            sb.AppendLine("    <div class=\"section\">");
            sb.AppendLine("        <h3>Summary</h3>");
            sb.AppendLine($"        <p class=\"{(Result.Success ? "success" : "error")}\">");
            sb.AppendLine($"            Status: {(Result.Success ? "✓ Success" : "✗ Failed")}");
            sb.AppendLine("        </p>");
            sb.AppendLine("    </div>");

            // Statistics section
            sb.AppendLine("    <div class=\"section\">");
            sb.AppendLine("        <h3>Statistics</h3>");
            sb.AppendLine("        <table>");
            sb.AppendLine($"            <tr><td>States Converted</td><td>{Statistics.StatesConverted}</td></tr>");
            sb.AppendLine($"            <tr><td>Transitions Created</td><td>{Statistics.TransitionsCreated}</td></tr>");
            sb.AppendLine($"            <tr><td>Parameters Converted</td><td>{Statistics.ParametersConverted}</td></tr>");
            sb.AppendLine($"            <tr><td>Animation Clips</td><td>{Statistics.AnimationClipsUsed}</td></tr>");
            sb.AppendLine($"            <tr><td>Blend Trees</td><td>{Statistics.BlendTreesConverted}</td></tr>");
            sb.AppendLine($"            <tr><td>Sub-State Machines</td><td>{Statistics.SubStateMachinesConverted}</td></tr>");
            sb.AppendLine($"            <tr><td>Any State Transitions</td><td>{Statistics.AnyStateTransitions}</td></tr>");
            sb.AppendLine("        </table>");
            sb.AppendLine("    </div>");

            // Feature usage section
            sb.AppendLine("    <div class=\"section\">");
            sb.AppendLine("        <h3>Feature Comparison</h3>");
            sb.AppendLine("        <table>");
            sb.AppendLine("            <tr><th>Feature</th><th>Unity</th><th>DMotion</th><th>Status</th></tr>");

            foreach (var feature in FeatureUsage.Features)
            {
                string statusClass = feature.ConversionStatus switch
                {
                    ConversionStatus.Supported => "success",
                    ConversionStatus.PartiallySupported => "warning",
                    ConversionStatus.NotSupported => "error",
                    ConversionStatus.NotUsed => "neutral",
                    _ => "neutral"
                };

                string statusIcon = feature.ConversionStatus switch
                {
                    ConversionStatus.Supported => "✓",
                    ConversionStatus.PartiallySupported => "⚠",
                    ConversionStatus.NotSupported => "✗",
                    ConversionStatus.NotUsed => "-",
                    _ => "-"
                };

                sb.AppendLine($"            <tr>");
                sb.AppendLine($"                <td>{feature.FeatureName}</td>");
                sb.AppendLine($"                <td>{(feature.UsedInUnity ? "Used" : "-")}</td>");
                sb.AppendLine($"                <td>{feature.DMotionEquivalent}</td>");
                sb.AppendLine($"                <td class=\"{statusClass}\">{statusIcon} {feature.ConversionStatus}</td>");
                sb.AppendLine($"            </tr>");
            }

            sb.AppendLine("        </table>");
            sb.AppendLine("    </div>");

            // Warnings section
            if (Result.Success && Statistics.WarningsCount > 0)
            {
                sb.AppendLine("    <div class=\"section\">");
                sb.AppendLine("        <h3>Warnings</h3>");
                sb.AppendLine("        <ul class=\"warnings\">");

                var warnings = FeatureUsage.Features
                    .Where(f => f.ConversionStatus == ConversionStatus.PartiallySupported ||
                                f.ConversionStatus == ConversionStatus.NotSupported)
                    .ToList();

                foreach (var warning in warnings)
                {
                    sb.AppendLine($"            <li>{warning.FeatureName}: {warning.Notes}</li>");
                }

                sb.AppendLine("        </ul>");
                sb.AppendLine("    </div>");
            }

            // Recommendations section
            if (Recommendations.Count > 0)
            {
                sb.AppendLine("    <div class=\"section\">");
                sb.AppendLine("        <h3>Recommendations</h3>");
                sb.AppendLine("        <ul class=\"recommendations\">");

                foreach (var rec in Recommendations)
                {
                    string priorityClass = rec.Priority switch
                    {
                        RecommendationPriority.High => "priority-high",
                        RecommendationPriority.Medium => "priority-medium",
                        RecommendationPriority.Low => "priority-low",
                        _ => ""
                    };

                    sb.AppendLine($"            <li class=\"{priorityClass}\">");
                    sb.AppendLine($"                <strong>{rec.Title}</strong><br/>");
                    sb.AppendLine($"                {rec.Description}");
                    sb.AppendLine($"            </li>");
                }

                sb.AppendLine("        </ul>");
                sb.AppendLine("    </div>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a Markdown report for documentation or version control.
        /// </summary>
        public string ExportToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# DMotion Conversion Report");
            sb.AppendLine();
            sb.AppendLine($"**Controller**: {Result.ControllerName}");
            sb.AppendLine($"**Status**: {(Result.Success ? "✓ Success" : "✗ Failed")}");
            sb.AppendLine();

            // Statistics
            sb.AppendLine("## Statistics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Count |");
            sb.AppendLine("|--------|-------|");
            sb.AppendLine($"| States Converted | {Statistics.StatesConverted} |");
            sb.AppendLine($"| Transitions Created | {Statistics.TransitionsCreated} |");
            sb.AppendLine($"| Parameters Converted | {Statistics.ParametersConverted} |");
            sb.AppendLine($"| Animation Clips | {Statistics.AnimationClipsUsed} |");
            sb.AppendLine($"| Blend Trees | {Statistics.BlendTreesConverted} |");
            sb.AppendLine($"| Sub-State Machines | {Statistics.SubStateMachinesConverted} |");
            sb.AppendLine($"| Any State Transitions | {Statistics.AnyStateTransitions} |");
            sb.AppendLine();

            // Feature comparison
            sb.AppendLine("## Feature Comparison");
            sb.AppendLine();
            sb.AppendLine("| Feature | Unity | DMotion | Status |");
            sb.AppendLine("|---------|-------|---------|--------|");

            foreach (var feature in FeatureUsage.Features)
            {
                string statusIcon = feature.ConversionStatus switch
                {
                    ConversionStatus.Supported => "✓",
                    ConversionStatus.PartiallySupported => "⚠",
                    ConversionStatus.NotSupported => "✗",
                    ConversionStatus.NotUsed => "-",
                    _ => "-"
                };

                sb.AppendLine($"| {feature.FeatureName} | {(feature.UsedInUnity ? "Yes" : "-")} | {feature.DMotionEquivalent} | {statusIcon} {feature.ConversionStatus} |");
            }
            sb.AppendLine();

            // Warnings
            if (Statistics.WarningsCount > 0)
            {
                sb.AppendLine("## Warnings");
                sb.AppendLine();

                var warnings = FeatureUsage.Features
                    .Where(f => f.ConversionStatus == ConversionStatus.PartiallySupported ||
                                f.ConversionStatus == ConversionStatus.NotSupported)
                    .ToList();

                foreach (var warning in warnings)
                {
                    sb.AppendLine($"- **{warning.FeatureName}**: {warning.Notes}");
                }
                sb.AppendLine();
            }

            // Recommendations
            if (Recommendations.Count > 0)
            {
                sb.AppendLine("## Recommendations");
                sb.AppendLine();

                foreach (var rec in Recommendations)
                {
                    string priorityBadge = rec.Priority switch
                    {
                        RecommendationPriority.High => "🔴 HIGH",
                        RecommendationPriority.Medium => "🟡 MEDIUM",
                        RecommendationPriority.Low => "🟢 LOW",
                        _ => ""
                    };

                    sb.AppendLine($"### {priorityBadge} {rec.Title}");
                    sb.AppendLine();
                    sb.AppendLine(rec.Description);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private string GetHtmlStyles()
        {
            return @"
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
            max-width: 1000px;
            margin: 40px auto;
            padding: 20px;
            background: #f5f5f5;
            color: #333;
        }
        h1 {
            color: #2c3e50;
            border-bottom: 3px solid #3498db;
            padding-bottom: 10px;
        }
        h2 {
            color: #555;
            margin-top: 0;
        }
        h3 {
            color: #34495e;
            margin-top: 0;
        }
        .section {
            background: white;
            padding: 20px;
            margin: 20px 0;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        table {
            width: 100%;
            border-collapse: collapse;
            margin: 10px 0;
        }
        th, td {
            padding: 12px;
            text-align: left;
            border-bottom: 1px solid #ddd;
        }
        th {
            background: #34495e;
            color: white;
            font-weight: bold;
        }
        tr:hover {
            background: #f8f9fa;
        }
        .success {
            color: #27ae60;
            font-weight: bold;
        }
        .error {
            color: #e74c3c;
            font-weight: bold;
        }
        .warning {
            color: #f39c12;
            font-weight: bold;
        }
        .neutral {
            color: #95a5a6;
        }
        .warnings, .recommendations {
            list-style: none;
            padding: 0;
        }
        .warnings li, .recommendations li {
            padding: 10px;
            margin: 10px 0;
            border-left: 4px solid #f39c12;
            background: #fff9e6;
        }
        .recommendations li {
            border-left-color: #3498db;
            background: #e8f4f8;
        }
        .priority-high {
            border-left-color: #e74c3c !important;
            background: #fdecea !important;
        }
        .priority-medium {
            border-left-color: #f39c12 !important;
            background: #fff9e6 !important;
        }
        .priority-low {
            border-left-color: #27ae60 !important;
            background: #e8f8f0 !important;
        }";
        }
    }

    /// <summary>
    /// Statistics about the conversion process.
    /// </summary>
    public class ConversionStatistics
    {
        public int StatesConverted { get; set; }
        public int TransitionsCreated { get; set; }
        public int ParametersConverted { get; set; }
        public int AnimationClipsUsed { get; set; }
        public int BlendTreesConverted { get; set; }
        public int SubStateMachinesConverted { get; set; }
        public int AnyStateTransitions { get; set; }
        public int WarningsCount { get; set; }
        public int ErrorsCount { get; set; }
    }

    /// <summary>
    /// Tracks which Unity features were used and their conversion status.
    /// </summary>
    public class FeatureUsageInfo
    {
        public List<FeatureConversionInfo> Features { get; set; } = new();

        public void AddFeature(string name, bool usedInUnity, string dmotionEquivalent,
            ConversionStatus status, string notes = "")
        {
            Features.Add(new FeatureConversionInfo
            {
                FeatureName = name,
                UsedInUnity = usedInUnity,
                DMotionEquivalent = dmotionEquivalent,
                ConversionStatus = status,
                Notes = notes
            });
        }
    }

    /// <summary>
    /// Information about a single feature's conversion.
    /// </summary>
    public class FeatureConversionInfo
    {
        public string FeatureName { get; set; }
        public bool UsedInUnity { get; set; }
        public string DMotionEquivalent { get; set; }
        public ConversionStatus ConversionStatus { get; set; }
        public string Notes { get; set; }
    }

    public enum ConversionStatus
    {
        Supported,          // Full support
        PartiallySupported, // Some limitations
        NotSupported,       // Feature skipped
        NotUsed             // Feature not present in Unity controller
    }

    /// <summary>
    /// Recommendations for improving the conversion or DMotion setup.
    /// </summary>
    public class ConversionRecommendation
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public RecommendationPriority Priority { get; set; }
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High
    }
}
