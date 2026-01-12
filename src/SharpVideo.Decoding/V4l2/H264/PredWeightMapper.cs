using System.Runtime.Versioning;

using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.Decoding.V4l2.H264;

/// <summary>
/// Maps H264 prediction weight table to V4L2 control structure.
/// Following GStreamer's gst_v4l2_codec_h264_dec_fill_pred_weight.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class PredWeightMapper
{
    /// <summary>
    /// Maps the prediction weight table from slice header to V4L2 structure.
    /// </summary>
    public static V4L2CtrlH264PredWeights MapPredWeights(SliceHeaderState sliceHeader)
    {
        var predWeights = new V4L2CtrlH264PredWeights
        {
            LumaLog2WeightDenom = (ushort)(sliceHeader.pred_weight_table?.luma_log2_weight_denom ?? 0),
            ChromaLog2WeightDenom = (ushort)(sliceHeader.pred_weight_table?.chroma_log2_weight_denom ?? 0),
            WeightFactors = new V4L2H264WeightFactors[2]
        };

        // Initialize weight factors arrays
        for (int i = 0; i < 2; i++)
        {
            predWeights.WeightFactors[i] = new V4L2H264WeightFactors
            {
                LumaWeight = new short[32],
                LumaOffset = new short[32],
                ChromaWeight = new short[32 * 2],
                ChromaOffset = new short[32 * 2]
            };
        }

        var predWeightTable = sliceHeader.pred_weight_table;
        if (predWeightTable == null)
        {
            return predWeights;
        }

        // Fill L0 weights
        FillL0Weights(sliceHeader, predWeightTable, ref predWeights);

        // Fill L1 weights (for B-slices only)
        uint sliceType = sliceHeader.slice_type % 5;
        if (sliceType == 1) // B slice
        {
            FillL1Weights(sliceHeader, predWeightTable, ref predWeights);
        }

        return predWeights;
    }

    private static void FillL0Weights(
        SliceHeaderState sliceHeader,
        PredWeightTableState predWeightTable,
        ref V4L2CtrlH264PredWeights predWeights)
    {
        int numRefL0 = (int)sliceHeader.num_ref_idx_l0_active_minus1 + 1;

        for (int i = 0; i < numRefL0 && i < 32; i++)
        {
            // Luma weight and offset
            if (predWeightTable.luma_weight_l0 != null && i < predWeightTable.luma_weight_l0.Count)
            {
                predWeights.WeightFactors[0].LumaWeight[i] = (short)predWeightTable.luma_weight_l0[i];
            }

            if (predWeightTable.luma_offset_l0 != null && i < predWeightTable.luma_offset_l0.Count)
            {
                predWeights.WeightFactors[0].LumaOffset[i] = (short)predWeightTable.luma_offset_l0[i];
            }

            // Chroma weights and offsets (nested list: chroma_weight_l0[i][j])
            if (predWeightTable.chroma_weight_l0 != null && i < predWeightTable.chroma_weight_l0.Count)
            {
                var chromaWeights = predWeightTable.chroma_weight_l0[i];
                for (int j = 0; j < 2 && chromaWeights != null && j < chromaWeights.Count; j++)
                {
                    predWeights.WeightFactors[0].ChromaWeight[i * 2 + j] = (short)chromaWeights[j];
                }
            }

            if (predWeightTable.chroma_offset_l0 != null && i < predWeightTable.chroma_offset_l0.Count)
            {
                var chromaOffsets = predWeightTable.chroma_offset_l0[i];
                for (int j = 0; j < 2 && chromaOffsets != null && j < chromaOffsets.Count; j++)
                {
                    predWeights.WeightFactors[0].ChromaOffset[i * 2 + j] = (short)chromaOffsets[j];
                }
            }
        }
    }

    private static void FillL1Weights(
        SliceHeaderState sliceHeader,
        PredWeightTableState predWeightTable,
        ref V4L2CtrlH264PredWeights predWeights)
    {
        int numRefL1 = (int)sliceHeader.num_ref_idx_l1_active_minus1 + 1;

        for (int i = 0; i < numRefL1 && i < 32; i++)
        {
            // Luma weight and offset
            if (predWeightTable.luma_weight_l1 != null && i < predWeightTable.luma_weight_l1.Count)
            {
                predWeights.WeightFactors[1].LumaWeight[i] = (short)predWeightTable.luma_weight_l1[i];
            }

            if (predWeightTable.luma_offset_l1 != null && i < predWeightTable.luma_offset_l1.Count)
            {
                predWeights.WeightFactors[1].LumaOffset[i] = (short)predWeightTable.luma_offset_l1[i];
            }

            // Chroma weights and offsets (nested list: chroma_weight_l1[i][j])
            if (predWeightTable.chroma_weight_l1 != null && i < predWeightTable.chroma_weight_l1.Count)
            {
                var chromaWeights = predWeightTable.chroma_weight_l1[i];
                for (int j = 0; j < 2 && chromaWeights != null && j < chromaWeights.Count; j++)
                {
                    predWeights.WeightFactors[1].ChromaWeight[i * 2 + j] = (short)chromaWeights[j];
                }
            }

            if (predWeightTable.chroma_offset_l1 != null && i < predWeightTable.chroma_offset_l1.Count)
            {
                var chromaOffsets = predWeightTable.chroma_offset_l1[i];
                for (int j = 0; j < 2 && chromaOffsets != null && j < chromaOffsets.Count; j++)
                {
                    predWeights.WeightFactors[1].ChromaOffset[i * 2 + j] = (short)chromaOffsets[j];
                }
            }
        }
    }
}
