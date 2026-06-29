namespace Codec2Player;

/// <summary>Decoded audio: little-endian 16-bit mono PCM plus the source file's metadata.</summary>
/// <param name="Pcm">Decoded samples as little-endian 16-bit PCM bytes.</param>
/// <param name="Mode">The Codec 2 mode the file was encoded with (CODEC2_MODE_* value).</param>
/// <param name="SampleRate">Output sample rate in Hz (8000, or 16000 for 450PWB).</param>
/// <param name="Duration">Total playback length.</param>
sealed record DecodedAudio(byte[] Pcm, int Mode, int SampleRate, TimeSpan Duration);

/// <summary>Reads Codec 2 (.c2) files and decodes them to PCM through libcodec2.</summary>
static class C2Decoder
{
    // Codec 2 file header layout: magic (3 bytes) | version major | version minor | mode | flags.
    static readonly byte[] Magic = [0xC0, 0xDE, 0xC2];
    const int HeaderSize = 7;
    const int ModeOffset = 5;

    /// <summary>Decodes a headered .c2 file fully into memory.</summary>
    /// <param name="path">Path to a Codec 2 file that starts with the C0 DE C2 header.</param>
    /// <returns>The decoded PCM and its metadata.</returns>
    /// <exception cref="InvalidDataException">The file has no Codec 2 header.</exception>
    /// <exception cref="InvalidOperationException">libcodec2 rejected the mode or frame size.</exception>
    public static DecodedAudio DecodeFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        if (data.Length < HeaderSize || data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2])
            throw new InvalidDataException("Not a Codec 2 file with a header (expected magic C0 DE C2).");

        int mode = data[ModeOffset];
        int sampleRate = mode == (int)Codec2Mode.Mode450PWB ? 16000 : 8000;

        IntPtr codec = Codec2Native.codec2_create(mode);
        if (codec == IntPtr.Zero)
            throw new InvalidOperationException($"libcodec2 could not create a decoder for {Codec2Native.ModeName(mode)}.");

        try
        {
            int samplesPerFrame = Codec2Native.codec2_samples_per_frame(codec);
            int bytesPerFrame = (Codec2Native.codec2_bits_per_frame(codec) + 7) / 8;
            if (samplesPerFrame <= 0 || bytesPerFrame <= 0)
                throw new InvalidOperationException("libcodec2 reported an invalid frame size.");

            int frameCount = (data.Length - HeaderSize) / bytesPerFrame;
            short[] pcm = new short[frameCount * samplesPerFrame];
            byte[] bits = new byte[bytesPerFrame];
            short[] speech = new short[samplesPerFrame];

            for (int frame = 0; frame < frameCount; frame++)
            {
                Array.Copy(data, HeaderSize + frame * bytesPerFrame, bits, 0, bytesPerFrame);
                Codec2Native.codec2_decode(codec, speech, bits);
                Array.Copy(speech, 0, pcm, frame * samplesPerFrame, samplesPerFrame);
            }

            byte[] pcmBytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, pcmBytes, 0, pcmBytes.Length);
            return new DecodedAudio(pcmBytes, mode, sampleRate, TimeSpan.FromSeconds((double)pcm.Length / sampleRate));
        }
        finally
        {
            Codec2Native.codec2_destroy(codec);
        }
    }
}
