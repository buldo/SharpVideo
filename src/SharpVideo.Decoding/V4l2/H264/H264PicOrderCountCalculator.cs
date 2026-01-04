namespace SharpVideo.Decoding.V4l2.H264;

using SharpVideo.H264;

/// <summary>
/// Calculates PicOrderCnt (POC) for H.264 slices according to the specification.
/// </summary>
internal sealed class H264PicOrderCountCalculator
{
    private int _prevPicOrderCntMsb;
    private uint _prevPicOrderCntLsb;
    private int _prevFrameNumOffset;
    private uint _prevFrameNum;

    public void Reset()
    {
        _prevPicOrderCntMsb = 0;
        _prevPicOrderCntLsb = 0;
        _prevFrameNumOffset = 0;
        _prevFrameNum = 0;
    }

    public int CalculatePOC(SliceHeaderState header, SpsState sps, bool isIdr)
    {
        var spsData = sps.sps_data;

        if (isIdr)
        {
            Reset();
        }

        int picOrderCnt = 0;

        if (spsData.pic_order_cnt_type == 0)
        {
            uint maxPicOrderCntLsb = 1u << (int)(spsData.log2_max_pic_order_cnt_lsb_minus4 + 4);
            
            int prevPOCMsb = _prevPicOrderCntMsb;
            uint prevPOCLsb = _prevPicOrderCntLsb;

            int picOrderCntMsb;
            if ((header.pic_order_cnt_lsb < prevPOCLsb) &&
                ((prevPOCLsb - header.pic_order_cnt_lsb) >= (maxPicOrderCntLsb / 2)))
            {
                picOrderCntMsb = prevPOCMsb + (int)maxPicOrderCntLsb;
            }
            else if ((header.pic_order_cnt_lsb > prevPOCLsb) &&
                     ((header.pic_order_cnt_lsb - prevPOCLsb) > (maxPicOrderCntLsb / 2)))
            {
                picOrderCntMsb = prevPOCMsb - (int)maxPicOrderCntLsb;
            }
            else
            {
                picOrderCntMsb = prevPOCMsb;
            }

            picOrderCnt = picOrderCntMsb + (int)header.pic_order_cnt_lsb;

            if (header.nal_ref_idc != 0)
            {
                _prevPicOrderCntMsb = picOrderCntMsb;
                _prevPicOrderCntLsb = header.pic_order_cnt_lsb;
            }
        }
        else if (spsData.pic_order_cnt_type == 2)
        {
            uint maxFrameNum = 1u << (int)(spsData.log2_max_frame_num_minus4 + 4);
            int frameNumOffset;
            
            if (isIdr)
            {
                frameNumOffset = 0;
            }
            else if (_prevFrameNum > header.frame_num)
            {
                frameNumOffset = _prevFrameNumOffset + (int)maxFrameNum;
            }
            else
            {
                frameNumOffset = _prevFrameNumOffset;
            }

            if (isIdr)
            {
                picOrderCnt = 0;
            }
            else if (header.nal_ref_idc == 0)
            {
                picOrderCnt = 2 * (frameNumOffset + (int)header.frame_num) - 1;
            }
            else
            {
                picOrderCnt = 2 * (frameNumOffset + (int)header.frame_num);
            }

            _prevFrameNumOffset = frameNumOffset;
            _prevFrameNum = header.frame_num;
        }
        // pic_order_cnt_type 1 is omitted for simplicity as it is extremely rare in modern streams

        return picOrderCnt;
    }
}
