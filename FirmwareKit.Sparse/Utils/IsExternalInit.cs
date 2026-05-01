#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// <para>保留供编译器用于跟踪元数据。开发者不应在源代码中使用此类。</para>
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal static class IsExternalInit { }
#endif
