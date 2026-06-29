using System.Runtime.InteropServices;

namespace Codec2Player;

/// <summary>
/// Plays in-memory 16-bit mono PCM straight to the sound card via the Windows
/// winmm waveOut API - the same model as Android's AudioTrack: the codec is
/// decoded to PCM (see <see cref="C2Decoder"/>) and the PCM samples are handed
/// to the OS audio sink. No third-party audio library, no intermediate file.
/// </summary>
public sealed class WaveOutPlayer : IDisposable
{
    private IntPtr _hwo;
    private GCHandle _pcmHandle;
    private IntPtr _pHdr;
    private byte[] _pcm = Array.Empty<byte>();
    private int _sampleRate = 8000;
    private long _startByte;          // byte offset the current buffer was written from
    private bool _completedFired;

    public bool IsOpen { get; private set; }
    public bool IsPlaying { get; private set; }   // playing and not paused
    public bool IsPaused { get; private set; }

    public long TotalBytes => _pcm.Length;
    public int BytesPerSecond => _sampleRate * 2;
    public TimeSpan Total => TimeSpan.FromSeconds((double)TotalBytes / BytesPerSecond);
    public TimeSpan Current => TimeSpan.FromSeconds((double)PositionBytes / BytesPerSecond);

    private long PositionBytes
    {
        get
        {
            if (!IsOpen) return 0;
            var mmt = new MMTIME { wType = TIME_BYTES };
            waveOutGetPosition(_hwo, ref mmt, (uint)Marshal.SizeOf<MMTIME>());
            long pos = _startByte + mmt.u0;
            return Math.Min(pos, TotalBytes);
        }
    }

    /// <summary>Loads decoded PCM and writes it to the device in a paused state.</summary>
    public void Load(byte[] pcm, int sampleRate, float volume)
    {
        Stop();
        _pcm = pcm;
        _sampleRate = sampleRate;

        var fmt = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = 1,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = (uint)(sampleRate * 2),
            cbSize = 0,
        };
        Check(waveOutOpen(out _hwo, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL));
        IsOpen = true;
        SetVolume(volume);
        WriteFrom(0);
        waveOutPause(_hwo);   // queued but held until Play()
        IsPlaying = false;
        IsPaused = true;
        _completedFired = false;
    }

    private void WriteFrom(long byteOffset)
    {
        _pcmHandle = GCHandle.Alloc(_pcm, GCHandleType.Pinned);
        IntPtr data = _pcmHandle.AddrOfPinnedObject() + (int)byteOffset;

        var hdr = new WAVEHDR
        {
            lpData = data,
            dwBufferLength = (uint)(_pcm.Length - byteOffset),
            dwFlags = 0,
        };
        _pHdr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
        Marshal.StructureToPtr(hdr, _pHdr, false);

        Check(waveOutPrepareHeader(_hwo, _pHdr, (uint)Marshal.SizeOf<WAVEHDR>()));
        Check(waveOutWrite(_hwo, _pHdr, (uint)Marshal.SizeOf<WAVEHDR>()));
        _startByte = byteOffset;
    }

    private void ReleaseBuffer()
    {
        if (_pHdr != IntPtr.Zero)
        {
            waveOutUnprepareHeader(_hwo, _pHdr, (uint)Marshal.SizeOf<WAVEHDR>());
            Marshal.FreeHGlobal(_pHdr);
            _pHdr = IntPtr.Zero;
        }
        if (_pcmHandle.IsAllocated) _pcmHandle.Free();
    }

    public void Play()
    {
        if (!IsOpen) return;
        waveOutRestart(_hwo);
        IsPlaying = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsOpen) return;
        waveOutPause(_hwo);
        IsPlaying = false;
        IsPaused = true;
    }

    public void Stop()
    {
        if (!IsOpen) return;
        waveOutReset(_hwo);
        ReleaseBuffer();
        waveOutClose(_hwo);
        _hwo = IntPtr.Zero;
        IsOpen = false;
        IsPlaying = false;
        IsPaused = false;
    }

    public void SeekFraction(double fraction)
    {
        if (!IsOpen) return;
        fraction = Math.Clamp(fraction, 0, 1);
        long target = (long)(fraction * TotalBytes);
        target -= target % 2;                       // keep 16-bit sample alignment
        bool wasPlaying = IsPlaying;

        waveOutReset(_hwo);
        ReleaseBuffer();
        WriteFrom(target);
        if (wasPlaying) { waveOutRestart(_hwo); IsPlaying = true; IsPaused = false; }
        else { waveOutPause(_hwo); IsPlaying = false; IsPaused = true; }
        _completedFired = false;
    }

    public void SetVolume(float volume)
    {
        if (!IsOpen) return;
        uint v = (uint)(Math.Clamp(volume, 0f, 1f) * 0xFFFF);
        waveOutSetVolume(_hwo, (v << 16) | v);      // both channels
    }

    /// <summary>Returns true exactly once when playback reaches the end of the buffer.</summary>
    public bool PollCompleted()
    {
        if (!IsOpen || IsPaused || _completedFired) return false;
        if (PositionBytes >= TotalBytes && TotalBytes > 0)
        {
            _completedFired = true;
            IsPlaying = false;
            return true;
        }
        return false;
    }

    public void Dispose() => Stop();

    private static void Check(int mmResult)
    {
        if (mmResult != 0) throw new InvalidOperationException($"waveOut error {mmResult}.");
    }

    // ---- winmm interop ----

    private const ushort WAVE_FORMAT_PCM = 1;
    private const uint WAVE_MAPPER = 0xFFFFFFFF;
    private const uint CALLBACK_NULL = 0;
    private const uint TIME_BYTES = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MMTIME
    {
        public uint wType;
        public uint u0;
        public uint u1;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, uint uDeviceID, ref WAVEFORMATEX fmt, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern int waveOutPause(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutRestart(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutGetPosition(IntPtr hwo, ref MMTIME pmmt, uint cbmmt);
    [DllImport("winmm.dll")] private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);
}
