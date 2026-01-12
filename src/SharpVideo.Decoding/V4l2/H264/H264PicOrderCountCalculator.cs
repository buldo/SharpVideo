namespace SharpVideo.Decoding.V4l2.H264;

using System.Runtime.Versioning;
using SharpVideo.H264;

/// <summary>
/// Calculates PicOrderCnt (POC) for H.264 slices according to the specification.
/// Following GStreamer's gsth264decoder.c POC calculation logic.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class H264PicOrderCountCalculator
{
    // Values related to previously decoded reference picture (matching GStreamer)
    private bool _prevRefHasMemmgmnt5;
    private int _prevRefTopFieldOrderCnt;
    private int _prevRefPicOrderCntMsb;
    private int _prevRefPicOrderCntLsb;
    private H264PictureField _prevRefField;

    // Previous frame state
    private int _prevFrameNumOffset;
    private uint _prevFrameNum;
    private bool _prevHasMemmgmnt5;

    // Last calculated PicOrderCntMsb for POC type 0
    // Needed to store in picture for UpdateAfterPicture
    private int _lastPicOrderCntMsb;

    /// <summary>
    /// Gets the last calculated PicOrderCntMsb.
    /// Should be stored in the picture for UpdateAfterPicture.
    /// </summary>
    public int LastPicOrderCntMsb => _lastPicOrderCntMsb;

    public void Reset()
    {
        _prevRefHasMemmgmnt5 = false;
        _prevRefTopFieldOrderCnt = 0;
        _prevRefPicOrderCntMsb = 0;
        _prevRefPicOrderCntLsb = 0;
        _prevRefField = H264PictureField.Frame;
        _prevFrameNumOffset = 0;
        _prevFrameNum = 0;
        _prevHasMemmgmnt5 = false;
        _lastPicOrderCntMsb = 0;
    }

    /// <summary>
    /// Update the POC calculator state after a picture has been processed.
    /// This must be called after reference picture marking to properly handle MMCO 5.
    /// Following GStreamer's update of prev_ref_has_memmgmnt5.
    /// </summary>
    public void UpdateAfterPicture(H264Picture picture)
    {
        if (picture.IsRef)
        {
            _prevRefHasMemmgmnt5 = picture.MemMgmt5;
            _prevRefTopFieldOrderCnt = picture.TopFieldOrderCnt;
            _prevRefField = picture.Field;

            // For POC type 0: update pic_order_cnt_lsb/msb state
            // These values are used for the next picture's POC calculation
            // Following GStreamer's gst_h264_decoder_finish_current_picture
            _prevRefPicOrderCntLsb = picture.PicOrderCntLsb;
            _prevRefPicOrderCntMsb = picture.PicOrderCntMsb;
        }
        _prevHasMemmgmnt5 = picture.MemMgmt5;
    }

    /// <summary>
    /// Calculate POC and set TopFieldOrderCnt/BottomFieldOrderCnt on the picture.
    /// Returns the top field order count for backward compatibility.
    /// </summary>
    public int CalculatePOC(SliceHeaderState header, SpsState sps, bool isIdr)
    {
        return CalculatePOC(header, sps, isIdr, H264PictureField.Frame, out _, out _);
    }

    /// <summary>
    /// Calculate POC for H.264 picture following GStreamer's logic.
    /// </summary>
    public int CalculatePOC(
        SliceHeaderState header,
        SpsState sps,
        bool isIdr,
        H264PictureField field,
        out int topFieldOrderCnt,
        out int bottomFieldOrderCnt)
    {
        var spsData = sps.sps_data;
        topFieldOrderCnt = 0;
        bottomFieldOrderCnt = 0;

        switch (spsData.pic_order_cnt_type)
        {
            case 0:
                CalculatePocType0(header, spsData, isIdr, field, out topFieldOrderCnt, out bottomFieldOrderCnt);
                break;

            case 1:
                CalculatePocType1(header, spsData, isIdr, field, out topFieldOrderCnt, out bottomFieldOrderCnt);
                break;

            case 2:
                CalculatePocType2(header, spsData, isIdr, field, out topFieldOrderCnt, out bottomFieldOrderCnt);
                break;
        }

        // Determine pic_order_cnt based on field (following GStreamer 8.2.1 step 6)
        int picOrderCnt;
        switch (field)
        {
            case H264PictureField.Frame:
                picOrderCnt = Math.Min(topFieldOrderCnt, bottomFieldOrderCnt);
                break;
            case H264PictureField.TopField:
                picOrderCnt = topFieldOrderCnt;
                break;
            case H264PictureField.BottomField:
                picOrderCnt = bottomFieldOrderCnt;
                break;
            default:
                picOrderCnt = topFieldOrderCnt;
                break;
        }

        return picOrderCnt;
    }

    private void CalculatePocType0(
        SliceHeaderState header,
        SpsDataState spsData,
        bool isIdr,
        H264PictureField field,
        out int topFieldOrderCnt,
        out int bottomFieldOrderCnt)
    {
        // See spec 8.2.1.1
        int prevPicOrderCntMsb;
        int prevPicOrderCntLsb;

        if (isIdr)
        {
            prevPicOrderCntMsb = 0;
            prevPicOrderCntLsb = 0;
        }
        else
        {
            if (_prevRefHasMemmgmnt5)
            {
                if (_prevRefField != H264PictureField.BottomField)
                {
                    prevPicOrderCntMsb = 0;
                    prevPicOrderCntLsb = _prevRefTopFieldOrderCnt;
                }
                else
                {
                    prevPicOrderCntMsb = 0;
                    prevPicOrderCntLsb = 0;
                }
            }
            else
            {
                prevPicOrderCntMsb = _prevRefPicOrderCntMsb;
                prevPicOrderCntLsb = _prevRefPicOrderCntLsb;
            }
        }

        int maxPicOrderCntLsb = 1 << (int)(spsData.log2_max_pic_order_cnt_lsb_minus4 + 4);

        int picOrderCntMsb;
        if ((header.pic_order_cnt_lsb < prevPicOrderCntLsb) &&
            (prevPicOrderCntLsb - (int)header.pic_order_cnt_lsb >= maxPicOrderCntLsb / 2))
        {
            picOrderCntMsb = prevPicOrderCntMsb + maxPicOrderCntLsb;
        }
        else if ((header.pic_order_cnt_lsb > prevPicOrderCntLsb) &&
                 ((int)header.pic_order_cnt_lsb - prevPicOrderCntLsb > maxPicOrderCntLsb / 2))
        {
            picOrderCntMsb = prevPicOrderCntMsb - maxPicOrderCntLsb;
        }
        else
        {
            picOrderCntMsb = prevPicOrderCntMsb;
        }

        // Calculate field order counts based on field type (matching GStreamer exactly)
        switch (field)
        {
            case H264PictureField.Frame:
                topFieldOrderCnt = picOrderCntMsb + (int)header.pic_order_cnt_lsb;
                bottomFieldOrderCnt = topFieldOrderCnt + header.delta_pic_order_cnt_bottom;
                break;
            case H264PictureField.TopField:
                topFieldOrderCnt = picOrderCntMsb + (int)header.pic_order_cnt_lsb;
                bottomFieldOrderCnt = 0; // Will be set when paired with bottom field
                break;
            case H264PictureField.BottomField:
                topFieldOrderCnt = 0; // Will be set when paired with top field
                bottomFieldOrderCnt = picOrderCntMsb + (int)header.pic_order_cnt_lsb;
                break;
            default:
                topFieldOrderCnt = picOrderCntMsb + (int)header.pic_order_cnt_lsb;
                bottomFieldOrderCnt = topFieldOrderCnt;
                break;
        }

        // Store PicOrderCntMsb for later use in UpdateAfterPicture
        // This is needed because MMCO operations may modify POC values
        _lastPicOrderCntMsb = picOrderCntMsb;
    }

    private void CalculatePocType1(
        SliceHeaderState header,
        SpsDataState spsData,
        bool isIdr,
        H264PictureField field,
        out int topFieldOrderCnt,
        out int bottomFieldOrderCnt)
    {
        // See spec 8.2.1.2
        if (_prevHasMemmgmnt5)
        {
            _prevFrameNumOffset = 0;
        }

        int frameNumOffset;
        int maxFrameNum = 1 << (int)(spsData.log2_max_frame_num_minus4 + 4);

        if (isIdr)
        {
            frameNumOffset = 0;
        }
        else if (_prevFrameNum > header.frame_num)
        {
            frameNumOffset = _prevFrameNumOffset + maxFrameNum;
        }
        else
        {
            frameNumOffset = _prevFrameNumOffset;
        }

        int absFrameNum = 0;
        if (spsData.num_ref_frames_in_pic_order_cnt_cycle != 0)
        {
            absFrameNum = frameNumOffset + (int)header.frame_num;
        }

        if (header.nal_ref_idc == 0 && absFrameNum > 0)
        {
            absFrameNum--;
        }

        int expectedPicOrderCnt = 0;
        if (absFrameNum > 0)
        {
            int expectedDeltaPerPicOrderCntCycle = 0;
            for (int i = 0; i < spsData.num_ref_frames_in_pic_order_cnt_cycle; i++)
            {
                expectedDeltaPerPicOrderCntCycle += spsData.offset_for_ref_frame[i];
            }

            int picOrderCntCycleCnt = (absFrameNum - 1) / (int)spsData.num_ref_frames_in_pic_order_cnt_cycle;
            int frameNumInPicOrderCntCycle = (absFrameNum - 1) % (int)spsData.num_ref_frames_in_pic_order_cnt_cycle;

            expectedPicOrderCnt = picOrderCntCycleCnt * expectedDeltaPerPicOrderCntCycle;
            for (int i = 0; i <= frameNumInPicOrderCntCycle; i++)
            {
                expectedPicOrderCnt += spsData.offset_for_ref_frame[i];
            }
        }

        if (header.nal_ref_idc == 0)
        {
            expectedPicOrderCnt += spsData.offset_for_non_ref_pic;
        }

        switch (field)
        {
            case H264PictureField.Frame:
                topFieldOrderCnt = expectedPicOrderCnt +
                    (header.delta_pic_order_cnt.Count > 0 ? header.delta_pic_order_cnt[0] : 0);
                bottomFieldOrderCnt = topFieldOrderCnt + spsData.offset_for_top_to_bottom_field +
                    (header.delta_pic_order_cnt.Count > 1 ? header.delta_pic_order_cnt[1] : 0);
                break;
            case H264PictureField.TopField:
                topFieldOrderCnt = expectedPicOrderCnt +
                    (header.delta_pic_order_cnt.Count > 0 ? header.delta_pic_order_cnt[0] : 0);
                bottomFieldOrderCnt = 0;
                break;
            case H264PictureField.BottomField:
                topFieldOrderCnt = 0;
                bottomFieldOrderCnt = expectedPicOrderCnt + spsData.offset_for_top_to_bottom_field +
                    (header.delta_pic_order_cnt.Count > 0 ? header.delta_pic_order_cnt[0] : 0);
                break;
            default:
                topFieldOrderCnt = expectedPicOrderCnt;
                bottomFieldOrderCnt = expectedPicOrderCnt;
                break;
        }

        _prevFrameNumOffset = frameNumOffset;
        _prevFrameNum = header.frame_num;
    }

    private void CalculatePocType2(
        SliceHeaderState header,
        SpsDataState spsData,
        bool isIdr,
        H264PictureField field,
        out int topFieldOrderCnt,
        out int bottomFieldOrderCnt)
    {
        // See spec 8.2.1.3
        if (_prevHasMemmgmnt5)
        {
            _prevFrameNumOffset = 0;
        }

        int frameNumOffset;
        int maxFrameNum = 1 << (int)(spsData.log2_max_frame_num_minus4 + 4);

        if (isIdr)
        {
            frameNumOffset = 0;
        }
        else if (_prevFrameNum > header.frame_num)
        {
            frameNumOffset = _prevFrameNumOffset + maxFrameNum;
        }
        else
        {
            frameNumOffset = _prevFrameNumOffset;
        }

        int tempPicOrderCnt;
        if (isIdr)
        {
            tempPicOrderCnt = 0;
        }
        else if (header.nal_ref_idc == 0)
        {
            tempPicOrderCnt = 2 * (frameNumOffset + (int)header.frame_num) - 1;
        }
        else
        {
            tempPicOrderCnt = 2 * (frameNumOffset + (int)header.frame_num);
        }

        switch (field)
        {
            case H264PictureField.Frame:
                topFieldOrderCnt = tempPicOrderCnt;
                bottomFieldOrderCnt = tempPicOrderCnt;
                break;
            case H264PictureField.TopField:
                topFieldOrderCnt = tempPicOrderCnt;
                bottomFieldOrderCnt = 0;
                break;
            case H264PictureField.BottomField:
                topFieldOrderCnt = 0;
                bottomFieldOrderCnt = tempPicOrderCnt;
                break;
            default:
                topFieldOrderCnt = tempPicOrderCnt;
                bottomFieldOrderCnt = tempPicOrderCnt;
                break;
        }

        _prevFrameNumOffset = frameNumOffset;
        _prevFrameNum = header.frame_num;
    }
}
