using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class TranscriptSummaryValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsEmptyOutput(string content)
    {
        var result = TranscriptSummaryValidator.Validate(content, "markdown_non_empty", requireStructuredSections: false);

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<think>analysis</think>\n## Overview")]
    [InlineData("```markdown\n## Overview\n```")]
    [InlineData("Task:\n- Do not add facts\n## Overview")]
    [InlineData("[end of text]")]
    public void Validate_RejectsChatterCodeFenceAndPromptEcho(string content)
    {
        var result = TranscriptSummaryValidator.Validate(content, "markdown_non_empty", requireStructuredSections: false);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsLowStructureWhenStructuredSectionsAreRequired()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## Overview\n\nOnly one section.",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.False(result.IsValid);
        Assert.Contains("Markdown sections", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsStructuredMarkdown()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## Overview\n\n- Summary.\n\n## Key points\n\n- Point.\n\n## Keywords\n\n- topic",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsStructuredMarkdownWithJapaneseKeywordHeading()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## 概要\n\n- Summary.\n\n## 主な内容\n\n- Point.\n\n## キーワード\n\n- topic",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsStructuredSummaryWithoutKeywords()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## Overview\n\n- Summary.\n\n## Key points\n\n- Point.",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.False(result.IsValid);
        Assert.Contains("Keywords", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsStructuredSummaryWithEmptyKeywords()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## Overview\n\n- Summary.\n\n## Key points\n\n- Point.\n\n## Keywords\n\n",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.False(result.IsValid);
        Assert.Contains("Keywords", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsRepeatedStructuredHeading()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## Key points\n\n- One.\n\n## Key points\n\n- Two.\n\n## Keywords\n\n- topic",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.False(result.IsValid);
        Assert.Contains("repeated", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsIncompleteTrailingBullet()
    {
        var result = TranscriptSummaryValidator.Validate(
            "## Overview\n\n- Summary.\n\n## Key points\n\n- Point.\n\n## Keywords\n\n- 育児の",
            "markdown_summary_sections",
            requireStructuredSections: true);

        Assert.False(result.IsValid);
        Assert.Contains("incomplete", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
