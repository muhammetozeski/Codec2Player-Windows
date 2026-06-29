namespace Codec2Player;

/// <summary>Result of decoding a .c2 file: raw little-endian 16-bit mono PCM plus metadata.</summary>
public sealed record DecodedAudio(byte[] Pcm, int Mode, int SampleRate, TimeSpan Duration);

/// <summary>Reads Codec 2 (.c2) files and decodes them to PCM via libcodec2.</summary>
public static class C2Decoder
{
    // Codec 2 file header: magic 0xC0 0xDE 0xC2, version_major, version_minor, mode, flags.
    private static readonly byte[] Magic = { 0xC0, 0xDE, 0xC2 };
    private const int HeaderSize = 7;

    public static DecodedAudio DecodeFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        if (data.Length < HeaderSize || data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2])
            throw new InvalidDataException(
                "Not a Codec 2 file with a header (expected magic C0 DE C2). " +
                "Only headered .c2 files are supported.");

        int mode = data[5];
        int offset = HeaderSize;
        int sampleRate = mode == (int)Codec2Mode.Mode450PWB ? 16000 : 8000;

        IntPtr c2 = Codec2Native.codec2_create(mode);
        if (c2 == IntPtr.Zero)
            throw new InvalidOperationException($"libcodec2 could not create a decoder for {Codec2Native.ModeName(mode)}.");

        try
        {
            int samplesPerFrame = Codec2Native.codec2_samples_per_frame(c2);
            int bitsPerFrame = Codec2Native.codec2_bits_per_frame(c2);
            int bytesPerFrame = (bitsPerFrame + 7) / 8;
            if (samplesPerFrame <= 0 || bytesPerFrame <= 0)
                throw new InvalidOperationException("libcodec2 reported an invalid frame size.");

            int frameCount = (data.Length - offset) / bytesPerFrame;
            var pcm = new short[frameCount * samplesPerFrame];

            var bits = new byte[bytesPerFrame];
            var speech = new short[samplesPerFrame];
            for (int i = 0; i < frameCount; i++)
            {
                Array.Copy(data, offset + i * bytesPerFrame, bits, 0, bytesPerFrame);
                Codec2Native.codec2_decode(c2, speech, bits);
                Array.Copy(speech, 0, pcm, i * samplesPerFrame, samplesPerFrame);
            }

            var pcmBytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, pcmBytes, 0, pcmBytes.Length);
            var duration = TimeSpan.FromSeconds((double)pcm.Length / sampleRate);
            return new DecodedAudio(pcmBytes, mode, sampleRate, duration);
        }
        finally
        {
            Codec2Native.codec2_destroy(c2);
        }
    }
}
