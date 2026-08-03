namespace HomeKTV.Core.Models;

public enum QueueOrderingMode { FirstComeFirstServed, FairRotation }
public enum QueueItemState { Waiting, Loading, Playing, Paused, Finished, Skipped, Failed, Removed }
public enum AudioMode { Original, Accompaniment, Automatic }
public enum AudioChannelMode { Stereo, Left, Right }
public enum MediaAvailability { Healthy, Missing, Unreadable, NoAudio, NoLyrics, Duplicate }
public enum SongBrowseMode { Popular, RecentImported, RecentPlayed, Favorites }
public enum SongMediaType { Video, Audio, VideoWithExternalAudio, AudioWithSlideshow }
public enum PreferredPlaybackAudio { Original, OriginalVocal, Accompaniment, AiAccompaniment, AiVocals }
public enum AiProcessingStatus { NotRequested, Pending, Processing, AwaitingReview, Completed, Cancelled, Failed, ModelRequired }
public enum AiReviewStatus { NotRequired, Pending, Draft, Approved, Rejected }
public enum AiQualityMode { Fast, Standard, HighQuality }
public enum SlideshowTransition { None, Fade, CrossDissolve }
public enum SlideshowFitMode { Contain, Cover, ContainBlurBackground }
public enum LyricRegionPosition { Top, Center, Bottom }
public enum LyricsDisplayMode { Normal, Karaoke }
public enum LyricsOverlayPosition { Top, Middle, LowerMiddle, Bottom }
