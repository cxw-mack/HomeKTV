package com.xinmu.homektv.media;

import static org.junit.Assert.assertEquals;

import com.xinmu.homektv.model.LrcLine;

import org.junit.Test;

import java.util.List;

public final class LyricsParserTest {
    @Test public void parsesMultipleTimestampsAndNormalizesFractions() {
        List<LrcLine> lines = LyricsParser.parseText("[00:01.2][00:02.34]合唱\n[01:03.456]下一句");
        assertEquals(3, lines.size());
        assertEquals(1200, lines.get(0).timeMs);
        assertEquals(2340, lines.get(1).timeMs);
        assertEquals("合唱", lines.get(1).text);
        assertEquals(63456, lines.get(2).timeMs);
    }

    @Test public void findsCurrentLineAtTimelineBoundaries() {
        List<LrcLine> lines = LyricsParser.parseText("[00:01.00]第一句\n[00:05.00]第二句");
        assertEquals(-1, LyricsParser.currentIndex(lines, 999));
        assertEquals(0, LyricsParser.currentIndex(lines, 1000));
        assertEquals(0, LyricsParser.currentIndex(lines, 4999));
        assertEquals(1, LyricsParser.currentIndex(lines, 5000));
    }
}
