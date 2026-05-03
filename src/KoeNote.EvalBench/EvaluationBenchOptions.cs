namespace KoeNote.EvalBench;

public sealed record EvaluationBenchOptions(string OutputRoot)
{
    public static EvaluationBenchOptions FromArgs(string[] args)
    {
        var outputRoot = Path.Combine(Environment.CurrentDirectory, "experiments", "phase8");
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                outputRoot = args[i + 1];
                i++;
            }
        }

        return new EvaluationBenchOptions(Path.GetFullPath(outputRoot));
    }
}
