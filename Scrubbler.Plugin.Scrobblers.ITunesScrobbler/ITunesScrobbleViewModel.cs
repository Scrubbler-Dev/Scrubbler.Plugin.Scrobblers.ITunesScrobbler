using MediaPlayerScrobblerBase;
using Scrubbler.MediaPlayerScrobblerBase;
using Scrubbler.PluginBase;
using Scrubbler.PluginBase.Discord;
using Scrubbler.PluginBase.Services;
using Scrubbler.Plugins.Scrobblers.MediaPlayerScrobbleBase;
using Shoegaze.LastFM;

namespace Scrubbler.Plugin.Scrobblers.ITunesScrobbler;

internal partial class ITunesScrobbleViewModel : MediaPlayerScrobblePluginViewModelBase, IDisposable
{
    public override string CurrentTrackName => _currentSong?.SongName ?? string.Empty;
    public override string CurrentArtistName => _currentSong?.SongArtist ?? string.Empty;
    public override string CurrentAlbumName => _currentSong?.SongAlbum ?? string.Empty;
    public override int CurrentTrackLength => _currentSong?.SongDuration ?? 0;

    private readonly IITunesAutomation _automation;
    private readonly ITickSource _refreshTicks;
    private readonly ITickSource _countTicks;
    private ITunesInfo? _currentSong;
    private bool _isPlaying;
    private bool _disposed;

    public ITunesScrobbleViewModel(ILastfmClient lastfmClient, ILogService logger,
        IITunesAutomation automation, ITickSource refreshTicks, ITickSource countTicks,
        IDiscordRichPresence discordRichPresence)
        : base(lastfmClient, discordRichPresence,
            new DiscordRichPresenceData("itunes", "iTunes", "scrubbler", "Scrubbler"), logger)
    {
        _automation = automation;
        _refreshTicks = refreshTicks;
        _countTicks = countTicks;
        _refreshTicks.Tick += OnRefreshTick;
        _countTicks.Tick += OnCountTick;
    }

    protected override Task Connect()
    {
        if (_disposed || IsConnected)
            return Task.CompletedTask;

        try
        {
            IsBusy = true;
            _automation.Connect();
            IsConnected = true;
            RefreshPlayback();
            _refreshTicks.Start();
            _countTicks.Start();
        }
        catch (Exception ex)
        {
            _logger.Error("Error connecting to iTunes", ex);
            AutoConnect = false;
            _ = Disconnect();
        }
        finally
        {
            IsBusy = false;
        }
        return Task.CompletedTask;
    }

    protected override Task Disconnect()
    {
        _refreshTicks.Stop();
        _countTicks.Stop();
        IsConnected = false;
        try
        {
            _automation.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.Error("Error disconnecting from iTunes", ex);
        }
        _currentSong = null;
        _isPlaying = false;
        ClearState();
        ClearPresence();
        return Task.CompletedTask;
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!IsConnected || _disposed)
            return;
        try
        {
            RefreshPlayback();
        }
        catch (Exception ex)
        {
            _logger.Error("Error while getting iTunes playback info", ex);
            _ = Disconnect();
        }
    }

    private void RefreshPlayback()
    {
        var playback = _automation.GetPlaybackInfo();
        var song = playback.Song;
        var wasPlaying = _isPlaying;
        _isPlaying = playback.IsPlaying && song != null;

        // Play count changes identify repeats even when the track ID stays the same.
        var newPlay = song?.TrackId != _currentSong?.TrackId
            || (song != null && _currentSong != null && song.PlayedCount > _currentSong.PlayedCount);
        var metadataChanged = song != _currentSong;
        _currentSong = song;
        if (newPlay || song == null)
        {
            CountedSeconds = 0;
            CurrentTrackScrobbled = false;
        }
        if (metadataChanged)
            ClearTrackMetadata();
        if (!_isPlaying && (wasPlaying || metadataChanged))
            ClearPresence();
        else if (_isPlaying && !wasPlaying && !metadataChanged)
            _ = UpdateDiscordRichPresence();
    }

    private void ClearTrackMetadata()
    {
        // ClearState also resets the playback counters; retain progress for metadata-only edits.
        var seconds = CountedSeconds;
        var scrobbled = CurrentTrackScrobbled;
        ClearState();
        CountedSeconds = seconds;
        CurrentTrackScrobbled = scrobbled;
    }

    private void OnCountTick(object? sender, EventArgs e)
    {
        if (!IsConnected || _disposed)
            return;
        try
        {
            // Read again so a track change between refresh and count cannot scrobble stale data.
            RefreshPlayback();
            if (!_isPlaying || _currentSong == null)
                return;

            _ = UpdateNowPlaying();
            _ = UpdateDiscordRichPresence();
            CountedSeconds++;
            if (!CurrentTrackScrobbled && CurrentTrackLengthToScrobble > 0
                && CountedSeconds >= CurrentTrackLengthToScrobble
                && !string.IsNullOrWhiteSpace(CurrentTrackName)
                && !string.IsNullOrWhiteSpace(CurrentArtistName))
            {
                CurrentTrackScrobbled = true;
                OnScrobblesDetected([new ScrobbleData(CurrentTrackName, CurrentArtistName, DateTimeOffset.UtcNow)
                {
                    Album = _currentSong.SongAlbum,
                    AlbumArtist = _currentSong.SongAlbumArtist
                }]);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error while counting iTunes playback", ex);
            _ = Disconnect();
        }
    }

    private void ClearPresence()
    {
        if (!EnableDiscordRichPresence)
            return;
        try { _discordRichPresence.Clear(); }
        catch (Exception ex) { _logger.Error("Error clearing iTunes Discord presence", ex); }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refreshTicks.Tick -= OnRefreshTick;
        _countTicks.Tick -= OnCountTick;
        _ = Disconnect();
    }
}
