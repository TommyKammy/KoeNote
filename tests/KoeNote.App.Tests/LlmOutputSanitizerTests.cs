using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlmOutputSanitizerTests
{
    [Fact]
    public void Strict_RemovesThinkBlockAndKeepsMarkdownSummary()
    {
        var output = """
            <think>
            I should explain my reasoning first.
            </think>
            ## Overview

            - The speaker explains language information.
            """;

        var sanitized = LlmOutputSanitizer.SanitizeMarkdown(output, LlmOutputSanitizerProfiles.Strict);

        Assert.DoesNotContain("<think", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reasoning", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("## Overview", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_DropsUnclosedThinkWhenNoMarkdownSectionExists()
    {
        var output = """
            <think>
            The model is still reasoning and never produced the requested answer.
            """;

        var sanitized = LlmOutputSanitizer.SanitizeMarkdown(output, LlmOutputSanitizerProfiles.Strict);

        Assert.True(string.IsNullOrWhiteSpace(sanitized));
    }

    [Fact]
    public void MarkdownSectionOnly_RemovesLeadingExplanation()
    {
        var output = """
            Okay, I will summarize the transcript.

            ## Summary

            - The source mentions one topic.
            """;

        var sanitized = LlmOutputSanitizer.SanitizeMarkdown(output, LlmOutputSanitizerProfiles.MarkdownSectionOnly);

        Assert.StartsWith("## Summary", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("Okay, I will", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_IgnoresInlineQuotedHeadingInstruction()
    {
        var output = """
            Begin with "## Overview".

            I will analyze the transcript first.

            ## Overview

            - The source mentions one topic.
            """;

        var sanitized = LlmOutputSanitizer.SanitizeMarkdown(output, LlmOutputSanitizerProfiles.Strict);

        Assert.StartsWith("## Overview", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("I will analyze", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_KeepsJapaneseMarkdownHeading()
    {
        var output = """
            <think>
            まず内容を確認します。
            </think>
            ## 概要

            - 日本語の要約です。
            """;

        var sanitized = LlmOutputSanitizer.SanitizeMarkdown(output, LlmOutputSanitizerProfiles.Strict);

        Assert.StartsWith("## 概要", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("<think", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeJsonCandidate_RemovesThinkBlockBeforeJsonExtraction()
    {
        var output = """
            <think>Prepare JSON only.</think>
            [{"segment_id":"000001","issue_type":"grammar","original_text":"ni wo","suggested_text":"wo","reason":"duplicate particle","confidence":0.9}]
            """;

        var sanitized = LlmOutputSanitizer.SanitizeJsonCandidate(output, LlmOutputSanitizerProfiles.Strict);

        Assert.StartsWith("[{", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("<think", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForReviewModel_PrefersCatalogProfile()
    {
        var profile = LlmOutputSanitizerProfiles.ForReviewModel(
            "unknown-review-model",
            LlmOutputSanitizerProfiles.Strict);

        Assert.Equal(LlmOutputSanitizerProfiles.Strict, profile);
    }
}
