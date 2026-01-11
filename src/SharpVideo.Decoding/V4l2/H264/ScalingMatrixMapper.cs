namespace SharpVideo.Decoding.V4l2.H264;

using System.Runtime.Versioning;

using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Maps H264 scaling matrices to V4L2 control structures.
/// V4L2 expects scaling lists in raster scan order, but H.264 stores them in zigzag order.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class ScalingMatrixMapper
{
    // Zigzag scan order for 4x4 matrices (H.264 Table 7-3)
    private static readonly int[] ZigZag4X4 =
    {
        0, 1, 4, 8,
        5, 2, 3, 6,
        9, 12, 13, 10,
        7, 11, 14, 15
    };

    // Zigzag scan order for 8x8 matrices (H.264 Table 7-4)
    private static readonly int[] ZigZag8X8 =
    {
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63
    };

    public static V4L2CtrlH264ScalingMatrix MapScalingMatrix(SpsState sps, PpsState pps)
    {
        var matrix = new V4L2CtrlH264ScalingMatrix
        {
            scaling_list_4x4 = new byte[6 * 16],
            scaling_list_8x8 = new byte[6 * 64]
        };

        // Initialize with default flat scaling (16 for 4x4, 16 for 8x8 as per H.264 spec)
        Array.Fill(matrix.scaling_list_4x4, (byte)16);
        Array.Fill(matrix.scaling_list_8x8, (byte)16);

        // Determine chroma format for 8x8 matrix count
        // For YCbCr 4:2:2 and less, we need 2 8x8 matrices (Intra Y, Inter Y)
        // For 4:4:4, we need 6 8x8 matrices
        int num8x8Lists = sps.sps_data.chroma_format_idc == 3 ? 6 : 2;

        // If PPS has scaling matrix, it overrides SPS
        if (pps.pic_scaling_matrix_present_flag != 0)
        {
            CopyScalingLists4x4ZigzagToRaster(pps.ScalingList4x4, matrix.scaling_list_4x4);
            CopyScalingLists8x8ZigzagToRaster(pps.ScalingList8x8, matrix.scaling_list_8x8, num8x8Lists);
        }
        else if (sps.sps_data.seq_scaling_matrix_present_flag != 0)
        {
            CopyScalingLists4x4ZigzagToRaster(sps.sps_data.ScalingList4x4, matrix.scaling_list_4x4);
            CopyScalingLists8x8ZigzagToRaster(sps.sps_data.ScalingList8x8, matrix.scaling_list_8x8, num8x8Lists);
        }

        return matrix;
    }

    /// <summary>
    /// Copies 4x4 scaling lists from zigzag order to raster scan order.
    /// </summary>
    private static void CopyScalingLists4x4ZigzagToRaster(List<uint> source, byte[] destination)
    {
        if (source == null || source.Count == 0)
            return;

        // 6 lists of 16 elements each
        for (int listIdx = 0; listIdx < 6; listIdx++)
        {
            int srcOffset = listIdx * 16;
            int dstOffset = listIdx * 16;

            for (int i = 0; i < 16; i++)
            {
                int srcIndex = srcOffset + i;
                if (srcIndex < source.Count)
                {
                    // Convert from zigzag position i to raster position ZigZag4X4[i]
                    destination[dstOffset + ZigZag4X4[i]] = (byte)source[srcIndex];
                }
            }
        }
    }

    /// <summary>
    /// Copies 8x8 scaling lists from zigzag order to raster scan order.
    /// </summary>
    private static void CopyScalingLists8x8ZigzagToRaster(List<uint> source, byte[] destination, int numLists)
    {
        if (source == null || source.Count == 0)
            return;

        for (int listIdx = 0; listIdx < numLists; listIdx++)
        {
            int srcOffset = listIdx * 64;
            int dstOffset = listIdx * 64;

            for (int i = 0; i < 64; i++)
            {
                int srcIndex = srcOffset + i;
                if (srcIndex < source.Count)
                {
                    // Convert from zigzag position i to raster position ZigZag8X8[i]
                    destination[dstOffset + ZigZag8X8[i]] = (byte)source[srcIndex];
                }
            }
        }
    }
}
