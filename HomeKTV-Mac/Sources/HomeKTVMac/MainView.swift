import AppKit
import AVKit
import SwiftUI

struct MainView: View {
    @EnvironmentObject private var store: KTVStore
    @State private var showingQueue = false

    var body: some View {
        NavigationSplitView {
            VStack(alignment: .leading, spacing: 14) {
                HStack(spacing: 9) {
                    Image(systemName: "music.note.tv")
                        .font(.title2)
                        .foregroundStyle(.purple)
                    Text("HomeKTV")
                        .font(.title2.weight(.semibold))
                }
                Text("家庭点歌台")
                    .foregroundStyle(.secondary)
                    .font(.subheadline)
                Divider()
                List(LibraryFilter.allCases, selection: $store.filter) { item in
                    Label(item.rawValue, systemImage: icon(for: item))
                        .tag(item)
                }
                .listStyle(.sidebar)
                Spacer()
                Button(action: store.chooseAndScanFolder) {
                    Label("导入歌曲文件夹", systemImage: "folder.badge.plus")
                }
                .buttonStyle(.borderedProminent)
                .tint(.purple)
                Text("\(store.songs.count) 首歌曲")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .padding(16)
            .frame(minWidth: 190)
        } detail: {
            VStack(spacing: 0) {
                header
                Divider()
                songList
                Divider()
                nowPlaying
            }
            .background(Color(nsColor: .windowBackgroundColor))
        }
        .navigationTitle("HomeKTV")
        .sheet(isPresented: $showingQueue) { QueueSheet().environmentObject(store) }
    }

    private var header: some View {
        HStack(spacing: 12) {
            TextField("搜索歌名、歌手或文件夹", text: $store.query)
                .textFieldStyle(.roundedBorder)
                .frame(maxWidth: 420)
            Spacer()
            Button {
                store.openPlayerScreen()
            } label: {
                Label("打开大屏", systemImage: "display")
            }
            .buttonStyle(.bordered)
            Button {
                closePlayerWindow()
            } label: {
                Label("关闭大屏", systemImage: "display.and.arrow.down")
            }
            .buttonStyle(.bordered)
            Button {
                showingQueue = true
            } label: {
                Label("队列 \(store.queue.count)", systemImage: "text.line.first.and.arrowtriangle.forward")
            }
            .buttonStyle(.borderedProminent)
            .tint(.purple)
        }
        .padding(18)
    }

    private var songList: some View {
        Group {
            if store.visibleSongs.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "music.note.list")
                        .font(.system(size: 40))
                        .foregroundStyle(.secondary)
                    Text("暂无歌曲")
                        .font(.title3.weight(.semibold))
                    Text("点击左下角导入一个包含歌曲文件夹的目录")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                List(store.visibleSongs) { song in
                    SongRow(song: song)
                        .environmentObject(store)
                }
                .listStyle(.inset)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var nowPlaying: some View {
        HStack(spacing: 18) {
            VStack(alignment: .leading, spacing: 3) {
                Text(store.currentSong?.title ?? "等待点歌")
                    .font(.headline)
                    .lineLimit(1)
                Text(store.currentSong?.displayArtist ?? store.status)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            .frame(minWidth: 220, alignment: .leading)
            Button(action: store.togglePlayback) {
                Image(systemName: store.isPlaying ? "pause.fill" : "play.fill")
            }
            .buttonStyle(.borderedProminent)
            .tint(.purple)
            .disabled(store.currentSong == nil)
            Button(action: store.skip) { Image(systemName: "forward.fill") }
                .buttonStyle(.bordered)
                .disabled(store.currentSong == nil)
            Slider(value: Binding(get: { store.position }, set: store.seek(to:)), in: 0...max(1, store.duration))
                .tint(.purple)
                .disabled(store.currentSong == nil)
            Text(time(store.position) + " / " + time(store.duration))
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .frame(width: 112, alignment: .trailing)
            Picker("", selection: Binding(get: { store.audioMode }, set: store.switchAudio(to:))) {
                ForEach(AudioMode.allCases, id: \.self) { Text($0.rawValue).tag($0) }
            }
            .labelsHidden()
            .frame(width: 92)
            .disabled(store.currentSong?.hasAccompaniment != true)
            Toggle(isOn: $store.lyricsVisible) { Image(systemName: "text.quote") }
                .toggleStyle(.button)
                .disabled(store.currentSong?.hasLyrics != true)
        }
        .padding(.horizontal, 22)
        .padding(.vertical, 14)
        .background(.ultraThinMaterial)
    }

    private func icon(for filter: LibraryFilter) -> String {
        switch filter {
        case .all: return "music.note.list"
        case .favorites: return "heart"
        case .accompaniment: return "music.quarternote.3"
        case .lyrics: return "text.quote"
        case .video: return "film"
        case .audio: return "waveform"
        }
    }

    private func time(_ seconds: Double) -> String {
        guard seconds.isFinite else { return "0:00" }
        return String(format: "%d:%02d", Int(seconds) / 60, Int(seconds) % 60)
    }

    private func closePlayerWindow() {
        store.closePlayerScreen()
    }
}

private struct SongRow: View {
    @EnvironmentObject private var store: KTVStore
    let song: Song

    var body: some View {
        HStack(spacing: 13) {
            CoverImage(url: song.coverURL)
            VStack(alignment: .leading, spacing: 4) {
                Text(song.title).font(.headline).lineLimit(1)
                Text(song.displayArtist).foregroundStyle(.secondary).lineLimit(1)
                HStack(spacing: 7) {
                    if song.isVideo { Label("MV", systemImage: "film") }
                    if song.hasAccompaniment { Label("伴奏", systemImage: "music.quarternote.3") }
                    if song.hasLyrics { Label("歌词", systemImage: "text.quote") }
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
            Spacer()
            Button { store.toggleFavorite(song) } label: {
                Image(systemName: song.isFavorite ? "heart.fill" : "heart")
                    .foregroundStyle(song.isFavorite ? .pink : .secondary)
            }
            .buttonStyle(.plain)
            Button { store.enqueue(song) } label: { Image(systemName: "text.badge.plus") }
                .buttonStyle(.bordered)
                .help("加入队列")
            Button { store.playNow(song) } label: { Image(systemName: "play.fill") }
                .buttonStyle(.borderedProminent)
                .tint(.purple)
                .help("立即播放")
        }
        .padding(.vertical, 5)
        .contextMenu {
            Button("立即播放") { store.playNow(song) }
            Button("加入队列") { store.enqueue(song) }
            Button(song.isFavorite ? "取消收藏" : "收藏") { store.toggleFavorite(song) }
        }
    }
}

private struct CoverImage: View {
    let url: URL?
    var body: some View {
        Group {
            if let url, let image = NSImage(contentsOf: url) {
                Image(nsImage: image).resizable().scaledToFill()
            } else {
                Image(systemName: "music.note").font(.title2).foregroundStyle(.purple)
            }
        }
        .frame(width: 46, height: 46)
        .background(Color.purple.opacity(0.12))
        .clipShape(RoundedRectangle(cornerRadius: 6))
    }
}

private struct QueueSheet: View {
    @EnvironmentObject private var store: KTVStore
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            List(store.queue) { song in
                HStack {
                    VStack(alignment: .leading) {
                        Text(song.title)
                        Text(song.displayArtist).font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    if store.currentSong?.id == song.id { Text("播放中").foregroundStyle(.purple) }
                    Button { store.removeFromQueue(song) } label: { Image(systemName: "trash") }
                        .buttonStyle(.borderless)
                }
            }
            .navigationTitle("点歌队列")
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("完成") { dismiss() } } }
        }
        .frame(minWidth: 460, minHeight: 400)
    }
}

struct PlayerScreenView: View {
    @EnvironmentObject private var store: KTVStore

    var body: some View {
        ZStack(alignment: .bottom) {
            Color.black.ignoresSafeArea()
            PlayerLayerView(player: store.player)
                .aspectRatio(16 / 9, contentMode: .fit)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            if store.lyricsVisible, let line = store.currentLyric {
                VStack(spacing: 7) {
                    Text(line.text)
                        .font(.system(size: 80, weight: .bold))
                        .foregroundStyle(.blue)
                        .shadow(color: .black.opacity(0.95), radius: 4, x: 2, y: 3)
                        .multilineTextAlignment(.center)
                        .lineLimit(2)
                    if let next = store.nextLyric {
                        Text(next.text)
                            .font(.system(size: 42, weight: .semibold))
                            .foregroundStyle(.white.opacity(0.9))
                            .shadow(color: .black.opacity(0.95), radius: 3, x: 1, y: 2)
                            .lineLimit(2)
                    }
                }
                .padding(.horizontal, 48)
                .padding(.bottom, 54)
            }
        }
        .background(Color.black)
    }
}

private struct PlayerLayerView: NSViewRepresentable {
    let player: AVPlayer

    func makeNSView(context: Context) -> AVPlayerView {
        let view = AVPlayerView()
        view.controlsStyle = .none
        view.videoGravity = .resizeAspect
        view.player = player
        return view
    }

    func updateNSView(_ view: AVPlayerView, context: Context) { view.player = player }
}
