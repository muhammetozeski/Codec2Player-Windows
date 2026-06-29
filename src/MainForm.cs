using Timer = System.Windows.Forms.Timer;

namespace Codec2Player;

/// <summary>Main window: a .c2 playlist with a transport bar, driving <see cref="WaveOutPlayer"/>.</summary>
sealed class MainForm : Form
{
    const int WindowWidth = 660;
    const int WindowHeight = 480;
    const int MinimumWidth = 520;
    const int MinimumHeight = 380;
    const int ProgressIntervalMs = 200;
    const int SeekSteps = 1000;
    const int DefaultVolumePercent = 90;

    readonly ListBox _playlist = new();
    readonly Button _addFilesButton = new() { Text = "Add Files" };
    readonly Button _addFolderButton = new() { Text = "Add Folder" };
    readonly Button _removeButton = new() { Text = "Remove" };
    readonly Button _clearButton = new() { Text = "Clear" };

    readonly Label _nowPlayingLabel = new() { Text = "—", AutoEllipsis = true };
    readonly TrackBar _seekBar = new() { Minimum = 0, Maximum = SeekSteps, TickStyle = TickStyle.None, Enabled = false };
    readonly Label _timeLabel = new() { Text = "00:00 / 00:00", TextAlign = ContentAlignment.MiddleRight };

    readonly Button _previousButton = new() { Text = "⏮" };
    readonly Button _playPauseButton = new() { Text = "▶" };
    readonly Button _stopButton = new() { Text = "⏹" };
    readonly Button _nextButton = new() { Text = "⏭" };
    readonly Label _volumeIcon = new() { Text = "🔊", TextAlign = ContentAlignment.MiddleCenter };
    readonly TrackBar _volumeBar = new() { Minimum = 0, Maximum = 100, Value = DefaultVolumePercent, TickStyle = TickStyle.None };

    readonly Timer _progressTimer = new() { Interval = ProgressIntervalMs };
    readonly WaveOutPlayer _player = new();

    int _currentIndex = -1;
    bool _suppressSeekEvent;
    float _volume = DefaultVolumePercent / 100f;

    /// <summary>Builds the window, controls and event wiring.</summary>
    public MainForm()
    {
        Text = "Codec2Player";
        Width = WindowWidth;
        Height = WindowHeight;
        MinimumSize = new Size(MinimumWidth, MinimumHeight);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        WireEvents();
        UpdateTransportEnabled();
    }

