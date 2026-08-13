package com.xinmu.homektv;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.SeekBar;
import android.widget.Space;
import android.widget.TextView;
import android.widget.Toast;

import androidx.media3.common.MediaItem;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.Player;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.ui.PlayerView;

import com.xinmu.homektv.media.LyricsParser;
import com.xinmu.homektv.media.MediaFolderScanner;
import com.xinmu.homektv.model.KtvSong;
import com.xinmu.homektv.model.LrcLine;
import com.xinmu.homektv.ui.QueueAdapter;
import com.xinmu.homektv.ui.SongAdapter;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class MainActivity extends Activity implements Player.Listener {
    private static final int PICK_FOLDER = 2001;
    private static final String PREFS = "homektv";
    private static final String FOLDER_URI = "folderUri";
    private static final int PURPLE = Color.rgb(143, 75, 255);
    private static final int SURFACE = Color.rgb(33, 18, 54);
    private static final int INK = Color.rgb(18, 10, 34);

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final List<KtvSong> allSongs = new ArrayList<>();
    private final List<KtvSong> visibleSongs = new ArrayList<>();
    private final List<KtvSong> queue = new ArrayList<>();
    private final List<LrcLine> lyrics = new ArrayList<>();

    private ExoPlayer player;
    private SongAdapter songAdapter;
    private QueueAdapter queueAdapter;
    private ListView songList;
    private ListView queueList;
    private EditText search;
    private TextView status;
    private TextView currentTitle;
    private TextView currentArtist;
    private TextView lyricCurrent;
    private TextView lyricNext;
    private TextView libraryCount;
    private TextView emptyState;
    private PlayerView playerView;
    private Button originalButton;
    private Button accompanimentButton;
    private Button pauseButton;
    private SeekBar volumeBar;
    private KtvSong currentSong;
    private boolean accompanimentMode;
    private int masterVolume = 80;
    private int category = 0;
    private boolean wideLayout;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        player = new ExoPlayer.Builder(this).build();
        player.addListener(this);
        buildUi();
        loadLibrary();
        scheduleProgress();
    }

    private void buildUi() {
        wideLayout = getResources().getConfiguration().screenWidthDp >= 700
                || getResources().getConfiguration().orientation == Configuration.ORIENTATION_LANDSCAPE;
        LinearLayout root = column(INK);
        root.setPadding(dp(12), dp(8), dp(12), dp(8));
        root.addView(buildHeader(), new LinearLayout.LayoutParams(-1, dp(54)));
        if (wideLayout) {
            LinearLayout body = row(Color.TRANSPARENT);
            body.addView(buildNavigation(), new LinearLayout.LayoutParams(dp(164), -1));
            body.addView(buildMainPanel(), weight(1));
            body.addView(buildQueuePanel(), new LinearLayout.LayoutParams(dp(245), -1));
            root.addView(body, weight(1));
        } else {
            root.addView(buildCategoryStrip(), new LinearLayout.LayoutParams(-1, dp(52)));
            root.addView(buildMainPanel(), weight(1));
        }
        setContentView(root);
        updateFilter();
    }

    private View buildHeader() {
        LinearLayout header = row(Color.TRANSPARENT);
        TextView brand = text("HomeKTV", 25, Color.WHITE, true);
        header.addView(brand, new LinearLayout.LayoutParams(dp(150), -1));
        TextView tag = text("本地歌库 · TV / 手机 / 平板", 13, Color.rgb(205, 191, 220), false);
        tag.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(tag, weight(1));
        Button importButton = button("导入歌库");
        importButton.setOnClickListener(view -> pickFolder());
        header.addView(importButton, new LinearLayout.LayoutParams(dp(100), dp(44)));
        Button queueButton = button("队列");
        queueButton.setOnClickListener(view -> showQueueDialog());
        header.addView(queueButton, new LinearLayout.LayoutParams(dp(72), dp(44)));
        return header;
    }

    private View buildNavigation() {
        LinearLayout navigation = column(Color.TRANSPARENT);
        navigation.setPadding(0, dp(8), dp(10), 0);
        TextView title = text("歌库", 19, Color.WHITE, true);
        title.setPadding(dp(8), dp(4), 0, dp(8));
        navigation.addView(title, new LinearLayout.LayoutParams(-1, dp(40)));
        navigation.addView(buildCategoryStrip(), heightWeight(1));
        return navigation;
    }

    private View buildCategoryStrip() {
        HorizontalScrollView scroll = new HorizontalScrollView(this);
        scroll.setHorizontalScrollBarEnabled(false);
        LinearLayout categories = row(Color.TRANSPARENT);
        String[] names = {"全部歌曲", "收藏", "有歌词", "有伴奏", "MV", "音频", "歌手"};
        for (int index = 0; index < names.length; index++) {
            final int selected = index;
            Button item = new Button(this);
            item.setText(names[index]);
            item.setTextColor(Color.WHITE);
            item.setTextSize(14);
            item.setAllCaps(false);
            item.setGravity(Gravity.CENTER);
            item.setPadding(dp(8), 0, dp(8), 0);
            item.setBackgroundResource(com.xinmu.homektv.R.drawable.category_background);
            item.setFocusable(true);
            item.setSelected(index == category);
            item.setOnClickListener(view -> { category = selected; updateFilter(); });
            categories.addView(item, new LinearLayout.LayoutParams(wideLayout ? dp(154) : dp(96), dp(44)));
        }
        scroll.addView(categories, new ViewGroup.LayoutParams(wideLayout ? -1 : -2, -1));
        return scroll;
    }

    private View buildMainPanel() {
        LinearLayout panel = column(Color.TRANSPARENT);
        panel.setPadding(dp(4), dp(8), dp(4), 0);
        FrameLayout videoFrame = new FrameLayout(this);
        videoFrame.setBackgroundColor(Color.BLACK);
        playerView = new PlayerView(this);
        playerView.setPlayer(player);
        playerView.setUseController(false);
        videoFrame.addView(playerView, new FrameLayout.LayoutParams(-1, -1));
        emptyState = text("选择一首歌开始演唱", 22, Color.rgb(205, 191, 220), true);
        emptyState.setGravity(Gravity.CENTER);
        videoFrame.addView(emptyState, new FrameLayout.LayoutParams(-1, -1));
        panel.addView(videoFrame, new LinearLayout.LayoutParams(-1, wideLayout ? dp(275) : dp(185)));

        currentTitle = text("等待点歌", wideLayout ? 24 : 20, Color.WHITE, true);
        currentTitle.setPadding(dp(4), dp(7), 0, 0);
        panel.addView(currentTitle, new LinearLayout.LayoutParams(-1, dp(38)));
        currentArtist = text("导入歌曲文件夹后开始使用", 13, Color.rgb(205, 191, 220), false);
        currentArtist.setPadding(dp(4), 0, 0, 0);
        panel.addView(currentArtist, new LinearLayout.LayoutParams(-1, dp(26)));

        LinearLayout lyricBox = column(Color.TRANSPARENT);
        lyricBox.setGravity(Gravity.CENTER);
        lyricCurrent = lyricText("等待歌词", wideLayout ? 28 : 22, Color.rgb(50, 140, 255));
        lyricNext = lyricText("", wideLayout ? 19 : 16, Color.WHITE);
        lyricBox.addView(lyricCurrent, new LinearLayout.LayoutParams(-1, dp(43)));
        lyricBox.addView(lyricNext, new LinearLayout.LayoutParams(-1, dp(32)));
        panel.addView(lyricBox, new LinearLayout.LayoutParams(-1, wideLayout ? dp(82) : dp(68)));

        LinearLayout controls = row(Color.TRANSPARENT);
        originalButton = button("原唱");
        accompanimentButton = button("伴奏");
        pauseButton = button("播放");
        Button favorite = button("收藏");
        Button skip = button("下一首");
        originalButton.setOnClickListener(view -> switchAudio(false));
        accompanimentButton.setOnClickListener(view -> switchAudio(true));
        pauseButton.setOnClickListener(view -> togglePause());
        favorite.setOnClickListener(view -> toggleFavorite());
        skip.setOnClickListener(view -> playNext());
        controls.addView(originalButton, new LinearLayout.LayoutParams(dp(70), dp(42)));
        controls.addView(accompanimentButton, new LinearLayout.LayoutParams(dp(70), dp(42)));
        controls.addView(pauseButton, new LinearLayout.LayoutParams(dp(76), dp(42)));
        controls.addView(favorite, new LinearLayout.LayoutParams(dp(76), dp(42)));
        controls.addView(skip, new LinearLayout.LayoutParams(dp(82), dp(42)));
        TextView volumeLabel = text("音量", 13, Color.rgb(205, 191, 220), false);
        volumeLabel.setGravity(Gravity.CENTER_VERTICAL);
        controls.addView(volumeLabel, new LinearLayout.LayoutParams(dp(42), dp(42)));
        volumeBar = new SeekBar(this);
        volumeBar.setMax(100);
        volumeBar.setProgress(masterVolume);
        volumeBar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar bar, int progress, boolean fromUser) { masterVolume = Math.max(0, progress); applyVolume(); }
            @Override public void onStartTrackingTouch(SeekBar bar) { }
            @Override public void onStopTrackingTouch(SeekBar bar) { }
        });
        controls.addView(volumeBar, weight(1));
        panel.addView(controls, new LinearLayout.LayoutParams(-1, dp(48)));

        search = new EditText(this);
        search.setHint("搜索歌名或歌手");
        search.setHintTextColor(Color.rgb(205, 191, 220));
        search.setTextColor(Color.WHITE);
        search.setSingleLine(true);
        search.setTextSize(16);
        search.setPadding(dp(14), 0, dp(14), 0);
        search.setBackgroundResource(com.xinmu.homektv.R.drawable.panel_background);
        search.addTextChangedListener(new android.text.TextWatcher() {
            @Override public void beforeTextChanged(CharSequence value, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence value, int start, int before, int count) { updateFilter(); }
            @Override public void afterTextChanged(android.text.Editable value) { }
        });
        panel.addView(search, new LinearLayout.LayoutParams(-1, dp(46)));
        libraryCount = text("歌库为空", 13, Color.rgb(205, 191, 220), false);
        libraryCount.setPadding(dp(4), dp(5), 0, 0);
        panel.addView(libraryCount, new LinearLayout.LayoutParams(-1, dp(29)));
        songList = new ListView(this);
        songList.setDivider(null);
        songList.setItemsCanFocus(true);
        songAdapter = new SongAdapter(this);
        songList.setAdapter(songAdapter);
        songList.setOnItemClickListener((parent, view, position, id) -> enqueue(visibleSongs.get(position)));
        songList.setOnItemLongClickListener((parent, view, position, id) -> { playNow(visibleSongs.get(position)); return true; });
        panel.addView(songList, heightWeight(1));
        return panel;
    }

    private View buildQueuePanel() {
        LinearLayout panel = column(Color.TRANSPARENT);
        panel.setPadding(dp(10), dp(8), 0, 0);
        TextView heading = text("播放队列", 19, Color.WHITE, true);
        heading.setPadding(dp(4), dp(4), 0, dp(8));
        panel.addView(heading, new LinearLayout.LayoutParams(-1, dp(40)));
        queueList = new ListView(this);
        queueList.setDivider(null);
        queueAdapter = new QueueAdapter(this);
        queueList.setAdapter(queueAdapter);
        queueList.setOnItemClickListener((parent, view, position, id) -> { KtvSong song = queue.get(position); queue.remove(position); queueAdapter.replace(queue); playNow(song); });
        panel.addView(queueList, heightWeight(1));
        Button clear = button("清空队列");
        clear.setOnClickListener(view -> { queue.clear(); queueAdapter.replace(queue); toast("队列已清空"); });
        panel.addView(clear, new LinearLayout.LayoutParams(-1, dp(44)));
        return panel;
    }

    private void pickFolder() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        startActivityForResult(intent, PICK_FOLDER);
    }

    @SuppressLint("WrongConstant")
    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != PICK_FOLDER || resultCode != RESULT_OK || data == null || data.getData() == null) return;
        Uri uri = data.getData();
        int grantedFlags = data.getFlags() & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        try { if ((grantedFlags & Intent.FLAG_GRANT_READ_URI_PERMISSION) != 0) getContentResolver().takePersistableUriPermission(uri, grantedFlags); } catch (Exception ignored) { }
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString(FOLDER_URI, uri.toString()).apply();
        statusMessage("正在扫描歌曲文件夹...");
        worker.execute(() -> {
            List<KtvSong> found = MediaFolderScanner.scan(this, uri);
            runOnUiThread(() -> { allSongs.clear(); allSongs.addAll(found); MediaFolderScanner.save(this, allSongs); updateFilter(); statusMessage("已导入 " + found.size() + " 首歌曲"); });
        });
    }

    private void loadLibrary() {
        List<KtvSong> cached = MediaFolderScanner.load(this);
        allSongs.clear();
        allSongs.addAll(cached);
        String folder = getSharedPreferences(PREFS, MODE_PRIVATE).getString(FOLDER_URI, "");
        if (allSongs.isEmpty() && !folder.isEmpty()) {
            statusMessage("正在读取歌库...");
            worker.execute(() -> {
                List<KtvSong> found = MediaFolderScanner.scan(this, Uri.parse(folder));
                runOnUiThread(() -> { allSongs.addAll(found); MediaFolderScanner.save(this, allSongs); updateFilter(); statusMessage("已载入 " + found.size() + " 首歌曲"); });
            });
        }
        updateFilter();
    }

    private void updateFilter() {
        visibleSongs.clear();
        String query = search == null ? "" : search.getText().toString();
        for (KtvSong song : allSongs) {
            boolean accepted = switch (category) {
                case 1 -> song.favorite;
                case 2 -> song.hasLyrics();
                case 3 -> song.hasAccompaniment();
                case 4 -> song.video;
                case 5 -> !song.video;
                default -> true;
            };
            if (accepted && song.matches(query)) visibleSongs.add(song);
        }
        if (category == 6) Collections.sort(visibleSongs, (left, right) -> (left.artist + left.title).compareToIgnoreCase(right.artist + right.title));
        if (songAdapter != null) songAdapter.replace(visibleSongs);
        if (libraryCount != null) libraryCount.setText(getString(com.xinmu.homektv.R.string.library_count, visibleSongs.size()));
    }

    private void enqueue(KtvSong song) {
        if (currentSong == null || !player.isPlaying()) { playNow(song); return; }
        if (!queueContains(song.id) && (currentSong == null || !currentSong.id.equals(song.id))) queue.add(song);
        if (queueAdapter != null) queueAdapter.replace(queue);
        toast("已加入队列：" + song.title);
    }

    private void playNow(KtvSong song) {
        currentSong = song;
        removeQueuedSong(song.id);
        if (queueAdapter != null) queueAdapter.replace(queue);
        accompanimentMode = false;
        lyrics.clear();
        lyricCurrent.setText(song.hasLyrics() ? "加载歌词..." : "暂无歌词");
        lyricNext.setText("");
        currentTitle.setText(song.title);
        currentArtist.setText(song.artist.isEmpty() ? "未知歌手" : song.artist);
        if (emptyState != null) emptyState.setVisibility(View.GONE);
        originalButton.setEnabled(true);
        accompanimentButton.setEnabled(song.hasAccompaniment());
        MediaItem item = MediaItem.fromUri(Uri.parse(song.primaryUri));
        player.setMediaItem(item);
        player.setVolume(masterVolume / 100f);
        player.prepare();
        player.play();
        pauseButton.setText("暂停");
        if (song.hasLyrics()) {
            String uri = song.lyricUri;
            worker.execute(() -> { List<LrcLine> parsed = LyricsParser.parse(getContentResolver(), uri); runOnUiThread(() -> { lyrics.clear(); lyrics.addAll(parsed); }); });
        }
    }

    private void switchAudio(boolean accompaniment) {
        if (currentSong == null) return;
        if (accompaniment && !currentSong.hasAccompaniment()) { toast("这首歌没有关联伴奏"); return; }
        if (accompaniment == accompanimentMode) return;
        long position = player.getCurrentPosition();
        boolean wasPlaying = player.isPlaying();
        String uri = accompaniment ? currentSong.accompanimentUri : currentSong.primaryUri;
        accompanimentMode = accompaniment;
        player.setMediaItem(MediaItem.fromUri(Uri.parse(uri)));
        player.prepare();
        player.seekTo(Math.max(0, position));
        applyVolume();
        if (wasPlaying) player.play();
        statusMessage(accompaniment ? "已切换伴奏（音量 40%）" : "已切换原唱");
    }

    private void togglePause() {
        if (currentSong == null) return;
        if (player.isPlaying()) { player.pause(); pauseButton.setText("继续"); }
        else { player.play(); pauseButton.setText("暂停"); }
    }

    private void playNext() {
        if (queue.isEmpty()) { toast("队列已经结束"); return; }
        KtvSong next = queue.remove(0);
        if (queueAdapter != null) queueAdapter.replace(queue);
        playNow(next);
    }

    private void toggleFavorite() {
        if (currentSong == null) return;
        currentSong.favorite = !currentSong.favorite;
        MediaFolderScanner.save(this, allSongs);
        updateFilter();
        toast(currentSong.favorite ? "已收藏" : "已取消收藏");
    }

    private boolean queueContains(String songId) {
        for (KtvSong song : queue) if (song.id.equals(songId)) return true;
        return false;
    }

    private void removeQueuedSong(String songId) {
        for (int index = queue.size() - 1; index >= 0; index--) if (queue.get(index).id.equals(songId)) queue.remove(index);
    }

    private void applyVolume() { if (player != null) player.setVolume((masterVolume / 100f) * (accompanimentMode ? 0.4f : 1f)); }

    private void showQueueDialog() {
        if (wideLayout) { toast("队列显示在右侧"); return; }
        ListView list = new ListView(this);
        QueueAdapter adapter = new QueueAdapter(this);
        adapter.replace(queue);
        list.setAdapter(adapter);
        AlertDialog dialog = new AlertDialog.Builder(this).setTitle("播放队列").setView(list).setNegativeButton("关闭", null).create();
        list.setOnItemClickListener((parent, view, position, id) -> { KtvSong song = queue.remove(position); adapter.replace(queue); if (queueAdapter != null) queueAdapter.replace(queue); dialog.dismiss(); playNow(song); });
        dialog.show();
    }

    private void scheduleProgress() {
        handler.postDelayed(new Runnable() {
            @Override public void run() { updateLyrics(); handler.postDelayed(this, 250); }
        }, 250);
    }

    private void updateLyrics() {
        if (lyrics.isEmpty() || currentSong == null) return;
        int index = LyricsParser.currentIndex(lyrics, player.getCurrentPosition());
        if (index < 0) return;
        lyricCurrent.setText(lyrics.get(index).text);
        lyricNext.setText(index + 1 < lyrics.size() ? lyrics.get(index + 1).text : "");
    }

    @Override public void onPlaybackStateChanged(int state) {
        if (state == Player.STATE_ENDED) playNext();
        if (state == Player.STATE_READY && currentSong != null) pauseButton.setText(player.isPlaying() ? "暂停" : "继续");
    }

    @Override public void onIsPlayingChanged(boolean isPlaying) { if (pauseButton != null) pauseButton.setText(isPlaying ? "暂停" : "继续"); }
    @Override public void onPlayerError(PlaybackException error) { statusMessage("播放失败：" + error.getErrorCodeName()); }

    @Override public void onConfigurationChanged(Configuration configuration) {
        super.onConfigurationChanged(configuration);
        buildUi();
        if (currentSong == null) return;
        currentTitle.setText(currentSong.title);
        currentArtist.setText(currentSong.artist.isEmpty() ? "未知歌手" : currentSong.artist);
        emptyState.setVisibility(View.GONE);
        accompanimentButton.setEnabled(currentSong.hasAccompaniment());
        pauseButton.setText(player.isPlaying() ? "暂停" : "继续");
    }

    private LinearLayout column(int color) { LinearLayout value = new LinearLayout(this); value.setOrientation(LinearLayout.VERTICAL); value.setBackgroundColor(color); return value; }
    private LinearLayout row(int color) { LinearLayout value = new LinearLayout(this); value.setOrientation(LinearLayout.HORIZONTAL); value.setGravity(Gravity.CENTER_VERTICAL); value.setBackgroundColor(color); return value; }
    private LinearLayout.LayoutParams weight(float value) { return new LinearLayout.LayoutParams(0, -1, value); }
    private LinearLayout.LayoutParams heightWeight(float value) { return new LinearLayout.LayoutParams(-1, 0, value); }
    private TextView text(String value, float size, int color, boolean bold) { TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color); view.setTypeface(Typeface.DEFAULT, bold ? Typeface.BOLD : Typeface.NORMAL); return view; }
    private TextView lyricText(String value, float size, int color) { TextView view = text(value, size, color, true); view.setGravity(Gravity.CENTER); view.setSingleLine(true); view.setShadowLayer(5, 2, 2, Color.BLACK); return view; }
    private Button button(String value) { Button button = new Button(this); button.setText(value); button.setTextColor(Color.WHITE); button.setTextSize(13); button.setAllCaps(false); button.setPadding(dp(3), 0, dp(3), 0); button.setBackgroundResource(com.xinmu.homektv.R.drawable.button_background); button.setFocusable(true); return button; }
    private void statusMessage(String value) { if (status != null) status.setText(value); else toast(value); }
    private void toast(String value) { Toast.makeText(this, value, Toast.LENGTH_SHORT).show(); }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        worker.shutdownNow();
        if (player != null) player.release();
        super.onDestroy();
    }
}
