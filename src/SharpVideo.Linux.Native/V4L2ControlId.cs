namespace SharpVideo.Linux.Native
{
    public enum V4L2ControlId : uint
    {
        // User-class control IDs
        BRIGHTNESS = 0x00980900,
        CONTRAST = 0x00980901,
        SATURATION = 0x00980902,
        HUE = 0x00980903,

        // ... other standard controls ...

        // Special flags
        V4L2_CTRL_FLAG_NEXT_CTRL = 0x08000000,
        V4L2_CTRL_FLAG_NEXT_COMPOUND = 0x10000000
    }
}
