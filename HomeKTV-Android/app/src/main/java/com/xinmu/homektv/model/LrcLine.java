package com.xinmu.homektv.model;

public final class LrcLine {
    public final long timeMs;
    public final String text;

    public LrcLine(long timeMs, String text) {
        this.timeMs = timeMs;
        this.text = text;
    }
}
