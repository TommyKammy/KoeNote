using System.Text.RegularExpressions;

namespace KoeNote.App.Services.Transcript;

public static class LlmOutputSanitizerProfiles
{
    public const string None = "none";
    public const string XmlThinkTags = "xmlThinkTags";
    public const string LeadingReasoning = "leadingReasoning";
    public const string MarkdownSectionOnly = "markdownSectionOnly";
    public const string Strict = "strict";

    public static string ForReviewModel(string? modelId)
    {
        return ForReviewModel(modelId, catalogProfile: null);
    }

    public static string ForReviewModel(string? modelId, string? catalogProfile)
    {
        if (!string.IsNullOrWhiteSpace(catalogProfile))
        {
            return catalogProfile;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            return MarkdownSectionOnly;
        }

        return modelId switch
        {
            var id when id.Contains("bonsai", StringComparison.OrdinalIgnoreCase) => Strict,
            var id when id.Contains("gemma", StringComparison.OrdinalIgnoreCase) => MarkdownSectionOnly,
            var id when id.Contains("llm-jp", StringComparison.OrdinalIgnoreCase) => MarkdownSectionOnly,
            _ => MarkdownSectionOnly
        };
    }
}

public static class LlmOutputSanitizer
{
    private static readonly Regex CompleteThinkBlockRegex = new(
        @"<think\b[^>]*>.*?</think>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex ThinkTagRegex = new(
        @"</?think\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] DefaultMarkdownAnchors =
    [
        "## Overview",
        "## Summary",
        "## Key Points",
        "## Key points",
        "## Decisions",
        "## Action Items",
        "## Action items",
        "## Open Questions",
        "## Open questions",
        "## Keywords",
        "## 概要",
        "## 主な内容",
        "## 決定事項",
        "## アクション項目",
        "## 未解決の質問",
        "## キーワード"
    ];

    private static readonly Regex MarkdownHeadingRegex = new(
        @"(?im)^[ \t]*(##\s+(Overview|Summary|Key Points|Key points|Decisions|Action Items|Action items|Open Questions|Open questions|Keywords|概要|主な内容|決定事項|アクション項目|未解決の質問|キーワード))\b",
        RegexOptions.CultureInvariant);

    public static string SanitizeMarkdown(string output, string? profile)
    {
        var text = StripStopMarkers(StripCodeFence(output));
        var normalizedProfile = string.IsNullOrWhiteSpace(profile)
            ? LlmOutputSanitizerProfiles.None
            : profile;

        if (normalizedProfile.Equals(LlmOutputSanitizerProfiles.None, StringComparison.OrdinalIgnoreCase))
        {
            return text.Trim();
        }

        if (normalizedProfile.Equals(LlmOutputSanitizerProfiles.XmlThinkTags, StringComparison.OrdinalIgnoreCase))
        {
            return StripThink(text, dropUnclosedThinkWhenNoAnchor: false).Trim();
        }

        if (normalizedProfile.Equals(LlmOutputSanitizerProfiles.LeadingReasoning, StringComparison.OrdinalIgnoreCase))
        {
            return KeepFromFirstMarkdownAnchor(text).Trim();
        }

        if (normalizedProfile.Equals(LlmOutputSanitizerProfiles.MarkdownSectionOnly, StringComparison.OrdinalIgnoreCase))
        {
            return KeepFromFirstMarkdownAnchor(StripThink(text, dropUnclosedThinkWhenNoAnchor: false)).Trim();
        }

        if (normalizedProfile.Equals(LlmOutputSanitizerProfiles.Strict, StringComparison.OrdinalIgnoreCase))
        {
            return KeepFromFirstMarkdownAnchor(StripThink(text, dropUnclosedThinkWhenNoAnchor: true)).Trim();
        }

        return text.Trim();
    }

    public static string SanitizeJsonCandidate(string output, string? profile)
    {
        var text = StripCodeFence(output);
        if (string.IsNullOrWhiteSpace(profile) ||
            profile.Equals(LlmOutputSanitizerProfiles.None, StringComparison.OrdinalIgnoreCase))
        {
            return text.Trim();
        }

        return StripThink(text, dropUnclosedThinkWhenNoAnchor: false).Trim();
    }

    private static string StripCodeFence(string output)
    {
        var text = (output ?? string.Empty).Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstLineEnd = text.IndexOf('\n');
        if (firstLineEnd >= 0)
        {
            text = text[(firstLineEnd + 1)..];
        }

        var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
        if (fenceIndex >= 0)
        {
            text = text[..fenceIndex];
        }

        return text.Trim();
    }

    private static string StripStopMarkers(string output)
    {
        return (output ?? string.Empty)
            .Replace("[end of text]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string StripThink(string text, bool dropUnclosedThinkWhenNoAnchor)
    {
        var withoutCompleteBlocks = CompleteThinkBlockRegex.Replace(text, string.Empty).Trim();
        var openThinkIndex = withoutCompleteBlocks.IndexOf("<think", StringComparison.OrdinalIgnoreCase);
        if (openThinkIndex >= 0)
        {
            var markdownAnchorIndex = FindFirstMarkdownAnchor(withoutCompleteBlocks, openThinkIndex);
            if (markdownAnchorIndex >= 0)
            {
                withoutCompleteBlocks = withoutCompleteBlocks[markdownAnchorIndex..];
            }
            else if (dropUnclosedThinkWhenNoAnchor)
            {
                withoutCompleteBlocks = withoutCompleteBlocks[..openThinkIndex];
            }
        }

        return ThinkTagRegex.Replace(withoutCompleteBlocks, string.Empty).Trim();
    }

    private static string KeepFromFirstMarkdownAnchor(string text)
    {
        var anchorIndex = FindFirstMarkdownAnchor(text);
        return anchorIndex >= 0 ? text[anchorIndex..] : text;
    }

    private static int FindFirstMarkdownAnchor(string text, int startIndex = 0)
    {
        foreach (Match match in MarkdownHeadingRegex.Matches(text))
        {
            if (match.Index >= startIndex)
            {
                return match.Index;
            }
        }

        return -1;
    }
}
