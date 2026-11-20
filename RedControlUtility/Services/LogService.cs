using System.Text;

namespace RedControlUtility.Services;

public static class LogService
{
    private static readonly object SyncLock = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static string LogFilePath => Path.Combine(AppPaths.LogDir, "red_control_utility.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (SyncLock)
            {
                var fi = new FileInfo(LogFilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    string old = LogFilePath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogFilePath, old);
                }

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
