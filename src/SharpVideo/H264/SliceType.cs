namespace SharpVideo.H264;

/// <summary>
/// Table 7-3
/// </summary>
enum SliceType : byte
{
    P = 0,
    B = 1,
    I = 2,
    SP = 3,
    SI = 4,
    // slice_type values in the range 5..9 specify, in addition to the coding
    // type of the current slice, that all other slices of the current coded
    // picture shall have a value of slice_type equal to the current value of
    // slice_type or equal to the current value of slice_type - 5.
    P_ALL = 5,
    B_ALL = 6,
    I_ALL = 7,
    SP_ALL = 8,
    SI_ALL = 9,
}