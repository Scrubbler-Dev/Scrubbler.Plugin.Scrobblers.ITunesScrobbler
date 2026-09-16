using Scrubbler.PluginBase.Settings;

namespace Scrubbler.Plugin.Scrobblers.ITunesScrobbler;

internal class PluginSettings : IPluginSettings
{
  public bool AutoConnect { get; set; } = false;

  public bool EnableDiscordRichPresence { get; set; } = false;
}

