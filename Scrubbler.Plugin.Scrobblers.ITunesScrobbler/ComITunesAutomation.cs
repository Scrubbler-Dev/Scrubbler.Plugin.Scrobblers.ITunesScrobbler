using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using iTunesLib;

namespace Scrubbler.Plugin.Scrobblers.ITunesScrobbler;

/// <summary>Uses the same iTunes COM API as the original WPF scrobbler.</summary>
internal sealed class ComITunesAutomation : IITunesAutomation
{
    private iTunesApp? _application;

    public void Connect()
    {
        Disconnect();
        _application = new iTunesApp();
    }

    public ITunesPlaybackInfo GetPlaybackInfo()
    {
        var application = _application ?? throw new InvalidOperationException("Not connected to iTunes.");
        IITTrack? track = null;
        try
        {
            var isPlaying = application.PlayerState == ITPlayerState.ITPlayerStatePlaying;
            track = application.CurrentTrack;
            if (track == null)
                return new(null, false);

            // AlbumArtist is not exposed by every iTunes track implementation.
            string albumArtist = string.Empty;
            try
            {
                albumArtist = ((dynamic)track).AlbumArtist ?? string.Empty;
            }
            catch (RuntimeBinderException) { }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80020003)
                                         || ex.HResult == unchecked((int)0x80020006)) { }

            return new(new ITunesInfo(track.trackID, track.PlayedCount, track.Name ?? string.Empty,
                track.Artist ?? string.Empty, track.Album ?? string.Empty, albumArtist, track.Duration), isPlaying);
        }
        finally
        {
            if (track != null && Marshal.IsComObject(track))
                Marshal.ReleaseComObject(track);
        }
    }

    public void Disconnect()
    {
        var application = _application;
        _application = null;
        if (application != null && Marshal.IsComObject(application))
            Marshal.ReleaseComObject(application);
    }

    public void Dispose() => Disconnect();
}
