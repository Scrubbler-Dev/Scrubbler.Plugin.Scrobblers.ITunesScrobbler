namespace Scrubbler.Plugin.Scrobblers.ITunesScrobbler;

internal sealed record ITunesInfo(int TrackId, int PlayedCount, string SongName,
    string SongArtist, string SongAlbum, string SongAlbumArtist, int SongDuration);

internal sealed record ITunesPlaybackInfo(ITunesInfo? Song, bool IsPlaying);
