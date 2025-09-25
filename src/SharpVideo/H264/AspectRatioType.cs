namespace SharpVideo.H264;

public enum AspectRatioType : byte
{
    AR_UNSPECIFIED = 0,
    AR_1_1 = 1,      // 1:1 ("square")
    AR_12_11 = 2,    // 12:11
    AR_10_11 = 3,    // 10:11
    AR_16_11 = 4,    // 16:11
    AR_40_33 = 5,    // 40:33
    AR_24_11 = 6,    // 24:11
    AR_20_11 = 7,    // 20:11
    AR_32_11 = 8,    // 32:11
    AR_80_33 = 9,    // 80:33
    AR_18_11 = 10,   // 18:11
    AR_15_11 = 11,   // 15:11
    AR_64_33 = 12,   // 64:33
    AR_160_99 = 13,  // 160:99
    AR_4_3 = 14,     // 4:3
    AR_3_2 = 15,     // 3:2
    AR_2_1 = 16,     // 2:1
    // 17..254: Reserved
    AR_EXTENDED_SAR = 255  // Extended_SAR
}