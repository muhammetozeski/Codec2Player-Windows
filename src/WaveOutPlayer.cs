using System.Runtime.InteropServices;

namespace Codec2Player;

/// <summary>
/// Streams in-memory 16-bit mono PCM to the sound card through the Windows winmm
/// waveOut API. The whole clip is written as a single buffer; play / pause / seek /
/// volume map onto waveOut restart / pause / rewrite / SetVolume.
/// </summary>
sealed class WaveOutPlayer : IDisposable
{
    const uint VolumeMax = 0xFFFF;

    IntPtr _deviceHandle;
    GCHandle _pcmPin;
    IntPtr _headerPtr;
    byte[] _pcm = [];
    int _sampleRate = 8000;
    long _bufferStartByte;          // byte offset the current buffer was written from
    bool _completedFired;

    /// <summary>True once a device is open and a clip is loaded.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True while audio is actively playing (open and not paused).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>True while loaded but paused.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Total length of the loaded clip.</summary>
    public TimeSpan Total => TimeSpan.FromSeconds((double)_pcm.Length / (_sampleRate * 2));

    /// <summary>Current playback position.</summary>
    public TimeSpan Current => TimeSpan.FromSeconds((double)PositionBytes / (_sampleRate * 2));

    long PositionBytes
    {
        get
        {
            if (!IsOpen) return 0;
            MmTime time = new() { Type = TimeBytes };
            waveOutGetPosition(_deviceHandle, ref time, (uint)Marshal.SizeOf<MmTime>());
            return Math.Min(_bufferStartByte + time.Value, _pcm.Length);
        }
    }

    /// <summary>Opens the audio device and queues the clip in a paused state.</summary>
    /// <param name="pcm">Little-endian 16-bit mono PCM samples.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="volume">Initial volume from 0 to 1.</param>
    public void Load(byte[] pcm, int sampleRate, float volume)
    {
        Stop();
        _pcm = pcm;
        _sampleRate = sampleRate;

        WaveFormat format = new()
        {
            FormatTag = WaveFormatPcm,
            Channels = 1,
            SamplesPerSecond = (uint)sampleRate,
            BitsPerSample = 16,
            BlockAlign = 2,
            AverageBytesPerSecond = (uint)(sampleRate * 2),
        };
        Check(waveOutOpen(out _deviceHandle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull));
        IsOpen = true;
        SetVolume(volume);
        WriteFrom(0);
        waveOutPause(_deviceHandle);
        IsPaused = true;
        _completedFired = false;
    }

    /// <summary>Resumes (or starts) playback.</summary>
    public void Play()
    {
        if (!IsOpen) return;
        waveOutRestart(_deviceHandle);
        IsPlaying = true;
        IsPaused = false;
    }

    /// <summary>Pauses playback, keeping the position.</summary>
    public void Pause()
    {
        if (!IsOpen) return;
        waveOutPause(_deviceHandle);
        IsPlaying = false;
        IsPaused = true;
    }

    /// <summary>Stops playback and releases the device and buffer.</summary>
    public void Stop()
    {
        if (!IsOpen) return;
        waveOutReset(_deviceHandle);
        ReleaseBuffer();
        waveOutClose(_deviceHandle);
        _deviceHandle = IntPtr.Zero;
        IsOpen = false;
        IsPlaying = false;
        IsPaused = false;
    }

    /// <summary>Jumps to a position given as a fraction of the total length.</summary>
    /// <param name="fraction">Target position from 0 (start) to 1 (end).</param>
    public void SeekFraction(double fraction)
    {
        if (!IsOpen) return;
        long target = (long)(Math.Clamp(fraction, 0, 1) * _pcm.Length);
        target -= target % 2;                       // keep 16-bit sample alignment
        bool wasPlaying = IsPlaying;

        waveOutReset(_deviceHandle);
        ReleaseBuffer();
        WriteFrom(target);
        if (wasPlaying) Play();
        else { waveOutPause(_deviceHandle); IsPaused = true; }
        _completedFired = false;
    }

