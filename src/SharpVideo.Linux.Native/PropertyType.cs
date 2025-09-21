namespace SharpVideo.Linux.Native;

[Flags]
public enum PropertyType : uint
{
    [Obsolete]
    DRM_MODE_PROP_PENDING = (1 << 0), /* deprecated, do not use */
    DRM_MODE_PROP_RANGE = (1 << 1),
    DRM_MODE_PROP_IMMUTABLE = (1 << 2),
    DRM_MODE_PROP_ENUM = (1 << 3), /* enumerated type with text strings */
    DRM_MODE_PROP_BLOB = (1 << 4),
    DRM_MODE_PROP_BITMASK = (1 << 5), /* bitmask of enumerated types */

    /* non-extended types: legacy bitmask, one bit per type: */
    DRM_MODE_PROP_LEGACY_TYPE = (DRM_MODE_PROP_RANGE |
                                 DRM_MODE_PROP_ENUM |
                                 DRM_MODE_PROP_BLOB |
                                 DRM_MODE_PROP_BITMASK),
    DRM_MODE_PROP_EXTENDED_TYPE	= 0x0000ffc0
}