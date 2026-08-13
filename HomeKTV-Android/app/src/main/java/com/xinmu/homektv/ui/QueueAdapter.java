package com.xinmu.homektv.ui;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.xinmu.homektv.model.KtvSong;

import java.util.ArrayList;
import java.util.List;

public final class QueueAdapter extends BaseAdapter {
    private final Context context;
    private final List<KtvSong> songs = new ArrayList<>();
    public QueueAdapter(Context context) { this.context = context; }
    public void replace(List<KtvSong> values) { songs.clear(); songs.addAll(values); notifyDataSetChanged(); }
    @Override public int getCount() { return songs.size(); }
    @Override public KtvSong getItem(int position) { return songs.get(position); }
    @Override public long getItemId(int position) { return position; }

    @Override public View getView(int position, View convertView, ViewGroup parent) {
        TextView text = convertView instanceof TextView ? (TextView) convertView : new TextView(context);
        text.setPadding(dp(12), dp(11), dp(12), dp(11));
        text.setGravity(Gravity.CENTER_VERTICAL);
        text.setTextSize(15);
        text.setTextColor(Color.WHITE);
        text.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        KtvSong song = getItem(position);
        text.setText(context.getString(com.xinmu.homektv.R.string.queue_entry, position + 1, song.displayName()));
        return text;
    }
    private int dp(int value) { return Math.round(value * context.getResources().getDisplayMetrics().density); }
}
