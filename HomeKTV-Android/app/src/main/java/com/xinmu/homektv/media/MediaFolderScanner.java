package com.xinmu.homektv.media;

import android.content.Context;
import android.net.Uri;

import androidx.documentfile.provider.DocumentFile;

import com.xinmu.homektv.model.KtvSong;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

public final class MediaFolderScanner {
    private static final java.util.Set<String> IMAGE_EXTENSIONS = new java.util.HashSet<>(java.util.Arrays.asList("jpg", "jpeg", "png", "webp", "bmp"));
    private static final java.util.Set<String> LYRIC_EXTENSIONS = new java.util.HashSet<>(java.util.Arrays.asList("lrc", "txt"));
    private static final String LIBRARY_FILE = "homektv-library.json";

    private MediaFolderScanner() { }

    public static List<KtvSong> scan(Context context, Uri treeUri) {
        DocumentFile root = DocumentFile.fromTreeUri(context, treeUri);
        if (root == null || !root.canRead()) return Collections.emptyList();
        List<KtvSong> songs = new ArrayList<>();
        scanFolder(root, songs);
        Collections.sort(songs, new Comparator<KtvSong>() {
            @Override public int compare(KtvSong left, KtvSong right) {
                return String.CASE_INSENSITIVE_ORDER.compare(left.displayName(), right.displayName());
            }
        });
        return songs;
    }

    private static void scanFolder(DocumentFile folder, List<KtvSong> songs) {
        DocumentFile[] children;
        try { children = folder.listFiles(); } catch (SecurityException error) { return; }
        List<DocumentFile> media = new ArrayList<>();
        List<DocumentFile> lyrics = new ArrayList<>();
        List<DocumentFile> covers = new ArrayList<>();
        List<DocumentFile> directories = new ArrayList<>();
        for (DocumentFile child : children) {
            if (child.isDirectory()) directories.add(child);
            else if (child.isFile()) {
                String extension = SongNameRules.extension(child.getName());
                if (SongNameRules.isMediaExtension(extension)) media.add(child);
                else if (LYRIC_EXTENSIONS.contains(extension)) lyrics.add(child);
                else if (IMAGE_EXTENSIONS.contains(extension)) covers.add(child);
            }
        }
        KtvSong song = buildSong(media, lyrics, covers);
        if (song != null) songs.add(song);
        for (DocumentFile directory : directories) scanFolder(directory, songs);
    }

    private static KtvSong buildSong(List<DocumentFile> media, List<DocumentFile> lyrics, List<DocumentFile> covers) {
        if (media.isEmpty()) return null;
        DocumentFile primary = null;
        DocumentFile accompaniment = null;
        for (DocumentFile file : media) {
            if (SongNameRules.isAccompaniment(file.getName())) {
                if (accompaniment == null) accompaniment = file;
                continue;
            }
            if (primary == null || (SongNameRules.isVideoFile(file.getName()) && !SongNameRules.isVideoFile(primary.getName()))) primary = file;
        }
        if (primary == null) primary = accompaniment;
        if (primary == null) return null;
        if (primary == accompaniment) accompaniment = null;

        String stem = SongNameRules.stem(primary.getName());
        SongNameRules.ParsedSongName metadata = SongNameRules.parse(stem);
        DocumentFile lyric = bestCompanion(lyrics, stem);
        DocumentFile cover = bestCompanion(covers, stem);
        return new KtvSong(
                primary.getUri().toString(), metadata.title, metadata.artist, primary.getUri().toString(),
                accompaniment == null ? "" : accompaniment.getUri().toString(),
                lyric == null ? "" : lyric.getUri().toString(), cover == null ? "" : cover.getUri().toString(),
                SongNameRules.isVideoFile(primary.getName()), false);
    }

    private static DocumentFile bestCompanion(List<DocumentFile> files, String primaryStem) {
        if (files.isEmpty()) return null;
        String normalized = SongNameRules.normalizeStem(primaryStem);
        for (DocumentFile file : files) {
            if (SongNameRules.normalizeStem(file.getName()).equals(normalized)) return file;
        }
        return files.get(0);
    }

    public static void save(Context context, List<KtvSong> songs) {
        try {
            JSONArray array = new JSONArray();
            for (KtvSong song : songs) array.put(song.toJson());
            try (FileOutputStream output = context.openFileOutput(LIBRARY_FILE, Context.MODE_PRIVATE)) {
                output.write(array.toString().getBytes(StandardCharsets.UTF_8));
            }
        } catch (Exception ignored) { }
    }

    public static List<KtvSong> load(Context context) {
        File file = new File(context.getFilesDir(), LIBRARY_FILE);
        if (!file.exists()) return new ArrayList<>();
        try (FileInputStream input = context.openFileInput(LIBRARY_FILE)) {
            byte[] data = new byte[(int) input.getChannel().size()];
            int read = input.read(data);
            if (read <= 0) return new ArrayList<>();
            JSONArray array = new JSONArray(new String(data, 0, read, StandardCharsets.UTF_8));
            List<KtvSong> songs = new ArrayList<>();
            for (int index = 0; index < array.length(); index++) songs.add(KtvSong.fromJson(array.getJSONObject(index)));
            return songs;
        } catch (Exception ignored) {
            return new ArrayList<>();
        }
    }
}
