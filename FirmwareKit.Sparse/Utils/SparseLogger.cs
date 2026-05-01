namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// A static logging utility for the sparse library that provides a singleton logger instance.
/// <para>稀疏库的静态日志工具，提供单例日志记录器实例。</para>
/// </summary>
public static class SparseLogger
{
    private static ISparseLogger _instance = new DelegateLogger(msg => LogMessage?.Invoke(msg));

    /// <summary>
    /// Gets or sets the current logger instance. Setting to null throws an exception.
    /// <para>获取或设置当前日志记录器实例。设置为 null 会抛出异常。</para>
    /// </summary>
    public static ISparseLogger Instance
    {
        get => _instance;
        set => _instance = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the callback action for default logging messages.
    /// <para>获取或设置默认日志消息的回调操作。</para>
    /// </summary>
    public static Action<string>? LogMessage { get; set; }

    /// <summary>
    /// Logs an informational message through the current logger instance.
    /// <para>通过当前日志记录器实例记录信息级别消息。</para>
    /// </summary>
    /// <param name="message">The message content. <para>消息内容。</para></param>
    public static void LogInformation(string message) => _instance.LogInformation(message);

    /// <summary>
    /// Logs a warning message through the current logger instance.
    /// <para>通过当前日志记录器实例记录警告级别消息。</para>
    /// </summary>
    /// <param name="message">The message content. <para>消息内容。</para></param>
    public static void LogWarning(string message) => _instance.LogWarning(message);

    /// <summary>
    /// Logs an error message through the current logger instance.
    /// <para>通过当前日志记录器实例记录错误级别消息。</para>
    /// </summary>
    /// <param name="message">The message content. <para>消息内容。</para></param>
    public static void LogError(string message) => _instance.LogError(message);
}
