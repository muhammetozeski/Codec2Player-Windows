namespace Codec2Player;

public sealed class MainForm : Form
{
    private readonly ListBox _playlist = new();
    private readonly Button _btnAddFiles = new() { Text = "Dosya Ekle" };
    private readonly Button _btnAddFolder = new() { Text = "Klasör Ekle" };
    private readonly Button _btnRemove = new() { Text = "Çıkar" };
    private readonly Button _btnClear = new() { Text = "Temizle" };

    private readonly Label _lblNow = new() { Text = "—", AutoEllipsis = true };
    private readonly TrackBar _seek = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Enabled = false };
    private readonly Label _lblTime = new() { Text = "00:00 / 00:00", TextAlign = ContentAlignment.MiddleRight };

    private readonly Button _btnPrev = new() { Text = "⏮" };
    private readonly Button _btnPlay = new() { Text = "▶" };
    private readonly Button _btnStop = new() { Text = "⏹" };
    private readonly Button _btnNext = new() { Text = "⏭" };
    private readonly Label _lblVol = new() { Text = "🔊", TextAlign = ContentAlignment.MiddleCenter };
    private readonly TrackBar _volume = new() { Minimum = 0, Maximum = 100, Value = 90, TickStyle = TickStyle.None };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly WaveOutPlayer _player = new();

    private int _current = -1;
    private bool _suppressSeek;
    private float _gain = 0.9f;

    public MainForm()
    {
        Text = "Codec2Player";
        Width = 660;
        Height = 480;
        MinimumSize = new Size(520, 380);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        WireEvents();
        UpdateTransport();
    }

    private void BuildUi()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0) };
        foreach (var b in new[] { _btnAddFiles, _btnAddFolder, _btnRemove, _btnClear })
        {
            b.AutoSize = true;
            b.Margin = new Padding(0, 0, 6, 0);
            toolbar.Controls.Add(b);
        }

        _playlist.Dock = DockStyle.Fill;
        _playlist.IntegralHeight = false;
        _playlist.Font = new Font(FontFamily.GenericSansSerif, 9.5f);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 132,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new Padding(10, 6, 10, 10),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        _lblNow.Dock = DockStyle.Fill;
        _lblNow.TextAlign = ContentAlignment.MiddleLeft;
        _lblNow.Font = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold);
        bottom.Controls.Add(_lblNow, 0, 0);
        bottom.SetColumnSpan(_lblNow, 3);

        _seek.Dock = DockStyle.Fill;
        bottom.Controls.Add(_seek, 0, 1);
        _lblTime.Dock = DockStyle.Fill;
        bottom.Controls.Add(_lblTime, 1, 1);
        bottom.SetColumnSpan(_lblTime, 2);

        var transport = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var b in new[] { _btnPrev, _btnPlay, _btnStop, _btnNext })
        {
            b.Width = 48;
            b.Height = 34;
            b.Font = new Font(FontFamily.GenericSansSerif, 11f);
            b.Margin = new Padding(0, 0, 6, 0);
            transport.Controls.Add(b);
        }
        bottom.Controls.Add(transport, 0, 2);

        var volPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _lblVol.Width = 24;
        _lblVol.Height = 34;
        _volume.Width = 200;
        volPanel.Controls.Add(_lblVol);
        volPanel.Controls.Add(_volume);
        bottom.Controls.Add(volPanel, 1, 2);
        bottom.SetColumnSpan(volPanel, 2);

        Controls.Add(_playlist);
        Controls.Add(bottom);
        Controls.Add(toolbar);
    }

    private void WireEvents()
    {
        _btnAddFiles.Click += (_, _) => AddFiles();
        _btnAddFolder.Click += (_, _) => AddFolder();
        _btnRemove.Click += (_, _) => RemoveSelected();
        _btnClear.Click += (_, _) => { Stop(); _playlist.Items.Clear(); _current = -1; UpdateTransport(); };

        _playlist.DoubleClick += async (_, _) => { if (_playlist.SelectedIndex >= 0) await LoadAndPlay(_playlist.SelectedIndex); };

        _btnPlay.Click += async (_, _) => await TogglePlay();
        _btnStop.Click += (_, _) => Stop();
        _btnNext.Click += async (_, _) => await Step(+1, wrap: true);
        _btnPrev.Click += async (_, _) => await Step(-1, wrap: true);

        _volume.Scroll += (_, _) => { _gain = _volume.Value / 100f; _player.SetVolume(_gain); };
        _seek.Scroll += (_, _) =>
        {
            if (_suppressSeek || !_player.IsOpen) return;
            _player.SeekFraction(_seek.Value / 1000.0);
            UpdateTime();
        };

        _timer.Tick += async (_, _) =>
        {
            UpdateProgress();
            if (_player.PollCompleted()) await Step(+1, wrap: false);
        };
        FormClosing += (_, _) => { _timer.Stop(); _player.Dispose(); };
    }

    private void AddFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Codec 2 dosyaları (*.c2)|*.c2|Tüm dosyalar (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            foreach (var f in dlg.FileNames) _playlist.Items.Add(f);
        UpdateTransport();
    }

    private void AddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = ".c2 dosyalarını içeren klasör" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in Directory.EnumerateFiles(dlg.SelectedPath, "*.c2", SearchOption.TopDirectoryOnly))
            _playlist.Items.Add(f);
        UpdateTransport();
    }

    private void RemoveSelected()
    {
        int idx = _playlist.SelectedIndex;
        if (idx < 0) return;
        if (idx == _current) Stop();
        _playlist.Items.RemoveAt(idx);
        if (_current > idx) _current--;
        else if (_current == idx) _current = -1;
        UpdateTransport();
    }

    private async Task TogglePlay()
    {
        if (_player.IsPlaying) { _player.Pause(); _btnPlay.Text = "▶"; return; }
        if (_player.IsPaused) { _player.Play(); _btnPlay.Text = "⏸"; return; }

        int idx = _playlist.SelectedIndex >= 0 ? _playlist.SelectedIndex : (_playlist.Items.Count > 0 ? 0 : -1);
        if (idx >= 0) await LoadAndPlay(idx);
    }

    private async Task LoadAndPlay(int index)
    {
        if (index < 0 || index >= _playlist.Items.Count) return;
        Stop();

        string path = (string)_playlist.Items[index];
        _lblNow.Text = $"Yükleniyor: {Path.GetFileName(path)}";

        DecodedAudio audio;
        try
        {
            audio = await Task.Run(() => C2Decoder.DecodeFile(path));
        }
        catch (Exception ex)
        {
            _lblNow.Text = "—";
            MessageBox.Show(this, ex.Message, "Çözme hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _current = index;
        _playlist.SelectedIndex = index;
        _player.Load(audio.Pcm, audio.SampleRate, _gain);
        _player.Play();

        _seek.Enabled = true;
        _btnPlay.Text = "⏸";
        _lblNow.Text = $"{Path.GetFileName(path)}   [{Codec2Native.ModeName(audio.Mode)}]";
        _timer.Start();
        UpdateProgress();
        UpdateTransport();
    }

    private void Stop()
    {
        _timer.Stop();
        _player.Stop();
        _seek.Value = 0;
        _seek.Enabled = false;
        _btnPlay.Text = "▶";
        _lblTime.Text = "00:00 / 00:00";
    }

    private async Task Step(int delta, bool wrap)
    {
        int count = _playlist.Items.Count;
        if (count == 0) return;
        int idx = _current + delta;
        if (idx < 0) idx = wrap ? count - 1 : -1;
        if (idx >= count) idx = wrap ? 0 : -1;
        if (idx < 0) { Stop(); return; }
        await LoadAndPlay(idx);
    }

    private void UpdateProgress()
    {
        if (!_player.IsOpen) return;
        UpdateTime();
        double total = _player.Total.TotalMilliseconds;
        if (total > 0)
        {
            _suppressSeek = true;
            _seek.Value = (int)Math.Clamp(_player.Current.TotalMilliseconds / total * 1000.0, 0, 1000);
            _suppressSeek = false;
        }
    }

    private void UpdateTime()
    {
        if (!_player.IsOpen) return;
        _lblTime.Text = $"{Fmt(_player.Current)} / {Fmt(_player.Total)}";
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    private void UpdateTransport()
    {
        bool has = _playlist.Items.Count > 0;
        _btnPlay.Enabled = has;
        _btnStop.Enabled = has;
        _btnNext.Enabled = has;
        _btnPrev.Enabled = has;
    }
}
