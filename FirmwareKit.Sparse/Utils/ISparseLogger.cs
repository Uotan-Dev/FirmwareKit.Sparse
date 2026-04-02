namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Defines a logger interface for the sparse library.
/// </summary>
public interface ISparseLogger
{
    /// <summary>Logs an informational message.</summary>
    void LogInformation(string message);
    /// <summary>Logs a warning message.</summary>
    void LogWarning(string message);
    /// <summary>Logs an error message.</summary>
    void LogError(string message);
}

/// <summary>
/// A simple implementation of <see cref="ISparseLogger"/> that uses a delegate.
/// </summary>
public class DelegateLogger : ISparseLogger
{
    private readonly Action<string> _logAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateLogger"/> class.
    /// </summary>
    /// <param name="logAction">The action to perform when logging.</param>
    public DelegateLogger(Action<string> logAction) => _logAction = logAction;

    /// <summary>
    /// Log an informational message using the underlying delegate.
    /// </summary>
    /// <param name="message">Message text to log.</param>
    public void LogInformation(string message) => _logAction($"[INFO] {message}");

    /// <summary>
    /// Log a warning message using the underlying delegate.
    /// </summary>
    /// <param name="message">Message text to log.</param>
    public void LogWarning(string message) => _logAction($"[WARN] {message}");

    /// <summary>
    /// Log an error message using the underlying delegate.
    /// </summary>
    /// <param name="message">Message text to log.</param>
    public void LogError(string message) => _logAction($"[ERROR] {message}");
}
