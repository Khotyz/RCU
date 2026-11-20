using System.Runtime.InteropServices;

namespace RedControlUtility.Services;

public static class NativeFileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OpenFileName ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    public static string? ShowOpenDialog(IntPtr ownerHwnd, string filter, string title)
    {
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            hwndOwner = ownerHwnd,
            lpstrFilter = filter,
            lpstrFile = new string('\0', 1024),
            nMaxFile = 1024,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
        };

        return GetOpenFileNameW(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    public static string? ShowSaveDialog(IntPtr ownerHwnd, string filter, string defaultExt, string defaultFileName, string title)
    {
        string fileBuffer = defaultFileName + new string('\0', Math.Max(0, 1024 - defaultFileName.Length));
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            hwndOwner = ownerHwnd,
            lpstrFilter = filter,
            lpstrFile = fileBuffer,
            nMaxFile = 1024,
            lpstrDefExt = defaultExt,
            lpstrTitle = title,
            Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
        };

        return GetSaveFileNameW(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }
}
