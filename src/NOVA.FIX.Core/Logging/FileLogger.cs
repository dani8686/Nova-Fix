using System.Text.Json;

namespace NOVA.FIX.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Structured, bounded, offline file logger. Writes JSON-lines to the local log
/// directory. Never performs any network I/O. Rotation keeps disk usage flat.
/// </summary>
public interface IAppLogger
{
    void Log(LogLevel level, string category, string message, Exception? exception = null);
    IReadOnlyList<string> RecentEntries(int count);
}

public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<string> _recent = new();
    private readonly string _logDirectory;
    private readonly int _maxRecent;
    private readonly long _maxFileBytes;
    private StreamWriter? _writer;
    private string _currentFile = string.Empty;

    public FileLogger(string logDirectory, int maxRecent = 500, long maxFileBytes = 5 * 1024 * 1024)
    {
        _logDirectory = logDirectory;
        _maxRecent = maxRecent;
        _maxFileBytes = maxFileBytes;
        Directory.CreateDirectory(logDirectory);
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        var entry = new LogEntry(
            DateTime.UtcNow,
            level.ToString().ToUpperInvariant(),
            category,
            message,
            exception?.GetType().Name,
            exception?.Message);

        var line = JsonSerializer.Serialize(entry);
        lock (_sync)
        {
            _recent.Enqueue(line);
            while (_recent.Count > _maxRecent) _recent.Dequeue();
            Write(line);
        }
    }

    public IReadOnlyList<string> RecentEntries(int count)
    {
        lock (_sync)
        {
            return _recent.Reverse().Take(count).Reverse().ToList();
        }
    }

    private void Write(string line)
    {
        try
        {
            var file = Path.Combine(_logDirectory, $"novafix-{DateTime.UtcNow:yyyyMMdd}.log");
            if (file != _currentFile || _writer is null)
            {
                _writer?.Dispose();
                if (File.Exists(file) && new FileInfo(file).Length > _maxFileBytes)
                {
                    var rotated = Path.Combine(_logDirectory,
                        $"novafix-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                    File.Move(file, rotated, overwrite: true);
                }
                _writer = new StreamWriter(new FileStream(
                    file, FileMode.Append, FileAccess.Write, FileShare.Read))
                { AutoFlush = true };
                _currentFile = file;
            }
            _writer.WriteLine(line);
        }
        catch
        {
            // Logging must never crash the product; drop the line silently.
        }
    }

    private sealed record LogEntry(
        DateTime TimestampUtc,
        string Level,
        string Category,
        string Message,
        string? ExceptionType,
        string? ExceptionMessage);

    public void Dispose()
    {
        lock (_sync) _writer?.Dispose();
    }
}
