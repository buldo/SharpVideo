using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.MultiPlaneGlExample;

// Simple EGL test - just tries to initialize EGL and reports result
Console.WriteLine("=== EGL Initialization Test ===");
Console.WriteLine();

var loggerFactory = LoggerFactory.Create(builder => builder
    .AddConsole()
    .SetMinimumLevel(LogLevel.Trace));

var logger = loggerFactory.CreateLogger("EglTest");

try
{
    logger.LogInformation("Starting EGL test...");
    logger.LogInformation("");
    
    // Try to get EGL display
    logger.LogInformation("Step 1: Getting EGL display...");
    var display = NativeEgl.GetDisplay(NativeEgl.EGL_DEFAULT_DISPLAY);
    
    if (display == 0)
    {
var error = NativeEgl.GetError();
        logger.LogError("? Failed to get EGL display: {Error}", NativeEgl.GetErrorString(error));
        return 1;
    }
    
    logger.LogInformation("? Got EGL display: 0x{Display:X}", display);
    logger.LogInformation("");
    
    // Try to initialize EGL
    logger.LogInformation("Step 2: Initializing EGL...");
    if (!NativeEgl.Initialize(display, out int major, out int minor))
    {
        var error = NativeEgl.GetError();
      logger.LogError("? Failed to initialize EGL: {Error}", NativeEgl.GetErrorString(error));
      logger.LogError("");
      logger.LogError("Common fixes:");
        logger.LogError("  1. Add user to groups: sudo usermod -a -G video,render $USER");
        logger.LogError("  2. Install packages: sudo apt install libgl1-mesa-dev libgles2-mesa-dev");
        logger.LogError("  3. Check permissions: ls -la /dev/dri/");
   logger.LogError("");
        logger.LogError("Run ./fix-egl.sh for detailed diagnostics");
return 1;
    }
    
    logger.LogInformation("? EGL initialized successfully!");
    logger.LogInformation("  Version: {Major}.{Minor}", major, minor);
    logger.LogInformation("");
    
    // Query EGL info
    logger.LogInformation("Step 3: Querying EGL information...");
    var vendor = Marshal.PtrToStringAnsi(NativeEgl.QueryString(display, 0x3053));  // EGL_VENDOR
    var version = Marshal.PtrToStringAnsi(NativeEgl.QueryString(display, 0x3054)); // EGL_VERSION
    var clientApis = Marshal.PtrToStringAnsi(NativeEgl.QueryString(display, 0x308D)); // EGL_CLIENT_APIS
    
  logger.LogInformation("  Vendor: {Vendor}", vendor);
    logger.LogInformation("  Version: {Version}", version);
    logger.LogInformation("  Client APIs: {ClientApis}", clientApis);
    logger.LogInformation("");
    
    // Check for required extensions
 logger.LogInformation("Step 4: Checking required extensions...");
    var extensions = Marshal.PtrToStringAnsi(NativeEgl.QueryString(display, 0x3055)); // EGL_EXTENSIONS
    
    var requiredExtensions = new[]
    {
        "EGL_KHR_image_base",
        "EGL_EXT_image_dma_buf_import",
        "EGL_KHR_gl_renderbuffer_image"
    };
    
    bool allPresent = true;
    foreach (var ext in requiredExtensions)
    {
        if (extensions?.Contains(ext) == true)
        {
            logger.LogInformation("  ? {Extension}", ext);
        }
        else
        {
      logger.LogWarning("  ? {Extension} - NOT FOUND", ext);
      allPresent = false;
        }
    }
    
    logger.LogInformation("");
    
    // Cleanup
    NativeEgl.Terminate(display);
    
    // Final verdict
    if (allPresent)
    {
   logger.LogInformation("========================================");
        logger.LogInformation("? EGL TEST PASSED");
  logger.LogInformation("========================================");
        logger.LogInformation("Your system is ready for OpenGL ES + DMA-BUF rendering!");
        logger.LogInformation("");
        logger.LogInformation("You can now run: dotnet run");
        return 0;
    }
    else
    {
        logger.LogWarning("========================================");
        logger.LogWarning("??  EGL PARTIALLY WORKING");
        logger.LogWarning("========================================");
        logger.LogWarning("EGL initializes, but some DMA-BUF extensions are missing.");
        logger.LogWarning("The full demo may not work.");
        logger.LogWarning("");
        logger.LogWarning("Your GPU driver may not support DMA-BUF import.");
logger.LogWarning("Try updating Mesa/GPU drivers.");
        return 2;
    }
}
catch (Exception ex)
{
    logger.LogError("? Unexpected error: {Message}", ex.Message);
    logger.LogError("{StackTrace}", ex.StackTrace);
    return 1;
}
