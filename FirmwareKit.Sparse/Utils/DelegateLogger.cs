namespace FirmwareKit.Sparse.Utils;

/// <summary>
/// A simple implementation of <see cref="ISparseLogger"/> that delegates logging to a provided action.
/// <para>ISparseLogger 的简单实现，将日志记录委托给提供的操作。</para>
/// </summary>
public class DelegateLogger : ISparseLogger
{
    private readonly Action<string> _logAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateLogger"/> class.
    /// <para>初始化 DelegateLogger 类的新实例。</para>
    /// </summary>
    /// <param name="logAction">The action to perform when logging. <para>记录日志时执行的操作。</para></param>
    public DelegateLogger(Action<string> logAction) => _logAction = logAction;

    /// <summary>
    /// Logs an informational message using the underlying delegate.
    /// <para>使用底层委托记录信息级别消息。</para>
    /// </summary>
    /// <param name="message">Message text to log. <para>要记录的消息文本。</para></param>
    public void LogInformation(string message) => _logAction($"[INFO] {message}");

    /// <summary>
    /// Logs a warning message using the underlying delegate.
    /// <para>使用底层委托记录警告级别消息。</para>
    /// </summary>
    /// <param name="message">Message text to log. <para>要记录的消息文本。</para></param>
    public void LogWarning(string message) => _logAction($"[WARN] {message}");

    /// <summary>
    /// Logs an error message using the underlying delegate.
    /// <para>使用底层委托记录错误级别消息。</para>
    /// </summary>
    /// <param name="message">Message text to log. <para>要记录的消息文本。</para></param>
    public void LogError(string message) => _logAction($"[ERROR] {message}");
}
