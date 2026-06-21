using KoeNote.App.Models;

namespace KoeNote.App.Services.Transcript;

public static class ReadableDocumentBlockSerializer
{
    public static string Serialize(
        IReadOnlyList<ReadableDocumentBlock> blocks,
        IReadOnlyList<string> bodyTexts)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(bodyTexts);
        if (blocks.Count != bodyTexts.Count)
        {
            throw new ArgumentException("The number of body texts must match the number of readable document blocks.", nameof(bodyTexts));
        }

        var renderedBlocks = new List<string>(blocks.Count);
        for (var index = 0; index < blocks.Count; index++)
        {
            var text = NormalizeBodyText(bodyTexts[index]);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            renderedBlocks.Add(RenderBlock(blocks[index], text));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, renderedBlocks).Trim();
    }

    private static string RenderBlock(ReadableDocumentBlock block, string text)
    {
        if (!block.HasMeta)
        {
            return text;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var firstLine = lines.FirstOrDefault() ?? string.Empty;
        var remainingLines = lines
            .Skip(1)
            .Where(static line => !string.IsNullOrWhiteSpace(line));
        var prefix = block.HasTimeRange ? $"[{block.TimeRange}] " : string.Empty;
        if (block.HasSpeaker)
        {
            prefix += $"{block.Speaker}: ";
        }

        var rendered = new List<string> { prefix + firstLine };
        rendered.AddRange(remainingLines);
        return string.Join(Environment.NewLine, rendered).Trim();
    }

    private static string NormalizeBodyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }
}
