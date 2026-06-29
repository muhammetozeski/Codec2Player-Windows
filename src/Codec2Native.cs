using System.Runtime.InteropServices;

namespace Codec2Player;

/// <summary>Codec 2 bitrate modes (values match codec2.h).</summary>
public enum Codec2Mode
{
    Mode3200 = 0,
    Mode2400 = 1,
    Mode1600 = 2,
    Mode1400 = 3,
    Mode1300 = 4,
    Mode1200 = 5,
    Mode700C = 8,
    Mode450 = 10,
    Mode450PWB = 11,
}

/// <summary>P/Invoke bindings for libcodec2.dll (built from the codec2 submodule, 450-capable).</summary>
internal static partial class Codec2Native
{
    private const string Dll = "libcodec2";

    [LibraryImport(Dll)]
    public static partial IntPtr codec2_create(int mode);

    [LibraryImport(Dll)]
    public static partial void codec2_destroy(IntPtr codec2State);

    [LibraryImport(Dll)]
    public static partial void codec2_decode(IntPtr codec2State, short[] speechOut, byte[] bits);

    [LibraryImport(Dll)]
    public static partial int codec2_samples_per_frame(IntPtr codec2State);

    [LibraryImport(Dll)]
    public static partial int codec2_bits_per_frame(IntPtr codec2State);

    public static string ModeName(int mode) => mode switch
    {
        0 => "3200",
        1 => "2400",
        2 => "1600",
        3 => "1400",
        4 => "1300",
        5 => "1200",
        8 => "700C",
        10 => "450",
        11 => "450PWB",
        _ => $"mode {mode}",
    };
}
