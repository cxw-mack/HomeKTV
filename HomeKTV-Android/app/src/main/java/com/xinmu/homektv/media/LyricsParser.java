package com.xinmu.homektv.media;

import android.content.ContentResolver;
import android.net.Uri;

import com.xinmu.homektv.model.LrcLine;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.Reader;
import java.io.StringReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class LyricsParser {
    private static final Pattern TIME = Pattern.compile("\\[(\\d{1,2}):(\\d{2})(?:[.:](\\d{1,3}))?\\]");

    private LyricsParser() { }

    public static List<LrcLine> parse(ContentResolver resolver, String uriText) {
        if (uriText == null || uriText.isEmpty()) return Collections.emptyList();
        try (InputStream input = resolver.openInputStream(Uri.parse(uriText));
             BufferedReader reader = new BufferedReader(new InputStreamReader(input, StandardCharsets.UTF_8))) {
            return parse(reader);
        } catch (Exception ignored) {
            return Collections.emptyList();
        }
    }

    public static List<LrcLine> parseText(String text) {
        if (text == null || text.isEmpty()) return Collections.emptyList();
        return parse(new StringReader(text));
    }

    private static List<LrcLine> parse(Reader source) {
        List<LrcLine> lines = new ArrayList<>();
        try {
            BufferedReader reader = source instanceof BufferedReader ? (BufferedReader) source : new BufferedReader(source);
            String raw;
            while ((raw = reader.readLine()) != null) {
                Matcher matcher = TIME.matcher(raw);
                int textStart = 0;
                while (matcher.find()) {
                    long minutes = Long.parseLong(matcher.group(1));
                    long seconds = Long.parseLong(matcher.group(2));
                    String fraction = matcher.group(3);
                    long millis = 0;
                    if (fraction != null) {
                        millis = Long.parseLong(fraction);
                        if (fraction.length() == 1) millis *= 100;
                        else if (fraction.length() == 2) millis *= 10;
                    }
                    textStart = matcher.end();
                    lines.add(new LrcLine((minutes * 60 + seconds) * 1000 + millis, ""));
                }
                if (textStart > 0) {
                    String text = raw.substring(textStart).trim();
                    for (int index = lines.size() - 1; index >= 0 && lines.get(index).text.isEmpty(); index--) {
                        LrcLine line = lines.get(index);
                        lines.set(index, new LrcLine(line.timeMs, text));
                    }
                }
            }
        } catch (Exception ignored) { return Collections.emptyList(); }
        List<LrcLine> timedLines = new ArrayList<>();
        for (LrcLine line : lines) if (!line.text.isEmpty()) timedLines.add(line);
        Collections.sort(timedLines, new Comparator<LrcLine>() {
            @Override public int compare(LrcLine left, LrcLine right) { return Long.compare(left.timeMs, right.timeMs); }
        });
        return timedLines;
    }

    public static int currentIndex(List<LrcLine> lines, long positionMs) {
        int low = 0;
        int high = lines.size() - 1;
        int result = -1;
        while (low <= high) {
            int mid = (low + high) >>> 1;
            if (lines.get(mid).timeMs <= positionMs) {
                result = mid;
                low = mid + 1;
            } else high = mid - 1;
        }
        return result;
    }
}
