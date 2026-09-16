using MediaPlayerScrobblerBase;
using Moq;
using Scrubbler.Plugin.Scrobblers.ITunesScrobbler;
using Scrubbler.PluginBase;
using Scrubbler.PluginBase.Discord;
using Scrubbler.PluginBase.Services;
using Shoegaze.LastFM;

namespace Scrubbler.Test.ITunesScrobblerTest;

[TestFixture]
public class ITunesScrobblerTest
{
    private Mock<IITunesAutomation> _automation = null!;
    private Mock<IDiscordRichPresence> _presence = null!;
    private ManualTickSource _refresh = null!;
    private ManualTickSource _count = null!;
    private ITunesScrobbleViewModel _vm = null!;
    private ITunesPlaybackInfo _playback = null!;
    private List<ScrobbleData> _scrobbles = null!;

    [SetUp]
    public void Setup()
    {
        _automation = new Mock<IITunesAutomation>(MockBehavior.Strict);
        _automation.Setup(a => a.Connect());
        _automation.Setup(a => a.Disconnect());
        _automation.Setup(a => a.GetPlaybackInfo()).Returns(() => _playback);
        _presence = new Mock<IDiscordRichPresence>();
        _refresh = new ManualTickSource();
        _count = new ManualTickSource();
        _playback = new(new ITunesInfo(1, 5, "Track", "Artist", "Album", "Album Artist", 100), true);
        _vm = new(new Mock<ILastfmClient>().Object, new Mock<ILogService>().Object,
            _automation.Object, _refresh, _count, _presence.Object);
        _scrobbles = [];
        _vm.ScrobblesDetected += (_, data) => _scrobbles.AddRange(data);
    }

    [TearDown]
    public void TearDown()
    {
        _vm.Dispose();
        _refresh.Dispose();
        _count.Dispose();
    }

    [Test]
    public async Task Connect_loads_track_and_starts_timers()
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_vm.IsConnected, Is.True);
            Assert.That(_vm.CurrentTrackName, Is.EqualTo("Track"));
            Assert.That(_vm.CurrentArtistName, Is.EqualTo("Artist"));
            Assert.That(_vm.CurrentAlbumName, Is.EqualTo("Album"));
            Assert.That(_vm.CurrentTrackLength, Is.EqualTo(100));
            Assert.That(_refresh.IsRunning && _count.IsRunning, Is.True);
        }
    }

    [Test]
    public async Task Scrobbles_once_at_half_duration_with_album_artist()
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(49);
        Assert.That(_scrobbles, Is.Empty);
        Tick(1);
        Assert.That(_scrobbles, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scrobbles[0].Track, Is.EqualTo("Track"));
            Assert.That(_scrobbles[0].Artist, Is.EqualTo("Artist"));
            Assert.That(_scrobbles[0].Album, Is.EqualTo("Album"));
            Assert.That(_scrobbles[0].AlbumArtist, Is.EqualTo("Album Artist"));
        }
        Tick(100);
        Assert.That(_scrobbles, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Pausing_preserves_progress_and_clears_presence_then_resumes()
    {
        _vm.EnableDiscordRichPresence = true;
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(20);
        _playback = _playback with { IsPlaying = false };
        _refresh.Fire();
        Tick(50);
        Assert.That(_vm.CountedSeconds, Is.EqualTo(20));
        Assert.That(_scrobbles, Is.Empty);
        _presence.Verify(p => p.Clear(), Times.AtLeastOnce);
        _playback = _playback with { IsPlaying = true };
        Tick(30);
        Assert.That(_scrobbles, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Increased_play_count_allows_same_track_to_scrobble_again()
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(50);
        _playback = _playback with { Song = _playback.Song! with { PlayedCount = 6 } };
        _refresh.Fire();
        Assert.That(_vm.CountedSeconds, Is.Zero);
        Assert.That(_vm.CurrentTrackScrobbled, Is.False);
        Tick(50);
        Assert.That(_scrobbles, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Track_change_before_count_tick_cannot_scrobble_previous_track()
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(49);
        _playback = _playback with { Song = _playback.Song! with { TrackId = 2, PlayedCount = 0, SongName = "Other" } };
        Tick(1);
        Assert.That(_scrobbles, Is.Empty);
        Assert.That(_vm.CountedSeconds, Is.EqualTo(1));
        Tick(49);
        Assert.That(_scrobbles.Single().Track, Is.EqualTo("Other"));
    }

    [Test]
    public async Task Missing_track_clears_display_and_progress()
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(10);
        _playback = new(null, false);
        _refresh.Fire();
        Tick(100);
        Assert.That(_vm.CurrentTrackName, Is.Empty);
        Assert.That(_vm.CountedSeconds, Is.Zero);
        Assert.That(_scrobbles, Is.Empty);
    }

    [TestCase(0, "Track", "Artist")]
    [TestCase(100, "", "Artist")]
    [TestCase(100, "Track", "")]
    public async Task Invalid_metadata_does_not_emit_scrobbles(int duration, string track, string artist)
    {
        _playback = _playback with { Song = _playback.Song! with { SongDuration = duration, SongName = track, SongArtist = artist } };
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(300);
        Assert.That(_scrobbles, Is.Empty);
        Assert.That(_vm.IsConnected, Is.True);
    }

    [Test]
    public async Task Long_track_scrobbles_at_four_minutes()
    {
        _playback = _playback with { Song = _playback.Song! with { SongDuration = 1000 } };
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(239);
        Assert.That(_scrobbles, Is.Empty);
        Tick(1);
        Assert.That(_scrobbles, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Connection_failure_releases_automation_and_disables_auto_connect()
    {
        _vm.AutoConnect = true;
        _automation.Setup(a => a.Connect()).Throws(new InvalidOperationException("iTunes unavailable"));
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Assert.That(_vm.IsConnected, Is.False);
        Assert.That(_vm.AutoConnect, Is.False);
        Assert.That(_refresh.IsRunning || _count.IsRunning, Is.False);
        _automation.Verify(a => a.Disconnect(), Times.Once);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Read_failure_disconnects_and_allows_reconnection(bool refresh)
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        _automation.Setup(a => a.GetPlaybackInfo()).Throws(new System.Runtime.InteropServices.COMException());
        if (refresh) _refresh.Fire(); else _count.Fire();
        Assert.That(_vm.IsConnected, Is.False);
        Assert.That(_vm.CurrentTrackName, Is.Empty);
        Assert.That(_refresh.IsRunning || _count.IsRunning, Is.False);
        _automation.Setup(a => a.GetPlaybackInfo()).Returns(() => _playback);
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Tick(50);
        Assert.That(_scrobbles, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Dispose_stops_timers_and_ignores_queued_ticks()
    {
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        _vm.Dispose();
        _vm.Dispose();
        _automation.Invocations.Clear();
        _refresh.FireQueued();
        _count.FireQueued();
        await _vm.ToggleConnectionCommand.ExecuteAsync(null);
        Assert.That(_automation.Invocations, Is.Empty);
        Assert.That(_vm.IsConnected, Is.False);
        Assert.That(_refresh.IsRunning || _count.IsRunning, Is.False);
    }

    private void Tick(int seconds)
    {
        for (var i = 0; i < seconds; i++) _count.Fire();
    }

    private sealed class ManualTickSource : ITickSource
    {
        public event EventHandler? Tick;
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() => Stop();
        public void Fire() { if (IsRunning) FireQueued(); }
        public void FireQueued() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
