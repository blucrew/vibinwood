using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace RmwHaptics
{
    public enum LogVerbosity { Off = 0, Error = 1, Warning = 2, Info = 3, Verbose = 4 }

    public static class LogCat
    {
        public const string System  = "System ";
        public const string Device  = "Device ";
        public const string Buttplug= "Buttplug";
        public const string XToys   = "XToys  ";
        public const string Event   = "Event  ";
        public const string Patch   = "Patch  ";
    }

    public struct LogEntry
    {
        public DateTime     Time;
        public LogVerbosity Level;
        public string       Category;
        public string       Message;
    }

    /// <summary>
    /// Centralised logger. Writes to the BepInEx log, keeps a 300-entry ring buffer,
    /// and optionally mirrors to BepInEx/logs/. Thread-safe.
    /// </summary>
    public static class HapticsLogger
    {
        public static ConfigEntry<LogVerbosity> Verbosity   = null!;
        public static ConfigEntry<bool>          WriteToFile = null!;

        private static ManualLogSource?  _bepLog;
        private static StreamWriter?     _fileWriter;

        private const  int               BufferCapacity = 300;
        private static readonly LogEntry[] _ring        = new LogEntry[BufferCapacity];
        private static volatile int      _head;
        private static volatile int      _count;
        private static readonly object   _lock         = new object();

        public static void Init(ManualLogSource bepLog, ConfigFile cfg)
        {
            _bepLog = bepLog;

            Verbosity = cfg.Bind("Logging", "Verbosity", LogVerbosity.Info,
                "How much detail to log. Off, Error, Warning, Info, Verbose.");

            WriteToFile = cfg.Bind("Logging", "WriteToFile", false,
                "Write a timestamped haptics log file to BepInEx/logs/. Debugging only.");

            if (WriteToFile.Value) OpenFileWriter();

            WriteToFile.SettingChanged += (_, _) =>
            {
                if (WriteToFile.Value) OpenFileWriter();
                else                   CloseFileWriter();
            };

            Info(LogCat.System, $"HapticsLogger initialised — verbosity={Verbosity.Value}, file={WriteToFile.Value}");
        }

        public static void Verbose(string cat, string msg) => Write(LogVerbosity.Verbose, cat, msg);
        public static void Info   (string cat, string msg) => Write(LogVerbosity.Info,    cat, msg);
        public static void Warning(string cat, string msg) => Write(LogVerbosity.Warning, cat, msg);
        public static void Error  (string cat, string msg) => Write(LogVerbosity.Error,   cat, msg);

        public static LogEntry[] GetSnapshot()
        {
            lock (_lock)
            {
                int total  = Math.Min(_count, BufferCapacity);
                var result = new LogEntry[total];
                int start = (_head - total + BufferCapacity) % BufferCapacity;
                for (int i = 0; i < total; i++)
                    result[i] = _ring[(start + i) % BufferCapacity];
                return result;
            }
        }

        public static int TotalCount => _count;

        public static void Shutdown()
        {
            Info(LogCat.System, "HapticsLogger shutting down.");
            CloseFileWriter();
        }

        private static void Write(LogVerbosity level, string category, string message)
        {
            if (Verbosity == null || level > Verbosity.Value) return;

            var entry = new LogEntry
            {
                Time     = DateTime.Now,
                Level    = level,
                Category = category,
                Message  = message,
            };

            lock (_lock)
            {
                _ring[_head] = entry;
                _head        = (_head + 1) % BufferCapacity;
                _count++;
                _fileWriter?.WriteLine(FormatFile(entry));
            }

            string line = $"[{category.Trim()}] {message}";
            switch (level)
            {
                case LogVerbosity.Verbose: _bepLog?.LogDebug(line);   break;
                case LogVerbosity.Info:    _bepLog?.LogInfo(line);    break;
                case LogVerbosity.Warning: _bepLog?.LogWarning(line); break;
                case LogVerbosity.Error:   _bepLog?.LogError(line);   break;
            }
        }

        private static string FormatFile(in LogEntry e)
            => $"{e.Time:HH:mm:ss.fff}  {LevelTag(e.Level)}  [{e.Category}]  {e.Message}";

        private static string LevelTag(LogVerbosity v) => v switch
        {
            LogVerbosity.Verbose => "[VERBOSE]",
            LogVerbosity.Info    => "[INFO   ]",
            LogVerbosity.Warning => "[WARN   ]",
            LogVerbosity.Error   => "[ERROR  ]",
            _                    => "[?      ]",
        };

        private static void OpenFileWriter()
        {
            CloseFileWriter();
            try
            {
                string dir  = Path.Combine(Paths.BepInExRootPath, "logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"rmw_haptics_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                _fileWriter = new StreamWriter(path, append: false) { AutoFlush = true };
                _fileWriter.WriteLine($"# Vibinwood debug log — opened {DateTime.Now:O}");
                _fileWriter.WriteLine();
                Info(LogCat.System, $"Log file opened: {path}");
            }
            catch (Exception ex)
            {
                _bepLog?.LogWarning($"[Haptics] Could not open log file: {ex.Message}");
            }
        }

        private static void CloseFileWriter()
        {
            try { _fileWriter?.Flush(); _fileWriter?.Close(); } catch { }
            _fileWriter = null;
        }
    }
}
