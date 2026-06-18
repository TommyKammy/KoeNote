namespace KoeNote.Updater;

public enum UpdaterExitCode
{
    Success = 0,
    InvalidArguments = 2,
    VerificationFailed = 10,
    InstallFailed = 20,
    RelaunchFailed = 30,
    UnexpectedFailure = 99
}