    /// <summary>Sets the output volume.</summary>
    /// <param name="volume">Volume from 0 (silent) to 1 (full).</param>
    public void SetVolume(float volume)
    {
        if (!IsOpen) return;
        uint level = (uint)(Math.Clamp(volume, 0f, 1f) * VolumeMax);
        waveOutSetVolume(_deviceHandle, (level << 16) | level);
    }

    /// <summary>Reports end-of-clip exactly once, so a caller can auto-advance.</summary>
    /// <returns>True on the first poll after playback reaches the end.</returns>
    public bool PollCompleted()
    {
        if (!IsOpen || IsPaused || _completedFired || _pcm.Length == 0) return false;
        if (PositionBytes < _pcm.Length) return false;
        _completedFired = true;
        IsPlaying = false;
        return true;
    }

    /// <summary>Stops playback and frees all native resources.</summary>
    public void Dispose() => Stop();

    /// <summary>Pins the PCM buffer and submits it to the device starting at a byte offset.</summary>
    /// <param name="byteOffset">Offset into the PCM buffer to start playback from.</param>
    void WriteFrom(long byteOffset)
    {
        _pcmPin = GCHandle.Alloc(_pcm, GCHandleType.Pinned);
        int headerSize = Marshal.SizeOf<WaveHeader>();
        WaveHeader header = new()
        {
            Data = _pcmPin.AddrOfPinnedObject() + (int)byteOffset,
            BufferLength = (uint)(_pcm.Length - byteOffset),
        };
        _headerPtr = Marshal.AllocHGlobal(headerSize);
        Marshal.StructureToPtr(header, _headerPtr, false);

        Check(waveOutPrepareHeader(_deviceHandle, _headerPtr, (uint)headerSize));
        Check(waveOutWrite(_deviceHandle, _headerPtr, (uint)headerSize));
        _bufferStartByte = byteOffset;
    }

    /// <summary>Unprepares and frees the current buffer header and unpins the PCM.</summary>
    void ReleaseBuffer()
    {
        if (_headerPtr != IntPtr.Zero)
        {
            waveOutUnprepareHeader(_deviceHandle, _headerPtr, (uint)Marshal.SizeOf<WaveHeader>());
            Marshal.FreeHGlobal(_headerPtr);
            _headerPtr = IntPtr.Zero;
        }
        if (_pcmPin.IsAllocated) _pcmPin.Free();
    }

    /// <summary>Throws if a winmm call returned a non-zero (error) result.</summary>
    /// <param name="result">The MMRESULT returned by a waveOut function.</param>
    static void Check(int result)
    {
        if (result != 0) throw new InvalidOperationException($"waveOut error {result}.");
    }

    #region winmm interop (1:1 Win32 waveOut bindings)

    const ushort WaveFormatPcm = 1;
    const uint WaveMapper = 0xFFFFFFFF;
    const uint CallbackNull = 0;
    const uint TimeBytes = 0x0004;

    /// <summary>Win32 WAVEFORMATEX.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    /// <summary>Win32 WAVEHDR.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    /// <summary>Win32 MMTIME; only the byte-count member of the union is used here.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint Padding;
    }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr deviceHandle, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint openFlags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr deviceHandle, IntPtr header, uint headerSize);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr deviceHandle, IntPtr header, uint headerSize);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr deviceHandle, IntPtr header, uint headerSize);
    [DllImport("winmm.dll")] static extern int waveOutPause(IntPtr deviceHandle);
    [DllImport("winmm.dll")] static extern int waveOutRestart(IntPtr deviceHandle);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr deviceHandle);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr deviceHandle);
    [DllImport("winmm.dll")] static extern int waveOutGetPosition(IntPtr deviceHandle, ref MmTime time, uint timeSize);
    [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr deviceHandle, uint volume);

    #endregion
}
