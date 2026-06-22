namespace KoeNote.Updater;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Any(static arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(UpdaterOptions.HelpText);
                return (int)UpdaterExitCode.Success;
            }

            var options = UpdaterOptions.Parse(args);
            await using var progressReporter = WpfUpdaterProgressReporter.CreateIfInteractive();
            var service = new UpdaterService(new SystemUpdaterProcessRunner(), progressReporter);
            return (int)await service.ExecuteAsync(options);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)UpdaterExitCode.InvalidArguments;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)UpdaterExitCode.UnexpectedFailure;
        }
    }
}
