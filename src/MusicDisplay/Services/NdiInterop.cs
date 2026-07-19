using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicDisplay.Services;

/// <summary>
/// Minimal P/Invoke surface for the NDI SDK (https://ndi.video), covering only what's needed to
/// send video frames: initialize, create/destroy a sender, and push a frame.
///
/// NDI is not statically linked. The NDI Runtime installer does not add itself to PATH; instead
/// it sets an environment variable (NDI_RUNTIME_DIR_V6, or an older version) pointing at its
/// install directory. <see cref="TryLocateAndPrepareLibrary"/> follows NDI's own documented
/// dynamic-loading pattern: find that directory, then call SetDllDirectory so the normal
/// DllImport below can resolve Processing.NDI.Lib.x64.dll. If no NDI Runtime installation is
/// found, it returns false and no NDI function is ever called.
/// </summary>
internal static class NdiInterop
{
    private const string LibraryName = "Processing.NDI.Lib.x64.dll";

    private static readonly string[] RuntimeDirEnvVars =
    {
        "NDI_RUNTIME_DIR_V6",
        "NDI_RUNTIME_DIR_V5",
        "NDI_RUNTIME_DIR_V4",
        "NDI_RUNTIME_DIR_V3",
        "NDI_RUNTIME_DIR_V2",
    };

    public const int FourCcBgra = 'B' | ('G' << 8) | ('R' << 16) | ('A' << 24);
    public const int FrameFormatProgressive = 1;

    /// <summary>Sentinel telling the SDK to synthesize its own timecode/timestamp from the
    /// local clock instead of using an explicit one.</summary>
    public const long SynthesizeTimecode = long.MinValue;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string? lpPathName);

    /// <summary>Finds the installed NDI Runtime and points the DLL search path at it. Safe to
    /// call more than once. Returns false (without throwing) if no NDI Runtime installation can
    /// be found.</summary>
    public static bool TryLocateAndPrepareLibrary()
    {
        foreach (var variable in RuntimeDirEnvVars)
        {
            var dir = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            if (File.Exists(Path.Combine(dir, LibraryName)))
            {
                SetDllDirectory(dir);
                return true;
            }
        }

        return false;
    }

    /// <summary>Encodes a .NET string as null-terminated UTF-8 in unmanaged memory (NDI uses
    /// UTF-8 for all strings, not the OS ANSI codepage). Caller owns the returned pointer and
    /// must free it with Marshal.FreeHGlobal.</summary>
    public static IntPtr AllocUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SendCreate
    {
        public IntPtr NdiName;
        public IntPtr Groups;
        [MarshalAs(UnmanagedType.I1)]
        public bool ClockVideo;
        [MarshalAs(UnmanagedType.I1)]
        public bool ClockAudio;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoFrame
    {
        public int Xres;
        public int Yres;
        public int FourCc;
        public int FrameRateN;
        public int FrameRateD;
        public float PictureAspectRatio;
        public int FrameFormatType;
        public long Timecode;
        public IntPtr PData;
        public int LineStrideInBytes;
        public IntPtr PMetadata;
        public long Timestamp;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool NDIlib_initialize();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void NDIlib_destroy();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr NDIlib_send_create(ref SendCreate createSettings);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void NDIlib_send_destroy(IntPtr instance);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void NDIlib_send_send_video_v2(IntPtr instance, ref VideoFrame videoData);
}
