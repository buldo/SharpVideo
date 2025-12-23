using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace SharpVideo.FfmpegBin;

public static class FfmpegLoader
{
    /// <summary>
    /// Right order to load
    /// </summary>
    private static readonly string[] _ffmpegDlls =
    [
        "avutil",
        "swresample",
        "swscale",
        "avcodec",
        "avformat",
        "avfilter",
        "avdevice"
    ];

    private static readonly string[] _linuxProbeDirs =
    [
        "/usr/lib/x86_64-linux-gnu"
    ];

    private static readonly string _libExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dll" : "so";
    private static readonly string _libPrefix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "" : "lib";

    static FfmpegLoader()
    {
        var currentPlatform = RuntimeInformation.OSArchitecture;
    }

    public static string? Load(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return LoadWindows(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return LoadLinux(logger);
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
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
        var result = Kernel32Native.AddDllDirectory(ffmpegPath);
        if (result == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            logger.LogWarning("AddDllDirectory failed with error {Error}, falling back to SetDllDirectory", error);
            Kernel32Native.SetDllDirectory(ffmpegPath);
        }

        var dllPaths = new List<string>();
        foreach (var dllName in _ffmpegDlls)
        {
            var path = GetLibraryPath(ffmpegPath, dllName);
            if (!File.Exists(path))
            {
                throw new Exception($"Dll not found {path}");
            }

            dllPaths.Add(path);
        }

        logger.LogDebug("Pre-loading FFmpeg libraries...");
        foreach (var dllPath in dllPaths)
        {
            var handle = Kernel32Native.LoadLibrary(dllPath);
            if (handle == IntPtr.Zero)
            {
                logger.LogError("Failed to load {DllName}: error {Error}", dllPath, Marshal.GetLastWin32Error());
            }
        }

        logger.LogDebug("FFmpeg library setup completed");

        return ffmpegPath;
    }

    [SupportedOSPlatform("linux")]
    private static string? LoadLinux(ILogger logger)
    {
        return null;
    }

    private static string? GetLibraryPath(string basePath, string name)
    {
        var files = Directory
            .GetFiles(basePath, $"{_libPrefix}{name}*.{_libExtension}")
            .ToList();
        if (files.Count != 1)
        {
            return null;
        }

        return files[0];
    }
}