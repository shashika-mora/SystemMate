namespace SystemMate.Services;

public static class DiagnosticsService
{
    private static readonly object Gate = new();

    public static void LogException(Exception exception)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemMate", "Logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}]\n{exception}\n\n");
            }
        }
        catch (Exception loggingError) when (loggingError is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(exception);
        }
    }
}
