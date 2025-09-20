namespace SharpVideo.V4L2;

public static class V4L2DeviceManager
{
    public static string[] GetVideoDevices()
    {
        return Directory.EnumerateFileSystemEntries("/dev", "video*")
            .Where(CheckForSymlink)
            .ToArray();

        bool CheckForSymlink(string path)
        {
            var fileInfo = new FileInfo(path);

            // This is not symlink
            if (!fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            var target = fileInfo.ResolveLinkTarget(true);
            if (target == null)
            {
                return true;
            }

            if(target.FullName.StartsWith("/dev/video"))
            {
                return false;
            }

            return true;
        }
    }

    // public static List<string> FindByOutputPixelFormat()
    // {

    // }
}