namespace KoeNote.App.Models;

public sealed record StatusItem(string Name, string Value, bool IsOk, string Detail = "");
