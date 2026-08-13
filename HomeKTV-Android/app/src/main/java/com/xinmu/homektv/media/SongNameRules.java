package com.xinmu.homektv.media;

import java.util.Arrays;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;

public final class SongNameRules {
    private static final Set<String> VIDEO_EXTENSIONS = new HashSet<>(Arrays.asList("mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "mpeg", "mpg"));
    private static final Set<String> AUDIO_EXTENSIONS = new HashSet<>(Arrays.asList("mp3", "flac", "wav", "m4a", "aac", "ogg", "opus", "wma"));

    private SongNameRules() { }

    public static String extension(String value) {
        if (value == null) return "";
        int index = value.lastIndexOf('.');
        return index < 0 ? "" : value.substring(index + 1).toLowerCase(Locale.ROOT);
    }

    public static String stem(String value) {
        if (value == null) return "";
        int index = value.lastIndexOf('.');
        return index < 0 ? value : value.substring(0, index);
    }

    public static boolean isMediaExtension(String value) { return VIDEO_EXTENSIONS.contains(value) || AUDIO_EXTENSIONS.contains(value); }
    public static boolean isVideoFile(String value) { return VIDEO_EXTENSIONS.contains(extension(value)); }

    public static boolean isAccompaniment(String value) {
        String lower = value == null ? "" : value.toLowerCase(Locale.ROOT);
        return lower.contains("伴奏") || lower.contains("卡拉ok") || lower.contains("karaoke")
                || lower.contains("instrumental") || lower.matches(".*(?:^|[ _.-])inst(?:[ _.-]|$).*");
    }

    public static ParsedSongName parse(String fileName) {
        String clean = stem(fileName)
                .replaceAll("(?i)(?:\\s*[-_—–－]?\\s*)(伴奏|卡拉ok|karaoke|instrumental|inst|原唱|官方版|mv)(?:\\s*[\\[（(].*?[\\]）)])?\\s*$", "")
                .replaceAll("[\\s_\\-—–－]+$", "")
                .replaceAll("\\s+", " ")
                .trim();
        String[] delimiters = {" - ", " -", "- ", "－", "—", "–"};
        for (String delimiter : delimiters) {
            int index = clean.indexOf(delimiter);
            if (index <= 0 || index >= clean.length() - delimiter.length()) continue;
            String artist = clean.substring(0, index).trim();
            String title = clean.substring(index + delimiter.length()).replaceAll("[\\s_\\-—–－]+$", "").trim();
            if (!artist.isEmpty() && !title.isEmpty()) return new ParsedSongName(artist, title);
        }
        return new ParsedSongName("", clean.isEmpty() ? "未知歌曲" : clean);
    }

    public static String normalizeStem(String value) {
        return stem(value).toLowerCase(Locale.ROOT)
                .replaceAll("(?i)(伴奏|卡拉ok|karaoke|instrumental|inst|mv|原唱)", "")
                .replaceAll("[\\s_\\-—–－()（）\\[\\]]", "");
    }

    public static final class ParsedSongName {
        public final String artist;
        public final String title;
        private ParsedSongName(String artist, String title) { this.artist = artist; this.title = title; }
    }
}
