namespace FirmwareKit.Sparse.Models;

/// <summary>
/// Represents the mode for reading a sparse file.
/// <para>表示稀疏文件的读取模式。</para>
/// </summary>
public enum SparseReadMode
{
    /// <summary>
    /// Normal mode — reads all data, treating the input as a raw byte stream.
    /// <para>普通模式 — 读取所有数据，将输入视为原始字节流。</para>
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Sparse mode — parses the sparse structure and reconstructs the original data.
    /// <para>稀疏模式 — 解析稀疏结构并重建原始数据。</para>
    /// </summary>
    Sparse = 1,

    /// <summary>
    /// Hole mode — treats zeroed blocks as holes (unallocated regions).
    /// <para>孔洞模式 — 将零填充块视为孔洞（未分配区域）。</para>
    /// </summary>
    Hole = 2
}
