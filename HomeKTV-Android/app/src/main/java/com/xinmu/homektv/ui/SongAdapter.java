package com.xinmu.homektv.ui;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.xinmu.homektv.model.KtvSong;

import java.util.ArrayList;
import java.util.List;

public final class SongAdapter extends BaseAdapter {
    private final Context context;
    private final List<KtvSong> songs = new ArrayList<>();
    private int selected = -1;

    public SongAdapter(Context context) { this.context = context; }
    public void replace(List<KtvSong> values) { songs.clear(); songs.addAll(values); selected = -1; notifyDataSetChanged(); }
    public void setSelected(int position) { selected = position; notifyDataSetChanged(); }
    @Override public int getCount() { return songs.size(); }
    @Override public KtvSong getItem(int position) { return songs.get(position); }
    @Override public long getItemId(int position) { return position; }

    @Override public View getView(int position, View convertView, ViewGroup parent) {
        Holder holder;
        if (convertView == null) {
            LinearLayout layout = new LinearLayout(context);
            layout.setOrientation(LinearLayout.VERTICAL);
            layout.setGravity(Gravity.CENTER_VERTICAL);
            int padding = dp(12);
            layout.setPadding(padding, dp(8), padding, dp(8));
            layout.setMinimumHeight(dp(64));
            TextView title = new TextView(context);
            title.setTextColor(Color.WHITE);
            title.setTextSize(17);
            title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
            title.setSingleLine(true);
            TextView detail = new TextView(context);
            detail.setTextColor(Color.rgb(205, 191, 220));
            detail.setTextSize(13);
            detail.setSingleLine(true);
            layout.addView(title, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            layout.addView(detail, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            holder = new Holder(title, detail);
            layout.setTag(holder);
            convertView = layout;
        } else holder = (Holder) convertView.getTag();
        KtvSong song = getItem(position);
        holder.title.setText((song.favorite ? "★ " : "") + song.title);
        String type = song.video ? "MV" : "音频";
        String extras = (song.hasAccompaniment() ? "  伴奏" : "") + (song.hasLyrics() ? "  歌词" : "");
        holder.detail.setText((song.artist.isEmpty() ? "未知歌手" : song.artist) + "  |  " + type + extras);
        convertView.setBackground(background(position == selected ? Color.rgb(75, 39, 121) : Color.TRANSPARENT));
        return convertView;
    }

    private GradientDrawable background(int color) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(color);
        drawable.setCornerRadius(dp(6));
        return drawable;
    }
    private int dp(int value) { return Math.round(value * context.getResources().getDisplayMetrics().density); }
    private record Holder(TextView title, TextView detail) { }
}
