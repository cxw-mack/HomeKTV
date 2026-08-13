import AppKit
import AVFoundation
import Combine
import Foundation
import SwiftUI

@MainActor
final class KTVStore: NSObject, ObservableObject, NSWindowDelegate {
    @Published private(set) var songs: [Song] = []
    @Published var queue: [Song] = []
    @Published var query = ""
    @Published var filter: LibraryFilter = .all
    @Published private(set) var currentSong: Song?
    @Published private(set) var lyrics: [LyricLine] = []
    @Published private(set) var position: Double = 0
    @Published private(set) var duration: Double = 0
    @Published private(set) var isPlaying = false
    @Published private(set) var audioMode: AudioMode = .original
    @Published var lyricsVisible = true
    @Published private(set) var status = "请选择歌曲文件夹"

    let player = AVPlayer()
    private var timeObserver: Any?
    private var endObserver: NSObjectProtocol?
    private let accompanimentVolume: Float = 0.4
    private var playerWindow: NSWindow?

    override init() {
        super.init()
        timeObserver = player.addPeriodicTimeObserver(forInterval: CMTime(seconds: 0.15, preferredTimescale: 600), queue: .main) { [weak self] time in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.position = max(0, time.seconds.isFinite ? time.seconds : 0)
                let total = self.player.currentItem?.duration.seconds ?? 0
                self.duration = total.isFinite ? max(0, total) : 0
            }
        }
        endObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.playNext()
            }
        }
    }

    deinit {
        if let timeObserver { player.removeTimeObserver(timeObserver) }
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
    }

    var visibleSongs: [Song] {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return songs.filter { song in
            let passesFilter: Bool
            switch filter {
            case .all: passesFilter = true
            case .favorites: passesFilter = song.isFavorite
            case .accompaniment: passesFilter = song.hasAccompaniment
            case .lyrics: passesFilter = song.hasLyrics
            case .video: passesFilter = song.isVideo
            case .audio: passesFilter = !song.isVideo
            }
            guard passesFilter else { return false }
            return text.isEmpty || song.title.lowercased().contains(text) || song.artist.lowercased().contains(text) || song.sourceFolder.lowercased().contains(text)
        }
    }

    var currentLyric: LyricLine? { lyrics.last(where: { $0.time <= position + 0.05 }) }
    var nextLyric: LyricLine? {
        guard let line = currentLyric, let index = lyrics.firstIndex(of: line), lyrics.indices.contains(index + 1) else { return nil }
        return lyrics[index + 1]
    }

    func chooseAndScanFolder() {
        let panel = NSOpenPanel()
        panel.title = "选择歌曲文件夹"
        panel.message = "每个歌曲文件夹可包含原唱、伴奏、歌词和封面"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let folder = panel.url else { return }
        scan(folder: folder)
    }

    func scan(folder: URL) {
        status = "正在扫描 \(folder.lastPathComponent)..."
        do {
            var scanned = try LibraryScanner.scan(folder: folder)
            let favorites = Dictionary(uniqueKeysWithValues: songs.map { ($0.originalPath, $0.isFavorite) })
            scanned = scanned.map { song in
                var copy = song
                copy.isFavorite = favorites[song.originalPath] ?? false
                return copy
            }
            songs = scanned
            saveLibrary()
            status = "已导入 \(scanned.count) 首歌曲"
        } catch {
            status = "扫描失败: \(error.localizedDescription)"
        }
    }

    func enqueue(_ song: Song) {
        guard !queue.contains(where: { $0.id == song.id }) else { return }
        queue.append(song)
        status = "已点 \(song.title)"
        if currentSong == nil { playNext() }
    }

    func playNow(_ song: Song) {
        if !queue.contains(where: { $0.id == song.id }) { queue.insert(song, at: 0) }
        start(song)
    }

    func removeFromQueue(_ song: Song) {
        queue.removeAll { $0.id == song.id }
    }

    func playNext() {
        if let currentSong { queue.removeAll { $0.id == currentSong.id } }
        guard let next = queue.first else {
            stop()
            status = "队列已播放完毕"
            return
        }
        start(next)
    }

    func togglePlayback() {
        if player.timeControlStatus == .playing {
            player.pause()
            isPlaying = false
        } else if currentSong != nil {
            player.play()
            isPlaying = true
        }
    }

    func stop() {
        player.pause()
        player.replaceCurrentItem(with: nil)
        currentSong = nil
        lyrics = []
        position = 0
        duration = 0
        isPlaying = false
    }

    func openPlayerScreen() {
        if playerWindow == nil {
            let window = NSWindow(
                contentRect: NSScreen.main?.frame ?? NSRect(x: 0, y: 0, width: 1280, height: 720),
                styleMask: [.borderless],
                backing: .buffered,
                defer: false
            )
            window.isOpaque = true
            window.backgroundColor = .black
            window.hasShadow = false
            window.level = .normal
            window.collectionBehavior = [.fullScreenAuxiliary, .canJoinAllSpaces]
            window.contentView = NSHostingView(rootView: PlayerScreenView().environmentObject(self))
            window.delegate = self
            playerWindow = window
        }

        let mainScreen = NSScreen.main
        let targetScreen = NSScreen.screens.first { screen in
            guard let mainScreen else { return true }
            return screen !== mainScreen
        } ?? mainScreen
        if let screen = targetScreen {
            playerWindow?.setFrame(screen.frame, display: true)
        }
        NSApplication.shared.activate(ignoringOtherApps: true)
        playerWindow?.makeKeyAndOrderFront(nil)
    }

    func closePlayerScreen() {
        playerWindow?.close()
    }

    func windowWillClose(_ notification: Notification) {
        guard let closingWindow = notification.object as? NSWindow,
              let activePlayerWindow = playerWindow,
              closingWindow === activePlayerWindow else { return }
        self.playerWindow = nil
    }

    func skip() { playNext() }

    func seek(to value: Double) {
        player.seek(to: CMTime(seconds: max(0, value), preferredTimescale: 600))
    }

    func switchAudio(to mode: AudioMode) {
        guard let song = currentSong else { return }
        guard mode == .original || song.hasAccompaniment else {
            status = "这首歌没有可用伴奏"
            return
        }
        guard mode != audioMode else { return }
        let wasPlaying = isPlaying
        let savedPosition = position
        audioMode = mode
        apply(song, position: savedPosition, autoplay: wasPlaying)
        status = "已切换到\(mode.rawValue)"
    }

    func toggleFavorite(_ song: Song) {
        guard let index = songs.firstIndex(where: { $0.id == song.id }) else { return }
        songs[index].isFavorite.toggle()
        if let queueIndex = queue.firstIndex(where: { $0.id == song.id }) { queue[queueIndex].isFavorite = songs[index].isFavorite }
        saveLibrary()
    }

    private func start(_ song: Song) {
        currentSong = song
        audioMode = .original
        lyrics = LyricsParser.parse(file: song.lyricURL)
        apply(song, position: 0, autoplay: true)
        if let index = songs.firstIndex(where: { $0.id == song.id }) {
            songs[index].lastPlayedAt = Date()
            currentSong = songs[index]
            saveLibrary()
        }
        status = "正在播放 \(song.title)"
    }

    private func apply(_ song: Song, position: Double, autoplay: Bool) {
        let url = audioMode == .accompaniment ? song.accompanimentURL ?? song.originalURL : song.originalURL
        let item = AVPlayerItem(url: url)
        player.replaceCurrentItem(with: item)
        player.volume = audioMode == .accompaniment ? accompanimentVolume : 1.0
        player.seek(to: CMTime(seconds: position, preferredTimescale: 600)) { [weak self] _ in
            guard let self else { return }
            if autoplay { self.player.play() }
            self.isPlaying = autoplay
        }
    }

    private var libraryFile: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("HomeKTV-Mac", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("library.json")
    }

    func restoreLibrary() {
        guard let data = try? Data(contentsOf: libraryFile), let saved = try? JSONDecoder().decode([Song].self, from: data) else { return }
        songs = saved.filter { FileManager.default.fileExists(atPath: $0.originalPath) }
        status = songs.isEmpty ? "请选择歌曲文件夹" : "已恢复 \(songs.count) 首本地歌曲"
    }

    private func saveLibrary() {
        guard let data = try? JSONEncoder().encode(songs) else { return }
        try? data.write(to: libraryFile, options: .atomic)
    }
}
