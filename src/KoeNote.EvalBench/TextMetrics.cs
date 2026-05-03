namespace KoeNote.EvalBench;

public static class TextMetrics
{
    public static double CharacterErrorRate(string reference, string hypothesis)
    {
        if (reference.Length == 0)
        {
            return hypothesis.Length == 0 ? 0 : 1;
        }

        return EditDistance(reference, hypothesis) / (double)reference.Length;
    }

    public static int EditDistance(string reference, string hypothesis)
    {
        var previous = new int[hypothesis.Length + 1];
        var current = new int[hypothesis.Length + 1];

        for (var j = 0; j <= hypothesis.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= reference.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= hypothesis.Length; j++)
            {
                var cost = reference[i - 1] == hypothesis[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[hypothesis.Length];
    }
}
