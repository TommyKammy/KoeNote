namespace KoeNote.App.Services.Review;

public sealed class ReviewWorkerException : Exception
{
    public ReviewWorkerException(ReviewFailureCategory category, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
    }

    public ReviewFailureCategory Category { get; }
}
