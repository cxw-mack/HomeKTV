namespace HomeKTV.Core.Models;

public enum QueueOrderingMode { FirstComeFirstServed, FairRotation }
public enum QueueItemState { Waiting, Loading, Playing, Paused, Finished, Skipped, Failed, Removed }
public enum AudioMode { Original, Accompaniment, Automatic }
public enum AudioChannelMode { Stereo, Left, Right }
public enum MediaAvailability { Healthy, Missing, Unreadable, NoAudio, NoLyrics, Duplicate }
public enum SongBrowseMode { Popular, RecentImported, RecentPlayed, Favorites }
