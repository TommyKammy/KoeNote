using System.Text;

namespace KoeNote.App.Services.Jobs;

public static class JobLogDiagnostics
{
    public static string FormatException(string category, Exception exception, string? relatedPath = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        builder.Append(category).Append(": ").Append(exception.Message);
        builder.AppendLine();
        builder.Append("exception_type: ").AppendLine(exception.GetType().FullName);
        if (!string.IsNullOrWhiteSpace(relatedPath))
        {
            builder.Append("related_path: ").AppendLine(relatedPath);
        }

        builder.AppendLine("exception:");
        builder.Append(exception);
        return builder.ToString();
    }
}
