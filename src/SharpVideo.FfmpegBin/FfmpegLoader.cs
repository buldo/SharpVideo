using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SharpVideo.FfmpegBin;

public static class FfmpegLoader
{

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    public static string? Load(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return LoadWindows(logger);
        }

        return null;
    }

    private static string LoadWindows(ILogger logger)
    {
        var ffmpegPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "runtimes",
                    "win-x64",
                    "native");

        if (!Directory.Exists(ffmpegPath))
        {
            throw new Exception($"Expected directory with binaries are not exists: {ffmpegPath}");
        }


        // Use AddDllDirectory (Windows 8+) instead of SetDllDirectory
        // This adds the directory to the search path without replacing it
        var result = AddDllDirectory(ffmpegPath);
        if (result == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            logger.LogWarning("AddDllDirectory failed with error {Error}, falling back to SetDllDirectory", error);
            SetDllDirectory(ffmpegPath);
        }

        // Right order to load
        var ffmpegDlls = new[]
        {
            "avutil-60.dll",
            "swresample-6.dll",
            "swscale-9.dll",
            "avcodec-62.dll",
            "avformat-62.dll",
            "avfilter-11.dll",
            "avdevice-62.dll",
        };

        var dllPaths = new List<string>();
        foreach (var dllName in ffmpegDlls)
        {
            var path = Path.Combine(ffmpegPath, dllName);
            if (!File.Exists(path))
            {
                throw new Exception($"Dll not found {path}");
            }

            dllPaths.Add(path);
        }

        logger.LogDebug("Pre-loading FFmpeg libraries...");
        foreach (var dllPath in dllPaths)
        {
            var handle = LoadLibrary(dllPath);
            if (handle == IntPtr.Zero)
            {
                logger.LogError("Failed to load {DllName}: error {Error}", dllPath, Marshal.GetLastWin32Error());
            }
        }

        logger.LogDebug("FFmpeg library setup completed");

        return ffmpegPath;
    }
}