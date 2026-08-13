package com.xinmu.homektv.model;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Locale;

public final class KtvSong {
    public final String id;
    public final String title;
    public final String artist;
    public final String primaryUri;
    public final String accompanimentUri;
    public final String lyricUri;
    public final String coverUri;
    public final boolean video;
    public boolean favorite;

    public KtvSong(String id, String title, String artist, String primaryUri, String accompanimentUri,
                   String lyricUri, String coverUri, boolean video, boolean favorite) {
        this.id = id;
        this.title = title;
        this.artist = artist;
        this.primaryUri = primaryUri;
        this.accompanimentUri = accompanimentUri;
        this.lyricUri = lyricUri;
        this.coverUri = coverUri;
        this.video = video;
        this.favorite = favorite;
    }

    public boolean hasAccompaniment() { return accompanimentUri != null && !accompanimentUri.isEmpty(); }
    public boolean hasLyrics() { return lyricUri != null && !lyricUri.isEmpty(); }
    public String displayName() { return artist.isEmpty() ? title : artist + " - " + title; }

    public boolean matches(String query) {
        if (query == null || query.trim().isEmpty()) return true;
        String target = (title + " " + artist).toLowerCase(Locale.ROOT);
        return target.contains(query.trim().toLowerCase(Locale.ROOT));
    }

    public JSONObject toJson() throws JSONException {
        JSONObject object = new JSONObject();
        object.put("id", id);
        object.put("title", title);
        object.put("artist", artist);
        object.put("primaryUri", primaryUri);
        object.put("accompanimentUri", accompanimentUri);
        object.put("lyricUri", lyricUri);
        object.put("coverUri", coverUri);
        object.put("video", video);
        object.put("favorite", favorite);
        return object;
    }

    public static KtvSong fromJson(JSONObject object) throws JSONException {
        return new KtvSong(
                object.getString("id"), object.getString("title"), object.optString("artist"),
                object.getString("primaryUri"), object.optString("accompanimentUri"),
                object.optString("lyricUri"), object.optString("coverUri"), object.optBoolean("video"),
                object.optBoolean("favorite"));
    }
}
