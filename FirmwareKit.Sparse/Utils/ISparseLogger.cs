namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// Defines a logger interface for the sparse library, providing diagnostic message output capabilities.
/// <para>定义稀疏库的日志记录器接口，提供诊断消息输出功能。</para>
/// </summary>
public interface ISparseLogger
{
    /// <summary>
    /// Logs an informational message.
    /// <para>记录信息级别消息。</para>
    /// </summary>
    /// <param name="message">The message text to log. <para>要记录的消息文本。</para></param>
    void LogInformation(string message);

    /// <summary>
    /// Logs a warning message.
    /// <para>记录警告级别消息。</para>
    /// </summary>
    /// <param name="message">The message text to log. <para>要记录的消息文本。</para></param>
    void LogWarning(string message);

    /// <summary>
    /// Logs an error message.
    /// <para>记录错误级别消息。</para>
    /// </summary>
    /// <param name="message">The message text to log. <para>要记录的消息文本。</para></param>
    void LogError(string message);
}
