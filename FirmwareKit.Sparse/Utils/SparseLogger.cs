namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// A simple logging utility for the sparse library.
/// </summary>
public static class SparseLogger
{
    private static ISparseLogger _instance = new DelegateLogger(msg => LogMessage?.Invoke(msg));

    /// <summary>Gets or sets the current logger instance.</summary>
    public static ISparseLogger Instance
    {
        get => _instance;
        set => _instance = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the callback action for default logging messages.</summary>
    public static Action<string>? LogMessage { get; set; }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">The message content.</param>
    public static void LogInformation(string message) => _instance.LogInformation(message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">The message content.</param>
    public static void LogWarning(string message) => _instance.LogWarning(message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    /// <param name="message">The message content.</param>
    public static void LogError(string message) => _instance.LogError(message);
}
