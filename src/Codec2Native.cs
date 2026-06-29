using System.Runtime.InteropServices;

namespace Codec2Player;

/// <summary>Codec 2 bitrate modes. Values match the CODEC2_MODE_* constants in codec2.h.</summary>
enum Codec2Mode
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

/// <summary>P/Invoke bindings for libcodec2.dll and the mode-name lookup.</summary>
static partial class Codec2Native
{
    const string Dll = "libcodec2";

    /// <summary>Allocates a Codec 2 decoder/encoder for the given mode.</summary>
    /// <param name="mode">A <see cref="Codec2Mode"/> value.</param>
    /// <returns>An opaque codec state handle, or <see cref="IntPtr.Zero"/> on failure.</returns>
    [LibraryImport(Dll)]
    public static partial IntPtr codec2_create(int mode);

    /// <summary>Frees a codec state previously returned by <see cref="codec2_create"/>.</summary>
    /// <param name="codec2State">The codec state handle to free.</param>
    [LibraryImport(Dll)]
    public static partial void codec2_destroy(IntPtr codec2State);

    /// <summary>Decodes one frame of packed Codec 2 bits into 16-bit PCM samples.</summary>
    /// <param name="codec2State">The codec state handle.</param>
    /// <param name="speechOut">Output buffer that receives <see cref="codec2_samples_per_frame"/> samples.</param>
    /// <param name="bits">Input buffer holding one frame of packed bits.</param>
    [LibraryImport(Dll)]
    public static partial void codec2_decode(IntPtr codec2State, short[] speechOut, byte[] bits);

    /// <summary>Number of PCM samples produced per decoded frame for the codec's mode.</summary>
    /// <param name="codec2State">The codec state handle.</param>
    /// <returns>Samples per frame.</returns>
    [LibraryImport(Dll)]
    public static partial int codec2_samples_per_frame(IntPtr codec2State);

    /// <summary>Number of bits in one encoded frame for the codec's mode.</summary>
    /// <param name="codec2State">The codec state handle.</param>
    /// <returns>Bits per frame (byte size is this rounded up to a byte boundary).</returns>
    [LibraryImport(Dll)]
    public static partial int codec2_bits_per_frame(IntPtr codec2State);

    /// <summary>Maps a Codec 2 mode value to its short display name (e.g. 8 to "700C").</summary>
    /// <param name="mode">A CODEC2_MODE_* value as stored in a .c2 file header.</param>
    /// <returns>The mode's display name, or "mode N" for an unknown value.</returns>
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
