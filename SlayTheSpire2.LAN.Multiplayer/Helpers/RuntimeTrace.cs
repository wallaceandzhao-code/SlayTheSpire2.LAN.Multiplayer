using System.Reflection;
using System.Text;

namespace SlayTheSpire2.LAN.Multiplayer.Helpers
{
    internal static class RuntimeTrace
    {
        private static readonly object SyncRoot = new();
        private static string? _logPath;

        public static void Write(string message)
        {
            try
            {
                var path = GetLogPath();
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (SyncRoot)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Best effort tracing only.
            }
        }

        private static string GetLogPath()
        {
            if (!string.IsNullOrEmpty(_logPath))
                return _logPath;

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory;
            _logPath = Path.Combine(assemblyDirectory, "SlayTheSpire2.LAN.Multiplayer.runtime.log");
            return _logPath;
        }
    }
}
