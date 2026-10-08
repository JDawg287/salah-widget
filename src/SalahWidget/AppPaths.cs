namespace SalahWidget;

internal static class AppPaths
{
    public static readonly string DataDir = CreateDataDir();

    private static string CreateDataDir()
    {
        // For a packaged app, LocalApplicationData is redirected into the package's private store.
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SalahWidget");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(AppPaths.DataDir, "widget.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 512 * 1024)
                    info.Delete();
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break the widget.
        }
    }
}
