namespace SharpVideo.Decoding.V4l2.H264;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;

/// <summary>
/// Builds and modifies reference picture lists for H.264 decoding.
/// Constructs RefPicListP0, RefPicListB0, RefPicListB1 for P and B slices.
/// Implements both initial list construction (8.2.4.2) and list modification (8.2.4.3).
/// Following GStreamer's gst_h264_decoder_prepare_ref_pic_lists and
/// gst_h264_decoder_modify_ref_pic_lists.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class H264RefPicListBuilder
{
    private readonly ILogger? _logger;

    /// <summary>
    /// Maximum long-term frame index. Updated by MMCO operations.
    /// </summary>
    public int MaxLongTermFrameIdx { get; set; } = -1;

    public H264RefPicListBuilder(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reset state (called on IDR).
    /// </summary>
    public void Reset()
    {
        MaxLongTermFrameIdx = -1;
    }

    /// <summary>
    /// Update pic_num and long_term_pic_num for all pictures in DPB.
    /// Following GStreamer's gst_h264_decoder_update_pic_nums.
    /// </summary>
    public void UpdatePicNums(H264Dpb dpb, H264Picture currentPicture, int maxFrameNum)
    {
        foreach (var picture in dpb.GetPictures())
        {
            if (!picture.IsRef)
            {
                continue;
            }

            if (picture.IsLongTermRef)
            {
                // Long-term reference
                if (currentPicture.Field == H264PictureField.Frame)
                {
                    picture.LongTermPicNum = picture.LongTermFrameIdx;
                }
                else if (currentPicture.Field == picture.Field)
                {
                    picture.LongTermPicNum = 2 * picture.LongTermFrameIdx + 1;
                }
                else
                {
                    picture.LongTermPicNum = 2 * picture.LongTermFrameIdx;
                }
            }
            else
            {
                // Short-term reference
                if ((int)picture.FrameNum > (int)currentPicture.FrameNum)
                {
                    picture.FrameNumWrap = (int)picture.FrameNum - maxFrameNum;
                }
                else
                {
                    picture.FrameNumWrap = (int)picture.FrameNum;
                }

                if (currentPicture.Field == H264PictureField.Frame)
                {
                    picture.PicNum = picture.FrameNumWrap;
                }
                else if (picture.Field == currentPicture.Field)
                {
                    picture.PicNum = 2 * picture.FrameNumWrap + 1;
                }
                else
                {
                    picture.PicNum = 2 * picture.FrameNumWrap;
                }
            }
        }
    }

    /// <summary>
    /// Build RefPicListP0 for P slices.
    /// Following GStreamer's construct_ref_pic_lists_p (8.2.4.2.1).
    /// </summary>
    public List<H264Picture> BuildRefPicListP0(H264Dpb dpb)
    {
        var result = new List<H264Picture>();

        // Short-term refs sorted by descending pic_num
        var shortTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && !p.IsNonExisting)
            .OrderByDescending(p => p.PicNum)
            .ToList();

        result.AddRange(shortTermRefs);

        // Long-term refs sorted by ascending long_term_pic_num
        var longTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && p.IsLongTermRef && !p.IsNonExisting)
            .OrderBy(p => p.LongTermPicNum)
            .ToList();

        result.AddRange(longTermRefs);

        return result;
    }

    /// <summary>
    /// Build RefPicListB0 for B slices.
    /// Following GStreamer's construct_ref_pic_lists_b (8.2.4.2.3).
    /// </summary>
    public List<H264Picture> BuildRefPicListB0(H264Dpb dpb, H264Picture currentPicture)
    {
        var result = new List<H264Picture>();
        int currentPoc = currentPicture.GetPicOrderCnt();

        // Short-term refs with POC < current POC, sorted by descending POC
        var shortTermLower = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && !p.IsNonExisting && p.GetPicOrderCnt() < currentPoc)
            .OrderByDescending(p => p.GetPicOrderCnt())
            .ToList();

        result.AddRange(shortTermLower);

        // Short-term refs with POC > current POC, sorted by ascending POC
        var shortTermHigher = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && !p.IsNonExisting && p.GetPicOrderCnt() > currentPoc)
            .OrderBy(p => p.GetPicOrderCnt())
            .ToList();

        result.AddRange(shortTermHigher);

        // Long-term refs sorted by ascending long_term_pic_num
        var longTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && p.IsLongTermRef && !p.IsNonExisting)
            .OrderBy(p => p.LongTermPicNum)
            .ToList();

        result.AddRange(longTermRefs);

        return result;
    }

    /// <summary>
    /// Build RefPicListB1 for B slices.
    /// Following GStreamer's construct_ref_pic_lists_b (8.2.4.2.4).
    /// </summary>
    public List<H264Picture> BuildRefPicListB1(H264Dpb dpb, H264Picture currentPicture)
    {
        var result = new List<H264Picture>();
        int currentPoc = currentPicture.GetPicOrderCnt();

        // Short-term refs with POC > current POC, sorted by ascending POC
        var shortTermHigher = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && !p.IsNonExisting && p.GetPicOrderCnt() > currentPoc)
            .OrderBy(p => p.GetPicOrderCnt())
            .ToList();

        result.AddRange(shortTermHigher);

        // Short-term refs with POC < current POC, sorted by descending POC
        var shortTermLower = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && !p.IsNonExisting && p.GetPicOrderCnt() < currentPoc)
            .OrderByDescending(p => p.GetPicOrderCnt())
            .ToList();

        result.AddRange(shortTermLower);

        // Long-term refs sorted by ascending long_term_pic_num
        var longTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && p.IsLongTermRef && !p.IsNonExisting)
            .OrderBy(p => p.LongTermPicNum)
            .ToList();

        result.AddRange(longTermRefs);

        // If lists identical, swap first two entries (spec 8.2.4.2.3)
        var refPicListB0 = BuildRefPicListB0(dpb, currentPicture);
        if (result.Count > 1 && ListsAreEqual(result, refPicListB0))
        {
            (result[0], result[1]) = (result[1], result[0]);
        }

        return result;
    }

    private static bool ListsAreEqual(List<H264Picture> l1, List<H264Picture> l2)
    {
        if (l1.Count != l2.Count)
        {
            return false;
        }

        for (int i = 0; i < l1.Count; i++)
        {
            if (!ReferenceEquals(l1[i], l2[i]))
            {
                return false;
            }
        }

        return true;
    }

    // ============================================
    // Reference Picture List Modification (8.2.4.3)
    // Following GStreamer's modify_ref_pic_list
    // ============================================

    /// <summary>
    /// Modify reference picture lists based on slice header's ref_pic_list_modification.
    /// Following H.264 spec 8.2.4.3 and GStreamer's gst_h264_decoder_modify_ref_pic_lists.
    /// </summary>
    public (List<H264Picture> refPicList0, List<H264Picture> refPicList1) ModifyRefPicLists(
        H264Dpb dpb,
        H264Picture currentPicture,
        SliceHeaderState sliceHeader,
        int maxPicNum)
    {
        var refPicList0 = new List<H264Picture>();
        var refPicList1 = new List<H264Picture>();

        uint sliceType = sliceHeader.slice_type % 5;

        if (sliceType == 0 || sliceType == 3) // P or SP slice
        {
            // Build initial RefPicListP0
            var initialList = BuildRefPicListP0(dpb);
            refPicList0 = ModifyRefPicList(
                dpb, currentPicture, sliceHeader, 0, initialList, maxPicNum);
        }
        else if (sliceType == 1) // B slice
        {
            // Build initial RefPicListB0 and B1
            var initialList0 = BuildRefPicListB0(dpb, currentPicture);
            var initialList1 = BuildRefPicListB1(dpb, currentPicture);

            refPicList0 = ModifyRefPicList(
                dpb, currentPicture, sliceHeader, 0, initialList0, maxPicNum);
            refPicList1 = ModifyRefPicList(
                dpb, currentPicture, sliceHeader, 1, initialList1, maxPicNum);
        }

        return (refPicList0, refPicList1);
    }

    /// <summary>
    /// Modify a single reference picture list.
    /// Following H.264 spec 8.2.4.3 and GStreamer's modify_ref_pic_list.
    /// </summary>
    private List<H264Picture> ModifyRefPicList(
        H264Dpb dpb,
        H264Picture currentPicture,
        SliceHeaderState sliceHeader,
        int listNum,
        List<H264Picture> initialList,
        int maxPicNum)
    {
        // Get modification parameters from slice header
        var refPicListMod = sliceHeader.ref_pic_list_modification;
        if (refPicListMod == null)
        {
            return new List<H264Picture>(initialList);
        }

        bool modificationFlag = listNum == 0
            ? refPicListMod.ref_pic_list_modification_flag_l0 != 0
            : refPicListMod.ref_pic_list_modification_flag_l1 != 0;

        int numRefIdxActive = listNum == 0
            ? (int)sliceHeader.num_ref_idx_l0_active_minus1 + 1
            : (int)sliceHeader.num_ref_idx_l1_active_minus1 + 1;

        // Start with a copy of initial list, resize to active count
        var result = new List<H264Picture>(initialList);
        if (result.Count > numRefIdxActive)
        {
            result = result.Take(numRefIdxActive).ToList();
        }

        if (!modificationFlag || refPicListMod.modification_of_pic_nums_idc.Count == 0)
        {
            return result;
        }

        int picNumLxPred = currentPicture.PicNum;
        int refIdxLx = 0;
        int absIdx = 0;
        int longTermIdx = 0;

        foreach (var modIdc in refPicListMod.modification_of_pic_nums_idc)
        {
            if (modIdc == 3) // End of modification list
            {
                break;
            }

            if (modIdc == 0 || modIdc == 1)
            {
                // 8.2.4.3.1 - Short-term reference picture modification
                if (absIdx >= refPicListMod.abs_diff_pic_num_minus1.Count)
                {
                    break;
                }

                int absDiffPicNumMinus1 = (int)refPicListMod.abs_diff_pic_num_minus1[absIdx];
                absIdx++;

                int picNumLxNoWrap;

                if (modIdc == 0)
                {
                    // Subtract given value from predicted PicNum (8-34)
                    picNumLxNoWrap = picNumLxPred - (absDiffPicNumMinus1 + 1);
                    if (picNumLxNoWrap < 0)
                    {
                        picNumLxNoWrap += maxPicNum;
                    }
                }
                else
                {
                    // Add given value to predicted PicNum (8-35)
                    picNumLxNoWrap = picNumLxPred + (absDiffPicNumMinus1 + 1);
                    if (picNumLxNoWrap >= maxPicNum)
                    {
                        picNumLxNoWrap -= maxPicNum;
                    }
                }

                // For next iteration
                picNumLxPred = picNumLxNoWrap;

                // (8-36)
                int picNumLx = picNumLxNoWrap > currentPicture.PicNum
                    ? picNumLxNoWrap - maxPicNum
                    : picNumLxNoWrap;

                // Find the picture and insert it
                var pic = dpb.FindShortTermRef(picNumLx);
                if (pic == null)
                {
                    _logger?.LogWarning("Malformed stream, no short-term pic num {PicNum}", picNumLx);
                    continue;
                }

                // (8-37) - Shift and insert
                ShiftRightAndInsert(result, refIdxLx, numRefIdxActive - 1, pic);
                refIdxLx++;

                // Remove duplicate entries
                RemoveDuplicates(result, refIdxLx, numRefIdxActive, p => PicNumF(p, maxPicNum), picNumLx);
            }
            else if (modIdc == 2)
            {
                // 8.2.4.3.2 - Long-term reference picture modification
                if (longTermIdx >= refPicListMod.long_term_pic_num.Count)
                {
                    break;
                }

                int longTermPicNum = (int)refPicListMod.long_term_pic_num[longTermIdx];
                longTermIdx++;

                var pic = dpb.FindLongTermRef(longTermPicNum);
                if (pic == null)
                {
                    _logger?.LogWarning("Malformed stream, no long-term pic num {PicNum}", longTermPicNum);
                    continue;
                }

                // (8-28) - Shift and insert
                ShiftRightAndInsert(result, refIdxLx, numRefIdxActive - 1, pic);
                refIdxLx++;

                // Remove duplicate entries
                RemoveDuplicates(result, refIdxLx, numRefIdxActive,
                    p => LongTermPicNumF(p), longTermPicNum);
            }
        }

        // Resize back to active count (8.2.4.3.2 NOTE 2)
        if (result.Count > numRefIdxActive)
        {
            result = result.Take(numRefIdxActive).ToList();
        }

        return result;
    }

    /// <summary>
    /// Shift elements on the list from 'from' to 'to', inclusive, one position
    /// to the right and insert pic at 'from'.
    /// Following GStreamer's shift_right_and_insert.
    /// </summary>
    private static void ShiftRightAndInsert(List<H264Picture> list, int from, int to, H264Picture pic)
    {
        // Ensure list is large enough
        while (list.Count <= to + 1)
        {
            list.Add(null!);
        }

        // Shift right
        for (int i = to + 1; i > from; i--)
        {
            if (i < list.Count && i - 1 < list.Count)
            {
                list[i] = list[i - 1];
            }
        }

        // Insert
        list[from] = pic;
    }

    /// <summary>
    /// Remove duplicate entries from list after modification.
    /// </summary>
    private static void RemoveDuplicates(
        List<H264Picture> list,
        int startIdx,
        int numActive,
        Func<H264Picture, int> getPicNum,
        int targetPicNum)
    {
        int src = startIdx;
        int dst = startIdx;

        while (src <= numActive && src < list.Count)
        {
            var srcPic = list[src];
            int srcPicNum = srcPic != null ? getPicNum(srcPic) : -1;

            if (srcPicNum != targetPicNum)
            {
                if (dst < list.Count)
                {
                    list[dst] = srcPic;
                }
                dst++;
            }
            src++;
        }
    }

    /// <summary>
    /// pic_num_f function from H.264 spec.
    /// Returns MaxPicNum for long-term refs, pic_num for short-term refs.
    /// </summary>
    private static int PicNumF(H264Picture? picture, int maxPicNum)
    {
        if (picture == null)
        {
            return maxPicNum;
        }
        return picture.IsLongTermRef ? maxPicNum : picture.PicNum;
    }

    /// <summary>
    /// long_term_pic_num_f function from H.264 spec.
    /// Returns 2*(MaxLongTermFrameIdx+1) for short-term refs, long_term_pic_num for long-term refs.
    /// </summary>
    private int LongTermPicNumF(H264Picture? picture)
    {
        if (picture == null || !picture.IsLongTermRef)
        {
            return 2 * (MaxLongTermFrameIdx + 1);
        }
        return picture.LongTermPicNum;
    }

    // ============================================
    // Field picture reference list construction (8.2.4.2.5)
    // Following GStreamer's construct_ref_field_pic_lists_p/b
    // ============================================

    /// <summary>
    /// Build RefPicListP0 for field pictures (P slices).
    /// Following GStreamer's construct_ref_field_pic_lists_p.
    /// </summary>
    public List<H264Picture> BuildFieldRefPicListP0(H264Dpb dpb, H264Picture currentPicture)
    {
        // refFrameList0ShortTerm sorted by descending frame_num_wrap
        var shortTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef)
            .OrderByDescending(p => p.FrameNumWrap)
            .ToList();

        // refFrameListLongTerm sorted by ascending long_term_frame_idx
        var longTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && p.IsLongTermRef)
            .OrderBy(p => p.LongTermFrameIdx)
            .ToList();

        // Build list using init_picture_refs_fields_1 algorithm (8.2.4.2.5)
        var result = new List<H264Picture>();
        InitPictureRefsFields(currentPicture.Field, shortTermRefs, result);
        InitPictureRefsFields(currentPicture.Field, longTermRefs, result);

        return result;
    }

    /// <summary>
    /// Build RefPicListB0 for field pictures (B slices).
    /// Following GStreamer's construct_ref_field_pic_lists_b.
    /// </summary>
    public List<H264Picture> BuildFieldRefPicListB0(H264Dpb dpb, H264Picture currentPicture)
    {
        int currentPoc = currentPicture.GetPicOrderCnt();

        // refFrameList0ShortTerm: POC < current sorted desc, then POC > current sorted asc
        var shortTermLower = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && p.GetPicOrderCnt() < currentPoc)
            .OrderByDescending(p => p.GetPicOrderCnt())
            .ToList();

        var shortTermHigher = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && p.GetPicOrderCnt() > currentPoc)
            .OrderBy(p => p.GetPicOrderCnt())
            .ToList();

        var shortTermRefs = shortTermLower.Concat(shortTermHigher).ToList();

        // Long-term refs sorted by ascending long_term_frame_idx
        var longTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && p.IsLongTermRef)
            .OrderBy(p => p.LongTermFrameIdx)
            .ToList();

        var result = new List<H264Picture>();
        InitPictureRefsFields(currentPicture.Field, shortTermRefs, result);
        InitPictureRefsFields(currentPicture.Field, longTermRefs, result);

        return result;
    }

    /// <summary>
    /// Build RefPicListB1 for field pictures (B slices).
    /// Following GStreamer's construct_ref_field_pic_lists_b.
    /// </summary>
    public List<H264Picture> BuildFieldRefPicListB1(H264Dpb dpb, H264Picture currentPicture)
    {
        int currentPoc = currentPicture.GetPicOrderCnt();

        // refFrameList1ShortTerm: POC > current sorted asc, then POC < current sorted desc
        var shortTermHigher = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && p.GetPicOrderCnt() > currentPoc)
            .OrderBy(p => p.GetPicOrderCnt())
            .ToList();

        var shortTermLower = dpb.GetPictures()
            .Where(p => p.IsRef && !p.IsLongTermRef && p.GetPicOrderCnt() < currentPoc)
            .OrderByDescending(p => p.GetPicOrderCnt())
            .ToList();

        var shortTermRefs = shortTermHigher.Concat(shortTermLower).ToList();

        // Long-term refs sorted by ascending long_term_frame_idx
        var longTermRefs = dpb.GetPictures()
            .Where(p => p.IsRef && p.IsLongTermRef)
            .OrderBy(p => p.LongTermFrameIdx)
            .ToList();

        var result = new List<H264Picture>();
        InitPictureRefsFields(currentPicture.Field, shortTermRefs, result);
        InitPictureRefsFields(currentPicture.Field, longTermRefs, result);

        // If B0 and B1 identical, swap first two entries
        var b0 = BuildFieldRefPicListB0(dpb, currentPicture);
        if (result.Count > 1 && ListsAreEqual(result, b0))
        {
            (result[0], result[1]) = (result[1], result[0]);
        }

        return result;
    }

    /// <summary>
    /// Initialize picture refs for fields (8.2.4.2.5).
    /// Following GStreamer's init_picture_refs_fields_1.
    /// Alternates between same-field and opposite-field pictures.
    /// </summary>
    private static void InitPictureRefsFields(
        H264PictureField currentField,
        List<H264Picture> refFrameList,
        List<H264Picture> refPicList)
    {
        int i = 0, j = 0;

        while (i < refFrameList.Count || j < refFrameList.Count)
        {
            // Add pictures with same field as current
            for (; i < refFrameList.Count; i++)
            {
                var pic = refFrameList[i];
                if (pic.Field == currentField)
                {
                    refPicList.Add(pic);
                    i++;
                    break;
                }
            }

            // Add pictures with opposite field
            for (; j < refFrameList.Count; j++)
            {
                var pic = refFrameList[j];
                if (pic.Field != currentField)
                {
                    refPicList.Add(pic);
                    j++;
                    break;
                }
            }
        }
    }
}
