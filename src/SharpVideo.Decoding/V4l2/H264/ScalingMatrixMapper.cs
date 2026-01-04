namespace SharpVideo.Decoding.V4l2.H264;

using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Maps H264 scaling matrices to V4L2 control structures.
/// </summary>
internal static class ScalingMatrixMapper
{
    public static V4L2CtrlH264ScalingMatrix MapScalingMatrix(SpsState sps, PpsState pps)
    {
        var matrix = new V4L2CtrlH264ScalingMatrix
        {
            scaling_list_4x4 = new byte[6 * 16],
            scaling_list_8x8 = new byte[6 * 64]
        };

        // Initialize with default values (8) if needed, but H.264 says 8 is default
        Array.Fill(matrix.scaling_list_4x4, (byte)8);
        Array.Fill(matrix.scaling_list_8x8, (byte)8);

        // If PPS has scaling matrix, it overrides SPS
        if (pps.pic_scaling_matrix_present_flag != 0)
        {
            CopyScalingLists(pps.ScalingList4x4, matrix.scaling_list_4x4);
            CopyScalingLists(pps.ScalingList8x8, matrix.scaling_list_8x8);
        }
        else if (sps.sps_data.seq_scaling_matrix_present_flag != 0)
        {
            CopyScalingLists(sps.sps_data.ScalingList4x4, matrix.scaling_list_4x4);
            CopyScalingLists(sps.sps_data.ScalingList8x8, matrix.scaling_list_8x8);
        }

        return matrix;
    }

    private static void CopyScalingLists(List<uint> source, byte[] destination)
    {
        if (source == null) return;
        int count = Math.Min(source.Count, destination.Length);
        for (int i = 0; i < count; i++)
        {
            destination[i] = (byte)source[i];
        }
    }
}