    /// <summary>Creates and lays out the toolbar, playlist and transport bar.</summary>
    void BuildUi()
    {
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0) };
        foreach (Button button in (Button[])[_addFilesButton, _addFolderButton, _removeButton, _clearButton])
        {
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 6, 0);
            toolbar.Controls.Add(button);
        }

        _playlist.Dock = DockStyle.Fill;
        _playlist.IntegralHeight = false;
        _playlist.Font = new Font(FontFamily.GenericSansSerif, 9.5f);

        TableLayoutPanel bottom = new()
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

        _nowPlayingLabel.Dock = DockStyle.Fill;
        _nowPlayingLabel.TextAlign = ContentAlignment.MiddleLeft;
        _nowPlayingLabel.Font = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold);
        bottom.Controls.Add(_nowPlayingLabel, 0, 0);
        bottom.SetColumnSpan(_nowPlayingLabel, 3);

        _seekBar.Dock = DockStyle.Fill;
        bottom.Controls.Add(_seekBar, 0, 1);
        _timeLabel.Dock = DockStyle.Fill;
        bottom.Controls.Add(_timeLabel, 1, 1);
        bottom.SetColumnSpan(_timeLabel, 2);

        FlowLayoutPanel transport = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (Button button in (Button[])[_previousButton, _playPauseButton, _stopButton, _nextButton])
        {
            button.Width = 48;
            button.Height = 34;
            button.Font = new Font(FontFamily.GenericSansSerif, 11f);
            button.Margin = new Padding(0, 0, 6, 0);
            transport.Controls.Add(button);
        }
        bottom.Controls.Add(transport, 0, 2);

        FlowLayoutPanel volumePanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _volumeIcon.Width = 24;
        _volumeIcon.Height = 34;
        _volumeBar.Width = 200;
        volumePanel.Controls.Add(_volumeIcon);
        volumePanel.Controls.Add(_volumeBar);
        bottom.Controls.Add(volumePanel, 1, 2);
        bottom.SetColumnSpan(volumePanel, 2);

        Controls.Add(_playlist);
        Controls.Add(bottom);
        Controls.Add(toolbar);
    }

    /// <summary>Connects control events to playlist and playback actions.</summary>
    void WireEvents()
    {
        _addFilesButton.Click += (_, _) => AddFiles();
        _addFolderButton.Click += (_, _) => AddFolder();
        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton.Click += (_, _) => { Stop(); _playlist.Items.Clear(); _currentIndex = -1; UpdateTransportEnabled(); };

        _playlist.DoubleClick += async (_, _) => { if (_playlist.SelectedIndex >= 0) await LoadAndPlay(_playlist.SelectedIndex); };

        _playPauseButton.Click += async (_, _) => await TogglePlay();
        _stopButton.Click += (_, _) => Stop();
        _nextButton.Click += async (_, _) => await Step(+1, wrap: true);
        _previousButton.Click += async (_, _) => await Step(-1, wrap: true);

        _volumeBar.Scroll += (_, _) => { _volume = _volumeBar.Value / 100f; _player.SetVolume(_volume); };
        _seekBar.Scroll += (_, _) =>
        {
            if (_suppressSeekEvent || !_player.IsOpen) return;
            _player.SeekFraction((double)_seekBar.Value / SeekSteps);
            UpdateTime();
        };

        _progressTimer.Tick += async (_, _) =>
        {
            UpdateProgress();
            if (_player.PollCompleted()) await Step(+1, wrap: false);
        };
        FormClosing += (_, _) => { _progressTimer.Stop(); _player.Dispose(); };
    }

    /// <summary>Prompts for one or more .c2 files and appends them to the playlist.</summary>
    void AddFiles()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Codec 2 files (*.c2)|*.c2|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            foreach (string file in dialog.FileNames) _playlist.Items.Add(file);
        UpdateTransportEnabled();
    }

    /// <summary>Prompts for a folder and appends every .c2 file in it to the playlist.</summary>
    void AddFolder()
    {
        using FolderBrowserDialog dialog = new() { Description = "Folder containing .c2 files" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (string file in Directory.EnumerateFiles(dialog.SelectedPath, "*.c2", SearchOption.TopDirectoryOnly))
            _playlist.Items.Add(file);
        UpdateTransportEnabled();
    }

    /// <summary>Removes the selected playlist entry, stopping it first if it is playing.</summary>
    void RemoveSelected()
    {
        int index = _playlist.SelectedIndex;
        if (index < 0) return;
        if (index == _currentIndex) Stop();
        _playlist.Items.RemoveAt(index);
        if (_currentIndex > index) _currentIndex--;
        else if (_currentIndex == index) _currentIndex = -1;
        UpdateTransportEnabled();
    }

    /// <summary>Pauses, resumes, or starts playback depending on the current state.</summary>
    async Task TogglePlay()
    {
        if (_player.IsPlaying) { _player.Pause(); _playPauseButton.Text = "▶"; return; }
        if (_player.IsPaused) { _player.Play(); _playPauseButton.Text = "⏸"; return; }

        int index = _playlist.SelectedIndex >= 0 ? _playlist.SelectedIndex : (_playlist.Items.Count > 0 ? 0 : -1);
        if (index >= 0) await LoadAndPlay(index);
    }

    /// <summary>Decodes the playlist entry at <paramref name="index"/> and starts playing it.</summary>
    /// <param name="index">Zero-based playlist index to play.</param>
    async Task LoadAndPlay(int index)
    {
        if (index < 0 || index >= _playlist.Items.Count) return;
        Stop();

        string path = (string)_playlist.Items[index];
        _nowPlayingLabel.Text = $"Loading: {Path.GetFileName(path)}";

        DecodedAudio audio;
        try
        {
            audio = await Task.Run(() => C2Decoder.DecodeFile(path));
        }
        catch (Exception error)
        {
            _nowPlayingLabel.Text = "—";
            MessageBox.Show(this, error.Message, "Decode error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _currentIndex = index;
        _playlist.SelectedIndex = index;
        _player.Load(audio.Pcm, audio.SampleRate, _volume);
        _player.Play();

        _seekBar.Enabled = true;
        _playPauseButton.Text = "⏸";
        _nowPlayingLabel.Text = $"{Path.GetFileName(path)}   [{Codec2Native.ModeName(audio.Mode)}]";
        _progressTimer.Start();
        UpdateProgress();
        UpdateTransportEnabled();
    }

    /// <summary>Stops playback and resets the transport bar.</summary>
    void Stop()
    {
        _progressTimer.Stop();
        _player.Stop();
        _seekBar.Value = 0;
        _seekBar.Enabled = false;
        _playPauseButton.Text = "▶";
        _timeLabel.Text = "00:00 / 00:00";
    }

    /// <summary>Moves to another track relative to the current one.</summary>
    /// <param name="delta">+1 for next, -1 for previous.</param>
    /// <param name="wrap">When true, wraps around the ends; otherwise stops past the last track.</param>
    async Task Step(int delta, bool wrap)
    {
        int count = _playlist.Items.Count;
        if (count == 0) return;
        int index = _currentIndex + delta;
        if (index < 0) index = wrap ? count - 1 : -1;
        else if (index >= count) index = wrap ? 0 : -1;
        if (index < 0) { Stop(); return; }
        await LoadAndPlay(index);
    }

    /// <summary>Updates the time label and seek bar from the player position, suppressing the seek event so the programmatic move is not handled as a user seek.</summary>
    void UpdateProgress()
    {
        if (!_player.IsOpen) return;
        UpdateTime();
        double total = _player.Total.TotalMilliseconds;
        if (total <= 0) return;
        _suppressSeekEvent = true;
        _seekBar.Value = (int)Math.Clamp(_player.Current.TotalMilliseconds / total * SeekSteps, 0, SeekSteps);
        _suppressSeekEvent = false;
    }

    /// <summary>Refreshes the "elapsed / total" time label.</summary>
    void UpdateTime() => _timeLabel.Text = $"{Format(_player.Current)} / {Format(_player.Total)}";

    /// <summary>Formats a duration as h:mm:ss when over an hour, otherwise mm:ss.</summary>
    /// <param name="time">The duration to format.</param>
    /// <returns>The formatted time string.</returns>
    static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");

    /// <summary>Enables or disables the transport buttons based on whether the playlist has items.</summary>
    void UpdateTransportEnabled()
    {
        bool hasItems = _playlist.Items.Count > 0;
        _playPauseButton.Enabled = hasItems;
        _stopButton.Enabled = hasItems;
        _nextButton.Enabled = hasItems;
        _previousButton.Enabled = hasItems;
    }
}
