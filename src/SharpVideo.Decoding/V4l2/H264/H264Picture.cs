using System.Runtime.Versioning;
using SharpVideo.H264;
using SharpVideo.Linux.Native.V4L2;
using SharpVideo.Utils;

namespace SharpVideo.Decoding.V4l2.H264;

/// <summary>
/// Represents an H.264 picture field type, matching GStreamer's GstH264PictureField.
/// </summary>
public enum H264PictureField
{
    /// <summary>Frame (non-interlaced) picture.</summary>
    Frame = 0,
    /// <summary>Top field picture.</summary>
    TopField = 1,
    /// <summary>Bottom field picture.</summary>
    BottomField = 2
}

/// <summary>
/// Represents an H.264 picture in the decoding process.
/// This mirrors GStreamer's GstH264Picture structure.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class H264Picture : IDisposable
{
    /// <summary>
    /// System frame number - a unique monotonically increasing number assigned to each picture.
    /// Used to generate reference timestamps.
    /// </summary>
    public uint SystemFrameNumber { get; init; }

    /// <summary>
    /// Reorder frame number for latency tracking.
    /// Following GStreamer's reorder_frame_number.
    /// </summary>
    public uint ReorderFrameNumber { get; set; }

    /// <summary>
    /// The frame_num from the slice header.
    /// </summary>
    public uint FrameNum { get; set; }

    /// <summary>
    /// FrameNumWrap - accounts for wrap-around in frame_num.
    /// Used for sliding window reference picture marking.
    /// Calculated as per H.264 spec 8.2.4.1.
    /// </summary>
    public int FrameNumWrap { get; set; }

    /// <summary>
    /// frame_num_offset for POC type 1 and 2.
    /// </summary>
    public int FrameNumOffset { get; set; }

    /// <summary>
    /// Picture number for short-term reference pictures.
    /// </summary>
    public int PicNum { get; set; }

    /// <summary>
    /// Long-term picture number for long-term reference pictures.
    /// </summary>
    public int LongTermPicNum { get; set; }

    /// <summary>
    /// Long-term frame index.
    /// </summary>
    public int LongTermFrameIdx { get; set; }

    /// <summary>
    /// NAL reference IDC value.
    /// </summary>
    public byte NalRefIdc { get; set; }

    /// <summary>
    /// Whether this is an IDR picture.
    /// </summary>
    public bool IsIdr { get; set; }

    /// <summary>
    /// IDR picture ID (for IDR pictures only).
    /// </summary>
    public uint IdrPicId { get; set; }

    /// <summary>
    /// Whether this is a reference picture.
    /// </summary>
    public bool IsRef { get; set; }

    /// <summary>
    /// Whether this is a long-term reference picture.
    /// </summary>
    public bool IsLongTermRef { get; set; }

    /// <summary>
    /// Whether this is a non-existing picture (used for gaps in frame_num).
    /// </summary>
    public bool IsNonExisting { get; set; }

    /// <summary>
    /// Whether this is a field picture.
    /// </summary>
    public bool FieldPicFlag { get; set; }

    /// <summary>
    /// Whether this is the bottom field (only valid when FieldPicFlag is true).
    /// </summary>
    public bool BottomFieldFlag { get; set; }

    /// <summary>
    /// Whether this is the second field of a complementary field pair.
    /// </summary>
    public bool SecondField { get; set; }

    /// <summary>
    /// Whether memory management operation 5 was performed on this picture.
    /// Following GStreamer's mem_mgmt_5.
    /// </summary>
    public bool MemMgmt5 { get; set; }

    /// <summary>
    /// Top field order count.
    /// </summary>
    public int TopFieldOrderCnt { get; set; }

    /// <summary>
    /// Bottom field order count.
    /// </summary>
    public int BottomFieldOrderCnt { get; set; }

    /// <summary>
    /// pic_order_cnt_msb for POC type 0.
    /// </summary>
    public int PicOrderCntMsb { get; set; }

    /// <summary>
    /// pic_order_cnt_lsb from slice header (for POC type 0).
    /// </summary>
    public int PicOrderCntLsb { get; set; }

    /// <summary>
    /// delta_pic_order_cnt_bottom from slice header.
    /// </summary>
    public int DeltaPicOrderCntBottom { get; set; }

    /// <summary>
    /// delta_pic_order_cnt[0] from slice header (for POC type 1).
    /// </summary>
    public int DeltaPicOrderCnt0 { get; set; }

    /// <summary>
    /// delta_pic_order_cnt[1] from slice header (for POC type 1).
    /// </summary>
    public int DeltaPicOrderCnt1 { get; set; }

    /// <summary>
    /// pic_order_cnt_type from SPS.
    /// </summary>
    public int PicOrderCntType { get; set; }

    /// <summary>
    /// The picture field type (frame, top, or bottom).
    /// </summary>
    public H264PictureField Field { get; set; } = H264PictureField.Frame;

    /// <summary>
    /// Reference to the other field for interlaced content.
    /// </summary>
    public H264Picture? OtherField { get; set; }

    /// <summary>
    /// The decoded frame buffer.
    /// </summary>
    public SharedDmaBuffer? Buffer { get; set; }

    /// <summary>
    /// Whether this picture has been outputted (removed from output queue).
    /// Following GStreamer's separate tracking from needed_for_output.
    /// </summary>
    public bool Outputted { get; set; }

    /// <summary>
    /// Whether this picture is needed for output (still in output queue).
    /// Following GStreamer's needed_for_output flag.
    /// This is set to true when picture is added to DPB and false when bumped.
    /// </summary>
    public bool NeededForOutput { get; set; }

    /// <summary>
    /// Whether this picture should be output.
    /// Used for intra refresh - pictures before recovery point may have this set to false.
    /// </summary>
    public bool OutputFlag { get; set; } = true;

    /// <summary>
    /// dec_ref_pic_marking data for reference picture marking.
    /// </summary>
    public DecRefPicMarkingState? DecRefPicMarking { get; set; }

    /// <summary>
    /// Get the reference timestamp in nanoseconds for V4L2 DPB.
    /// Following GStreamer convention: system_frame_number * 1000.
    /// </summary>
    public ulong ReferenceTs => (ulong)SystemFrameNumber * 1000;

    /// <summary>
    /// Get V4L2 DPB fields flag based on picture field type.
    /// </summary>
    public byte GetV4L2Fields()
    {
        return Field switch
        {
            H264PictureField.Frame => V4L2H264Constants.V4L2_H264_FRAME_REF,
            H264PictureField.TopField when OtherField != null =>
                (byte)(V4L2H264Constants.V4L2_H264_TOP_FIELD_REF | V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF),
            H264PictureField.TopField => V4L2H264Constants.V4L2_H264_TOP_FIELD_REF,
            H264PictureField.BottomField when OtherField != null =>
                (byte)(V4L2H264Constants.V4L2_H264_TOP_FIELD_REF | V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF),
            H264PictureField.BottomField => V4L2H264Constants.V4L2_H264_BOTTOM_FIELD_REF,
            _ => V4L2H264Constants.V4L2_H264_FRAME_REF
        };
    }

    /// <summary>
    /// Get the appropriate PicOrderCnt for this picture based on field type.
    /// </summary>
    public int GetPicOrderCnt()
    {
        return Field switch
        {
            H264PictureField.Frame => Math.Min(TopFieldOrderCnt, BottomFieldOrderCnt),
            H264PictureField.TopField => TopFieldOrderCnt,
            H264PictureField.BottomField => BottomFieldOrderCnt,
            _ => TopFieldOrderCnt
        };
    }

    /// <summary>
    /// Initialize picture from a parsed slice header.
    /// </summary>
    public void InitFromSliceHeader(SliceHeaderState header, SpsState sps, bool isIdr)
    {
        FrameNum = header.frame_num;
        NalRefIdc = (byte)header.nal_ref_idc;
        IsIdr = isIdr;
        IsRef = header.nal_ref_idc != 0;
        FieldPicFlag = header.field_pic_flag != 0;
        BottomFieldFlag = header.bottom_field_flag != 0;

        if (isIdr)
        {
            IdrPicId = header.idr_pic_id;
        }

        if (FieldPicFlag)
        {
            Field = BottomFieldFlag ? H264PictureField.BottomField : H264PictureField.TopField;
        }
        else
        {
            Field = H264PictureField.Frame;
        }

        // Store POC-related values from slice header
        PicOrderCntType = (int)sps.sps_data.pic_order_cnt_type;
        PicOrderCntLsb = (int)header.pic_order_cnt_lsb;
        DeltaPicOrderCntBottom = header.delta_pic_order_cnt_bottom;
        DeltaPicOrderCnt0 = header.delta_pic_order_cnt.Count > 0 ? header.delta_pic_order_cnt[0] : 0;
        DeltaPicOrderCnt1 = header.delta_pic_order_cnt.Count > 1 ? header.delta_pic_order_cnt[1] : 0;

        // Store dec_ref_pic_marking if present and adaptive mode
        if (header.dec_ref_pic_marking?.adaptive_ref_pic_marking_mode_flag != 0)
        {
            DecRefPicMarking = header.dec_ref_pic_marking;
        }
    }

    /// <summary>
    /// Create a field picture for the other field of a complementary field pair.
    /// Following GStreamer's gst_h264_decoder_new_field_picture.
    /// </summary>
    public H264Picture CreateComplementaryFieldPicture(uint systemFrameNumber)
    {
        var otherField = new H264Picture
        {
            SystemFrameNumber = systemFrameNumber,
            FrameNum = FrameNum,
            FrameNumWrap = FrameNumWrap,
            PicNum = PicNum,
            NalRefIdc = NalRefIdc,
            IsRef = IsRef,
            IsLongTermRef = IsLongTermRef,
            FieldPicFlag = FieldPicFlag,
            TopFieldOrderCnt = TopFieldOrderCnt,
            BottomFieldOrderCnt = BottomFieldOrderCnt,
            PicOrderCntMsb = PicOrderCntMsb,
            PicOrderCntLsb = PicOrderCntLsb,
            PicOrderCntType = PicOrderCntType,
            FrameNumOffset = FrameNumOffset,
            IsNonExisting = IsNonExisting,
            SecondField = true,
            OtherField = this
        };

        // Set the field type to the opposite of the current field
        otherField.Field = Field == H264PictureField.TopField
            ? H264PictureField.BottomField
            : H264PictureField.TopField;
        otherField.BottomFieldFlag = otherField.Field == H264PictureField.BottomField;

        // Link this picture to the other field
        OtherField = otherField;

        return otherField;
    }

    /// <summary>
    /// Split a frame picture into two field pictures for reference picture marking.
    /// Following GStreamer's gst_h264_decoder_split_frame.
    /// </summary>
    public H264Picture? SplitFrame(uint systemFrameNumber)
    {
        if (Field != H264PictureField.Frame)
        {
            return null;
        }

        var otherField = CreateComplementaryFieldPicture(systemFrameNumber);

        // Determine which field is first based on POC
        if (TopFieldOrderCnt < BottomFieldOrderCnt)
        {
            Field = H264PictureField.TopField;
            otherField.Field = H264PictureField.BottomField;
        }
        else
        {
            Field = H264PictureField.BottomField;
            otherField.Field = H264PictureField.TopField;
        }

        return otherField;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Buffer lifecycle is managed separately by the decoder
        Buffer = null;
        OtherField = null;
    }
}
