using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Midnight.Services
{
    public enum LogLevel { Info, Warning, Error, Success }

    public struct LogEntry
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
    }

    public static class LogService
    {
        public static event Action<LogEntry>? OnLogEntry;

        private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "latest.log");
        private static readonly object _lock = new object();
        private static readonly List<LogEntry> _logHistory = new List<LogEntry>(2000);
        private const int MaxHistorySize = 2000;

        static LogService()
        {
            try { File.WriteAllText(_logFilePath, string.Empty); } catch { }
        }

        public static IReadOnlyList<LogEntry> GetHistory()
        {
            lock (_lock) return new List<LogEntry>(_logHistory);
        }

        public static void Log(string message, LogLevel level = LogLevel.Info, string source = "General")
        {
            var entry = new LogEntry { Time = DateTime.Now, Level = level, Source = source, Message = message };
            string line = $"[{entry.Time:HH:mm:ss}] [{entry.Level.ToString().ToUpper()}] [{entry.Source}] {entry.Message}";

            Debug.WriteLine(line);

            lock (_lock)
            {
                try { File.AppendAllText(_logFilePath, line + Environment.NewLine); } catch { }

                if (_logHistory.Count >= MaxHistorySize)
                    _logHistory.RemoveAt(0);
                _logHistory.Add(entry);
            }

            OnLogEntry?.Invoke(entry);
        }

        public static void Error(string message, string source = "General") =>
            Log(message, LogLevel.Error, source);
    }
}

