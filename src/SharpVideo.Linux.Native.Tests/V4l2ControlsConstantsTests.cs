using System;

using SharpVideo.Linux.Native.V4L2;

using Xunit;

namespace SharpVideo.Linux.Native.Tests;

public class V4l2ControlsConstantsTests
{
    [Fact]
    public void ControlClassesMatchNativeHeaders()
    {
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassUser, V4l2ControlsConstants.V4L2_CTRL_CLASS_USER);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassCodec, V4l2ControlsConstants.V4L2_CTRL_CLASS_CODEC);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassCamera, V4l2ControlsConstants.V4L2_CTRL_CLASS_CAMERA);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassFmTx, V4l2ControlsConstants.V4L2_CTRL_CLASS_FM_TX);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassFlash, V4l2ControlsConstants.V4L2_CTRL_CLASS_FLASH);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassJpeg, V4l2ControlsConstants.V4L2_CTRL_CLASS_JPEG);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassImageSource, V4l2ControlsConstants.V4L2_CTRL_CLASS_IMAGE_SOURCE);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassImageProc, V4l2ControlsConstants.V4L2_CTRL_CLASS_IMAGE_PROC);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassDv, V4l2ControlsConstants.V4L2_CTRL_CLASS_DV);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassFmRx, V4l2ControlsConstants.V4L2_CTRL_CLASS_FM_RX);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassRfTuner, V4l2ControlsConstants.V4L2_CTRL_CLASS_RF_TUNER);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassDetect, V4l2ControlsConstants.V4L2_CTRL_CLASS_DETECT);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassCodecStateless, V4l2ControlsConstants.V4L2_CTRL_CLASS_CODEC_STATELESS);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CtrlClassColorimetry, V4l2ControlsConstants.V4L2_CTRL_CLASS_COLORIMETRY);
    }

    [Fact]
    public void StatelessCodecControlIdsMatchNativeHeaders()
    {
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidCodecStatelessBase, V4l2ControlsConstants.V4L2_CID_CODEC_STATELESS_BASE);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidCodecStatelessClass, V4l2ControlsConstants.V4L2_CID_CODEC_STATELESS_CLASS);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264DecodeMode, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264StartCode, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_START_CODE);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264Sps, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264Pps, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264ScalingMatrix, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SCALING_MATRIX);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264PredWeights, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PRED_WEIGHTS);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264SliceParams, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS);
        AssertMatches(NativeTestLibrary.GetNativeV4L2CidStatelessH264DecodeParams, V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS);
    }

    private static void AssertMatches(Func<uint> nativeAccessor, uint managedValue)
    {
        var expected = nativeAccessor();
        var actual = managedValue;
        Assert.Equal(expected, actual);
    }
}
