namespace KoeNote.EvalBench;

public sealed record EvaluationBenchOptions(string OutputRoot, string? AsrManifestPath = null)
{
    public static EvaluationBenchOptions FromArgs(string[] args)
    {
        var outputRoot = Path.Combine(Environment.CurrentDirectory, "experiments", "phase8");
        string? asrManifestPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outputRoot = args[i + 1];
                i++;
            }
            else if (string.Equals(args[i], "--asr-manifest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                asrManifestPath = Path.GetFullPath(args[i + 1]);
                i++;
            }
        }

        return new EvaluationBenchOptions(Path.GetFullPath(outputRoot), asrManifestPath);
    }
}
