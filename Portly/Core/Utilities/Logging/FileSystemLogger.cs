using System.Collections.Concurrent;
using System.Text;

namespace Portly.Core.Utilities.Logging
{
    /// <summary>
    /// A logging implementation that writes to a file on the disk.
    /// </summary>
    public class FileSystemLogger : LogProviderBase, IDisposable
    {
        private readonly Settings _settings;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private readonly Dictionary<string, LogFileState> _files = [];
        private readonly Task _processingTask;
        private readonly string _sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        private bool _disposed;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="enableDebug"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public FileSystemLogger(Settings? settings = null, bool enableDebug = false) : base(enableDebug)
        {
            _settings = settings ?? new Settings();
            Directory.CreateDirectory(_settings.FolderPath);

            if (_settings.DeleteAllLogsOnStartup)
                CleanupOldFiles(string.Empty, true);

            _processingTask = Task.Run(ProcessQueueAsync);
        }

        /// <inheritdoc/>
        protected override void Write(string message, LogLevel logLevel)
        {
            _queue.Enqueue(new LogEntry
            {
                Message = message,
                Level = logLevel,
                Timestamp = DateTime.Now
            });
        }

        private async Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_queue.IsEmpty)
                    {
                        await Task.Delay(50, _cts.Token);
                        continue;
                    }

                    var batch = new List<LogEntry>();
                    while (_queue.TryDequeue(out var entry) && batch.Count < _settings.BatchSize)
                        batch.Add(entry);

                    WriteBatch(batch);
                }
                catch (OperationCanceledException) { }
                catch { /* swallow to prevent background crash */ }
            }

            // Flush remaining logs on shutdown
            while (_queue.TryDequeue(out var remaining))
                WriteBatch([remaining]);

            foreach (var state in _files.Values)
                CloseFile(state);
        }

        private void WriteBatch(IEnumerable<LogEntry> entries)
        {
            foreach (var entry in entries)
            {
                string levelName = entry.Level.ToString().ToLower();
                string key = _settings.SplitPerLogLevel ? levelName : string.Empty;

                if (!_files.TryGetValue(key, out var state))
                {
                    state = CreateNewFile(levelName);
                    _files[key] = state;
                }

                string xml = FormatMessage(entry);
                byte[] bytes = Encoding.UTF8.GetBytes(xml + Environment.NewLine);

                // Rotate file if size exceeded
                if (state.SizeBytes + bytes.Length > _settings.MaxLogFileSizeInKb * 1024)
                {
                    CloseFile(state);
                    state = CreateNewFile(levelName, state.Index + 1);
                    _files[key] = state;
                }

                // Simply write via StreamWriter and flush
                state.Writer.WriteLine(xml);
                state.Writer.Flush();
                state.SizeBytes += bytes.Length;
            }
        }

        private LogFileState CreateNewFile(string levelName, int index = 1)
        {
            CleanupOldFiles(levelName);

            string fileName = _settings.SplitPerLogLevel
                ? $"{levelName}_log_{_sessionTimestamp}_{index}.xml"
                : $"log_{_sessionTimestamp}_{index}.xml";

            string path = Path.Combine(_settings.FolderPath, fileName);

            var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.Flush();

            return new LogFileState
            {
                Writer = writer,
                FilePath = path,
                Index = index,
                SizeBytes = 0
            };
        }

        private static void CloseFile(LogFileState state, string? lastLine = null)
        {
            try
            {
                state.Writer.Dispose();
            }
            catch { }
        }

        private void CleanupOldFiles(string levelName, bool deleteAll = false)
        {
            string[]? files;
            if (deleteAll)
            {
                files = Directory.GetFiles(_settings.FolderPath);

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { /* ignore errors */ }
                }
                return;
            }

            if (_settings.RetentionDays <= 0) return;

            var cutoff = DateTime.UtcNow.AddDays(-_settings.RetentionDays);
            files = Directory.GetFiles(_settings.FolderPath);

            foreach (var file in files)
            {
                try
                {
                    if (File.GetCreationTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { /* ignore errors */ }
            }
        }

        private static string FormatMessage(LogEntry entry)
        {
            return $"<msg datetime=\"{entry.Timestamp:HH:mm:ss.fff}\" level=\"{entry.Level}\">{System.Security.SecurityElement.Escape(entry.Message)}</msg>";
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;

            _cts.Cancel();
            _processingTask.Wait();

            foreach (var state in _files.Values)
                CloseFile(state);

            _cts.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private sealed class LogFileState
        {
            public StreamWriter Writer { get; set; } = null!;
            public long SizeBytes { get; set; }
            public int Index { get; set; }
            public string FilePath { get; set; } = null!;
            public string? LastLine { get; set; }
        }

        private sealed class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; } = null!;
        }

        /// <summary>
        /// Configuration options for the <see cref="FileSystemLogger"/>.
        /// </summary>
        public class Settings
        {
            /// <summary>
            /// Gets or sets the directory where log files are created.
            /// The directory will be created automatically if it does not exist.
            /// Defaults to <c>"Logs"</c>.
            /// </summary>
            public string FolderPath { get; set; } = "Logs";

            /// <summary>
            /// Gets or sets a value indicating whether logs are written to separate files per log level.
            /// When enabled, each level (e.g. Info, Error, Debug) is written to its own file.
            /// When disabled, all log entries are written to a single file.
            /// Defaults to <c>false</c>.
            /// </summary>
            public bool SplitPerLogLevel { get; set; } = false;

            /// <summary>
            /// Gets or sets the maximum size of a log file in kilobytes before a new file is created.
            /// Once the limit is exceeded, logging continues in a new file with an incremented index.
            /// Defaults to <c>5000</c> (approximately 5 MB).
            /// </summary>
            public int MaxLogFileSizeInKb { get; set; } = 5000;

            /// <summary>
            /// Gets or sets the number of log entries processed in a single batch write operation.
            /// Larger batch sizes reduce disk I/O overhead and improve throughput, but may increase memory usage
            /// and delay the time before logs are written to disk.
            /// <br>Use larger batches for optimization if a lot of logging is involved.</br>
            /// Defaults to <c>1</c>.
            /// </summary>
            public int BatchSize { get; set; } = 1;

            /// <summary>
            /// Gets or sets the number of days to retain log files before they are automatically deleted.
            /// A value of <c>0</c> or less disables log cleanup.
            /// Defaults to <c>7</c>.
            /// </summary>
            public int RetentionDays { get; set; } = 7;

            /// <summary>
            /// Removes all logfiles on startup, if true <see cref="RetentionDays"/> becomes obsolete.
            /// </summary>
            public bool DeleteAllLogsOnStartup { get; set; } = false;
        }
    }
}
