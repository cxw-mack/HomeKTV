package com.xinmu.homektv.media;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class SongNameRulesTest {
    @Test public void parsesArtistAndTitleAndRemovesAccompanimentSuffix() {
        SongNameRules.ParsedSongName result = SongNameRules.parse("周杰伦 - 稻香 - 伴奏.flac");
        assertEquals("周杰伦", result.artist);
        assertEquals("稻香", result.title);
    }

    @Test public void recognizesChineseAndEnglishAccompanimentNames() {
        assertTrue(SongNameRules.isAccompaniment("F.I.R - 千年之恋 - 伴奏.wav"));
        assertTrue(SongNameRules.isAccompaniment("F.I.R - 千年之恋 instrumental.mp3"));
        assertTrue(SongNameRules.isAccompaniment("F.I.R - 千年之恋_inst.m4a"));
        assertFalse(SongNameRules.isAccompaniment("F.I.R - 千年之恋.mp4"));
    }

    @Test public void normalizesCompanionNamesAndRecognizesVideoExtensions() {
        assertEquals("周杰伦稻香", SongNameRules.normalizeStem("周杰伦 - 稻香 - 伴奏.lrc"));
        assertTrue(SongNameRules.isVideoFile("song.MKV"));
        assertFalse(SongNameRules.isVideoFile("song.flac"));
        assertTrue(SongNameRules.isMediaExtension("opus"));
    }
}
