namespace Scrubbler.Plugin.Scrobblers.ITunesScrobbler;

/// <summary>Access to iTunes, isolated from the view model for testing.</summary>
internal interface IITunesAutomation : IDisposable
{
    void Connect();
    void Disconnect();
    ITunesPlaybackInfo GetPlaybackInfo();
}
