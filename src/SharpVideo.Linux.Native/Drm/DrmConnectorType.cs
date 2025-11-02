namespace SharpVideo.Linux.Native.Drm;

public enum DrmConnectorType : uint
{
    Unknown = 0,
    VGA = 1,
    DVII = 2,
    DVID = 3,
    DVIA = 4,
    Composite = 5,
    SVIDEO = 6,
    LVDS = 7,
    Component = 8,
    NinePinDIN = 9,
    DisplayPort = 10,
    HDMIA = 11,
    HDMIB = 12,
    TV = 13,
    eDP = 14,
    VIRTUAL = 15,
    DSI = 16,
    DPI = 17,
    WRITEBACK = 18,
    SPI = 19,
    USB = 20
}