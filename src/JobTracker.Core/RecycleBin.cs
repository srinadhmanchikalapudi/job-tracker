using System.Runtime.InteropServices;

namespace JobTracker.Core;

/// <summary>Moves a folder to the Windows Recycle Bin, so removing something by mistake can be undone from there.</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    // With NOCONFIRMATION this still warns when the shell would have to delete for good (a drive with no Recycle Bin) instead of recycling.
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// Sends the folder and everything in it to the Recycle Bin. Throws <see cref="IOException"/> (in plain words) when Windows could not, or
    /// when the person was asked and said no; the folder is then still there.
    /// </summary>
    public static void Send(string folder)
    {
        var path = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        const ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NOERRORUI | FOF_SILENT;
        var from = path + "\0\0"; // a list of paths, each ending in a null, and a last extra null

        int result;
        bool aborted;
        if (IntPtr.Size == 8)
        {
            var op = new SHFILEOPSTRUCT64 { wFunc = FO_DELETE, pFrom = from, fFlags = flags };
            result = SHFileOperation64(ref op);
            aborted = op.fAnyOperationsAborted;
        }
        else
        {
            var op = new SHFILEOPSTRUCT32 { wFunc = FO_DELETE, pFrom = from, fFlags = flags };
            result = SHFileOperation32(ref op);
            aborted = op.fAnyOperationsAborted;
        }

        if (result != 0 || aborted || Directory.Exists(path))
            throw new IOException(aborted
                ? "The folder was not moved to the Recycle Bin."
                : $"Windows could not move the folder to the Recycle Bin (code {result}). A file in it may be open in another program.");
    }

    // 32-bit Windows packs this structure to one byte; 64-bit uses the normal layout.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT32
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo; public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT64
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo; public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation32(ref SHFILEOPSTRUCT32 op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation64(ref SHFILEOPSTRUCT64 op);
}