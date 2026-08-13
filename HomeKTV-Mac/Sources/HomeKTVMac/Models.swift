import Foundation

enum AudioMode: String, Codable, CaseIterable {
    case original = "原唱"
    case accompaniment = "伴奏"
}

enum LibraryFilter: String, CaseIterable, Identifiable {
    case all = "全部歌曲"
    case favorites = "我的收藏"
    case accompaniment = "有伴奏"
    case lyrics = "有歌词"
    case video = "MV 视频"
    case audio = "纯音频"

    var id: String { rawValue }
}

struct LyricLine: Identifiable, Hashable {
    let time: TimeInterval
    let text: String
    var id: String { "\(time)-\(text)" }
}

struct Song: Identifiable, Codable, Hashable {
    var id: UUID = UUID()
    var title: String
    var artist: String
    var sourceFolder: String
    var originalPath: String
    var accompanimentPath: String?
    var lyricPath: String?
    var coverPath: String?
    var isFavorite = false
    var lastPlayedAt: Date?

    var originalURL: URL { URL(fileURLWithPath: originalPath) }
    var accompanimentURL: URL? { accompanimentPath.map { URL(fileURLWithPath: $0) } }
    var lyricURL: URL? { lyricPath.map { URL(fileURLWithPath: $0) } }
    var coverURL: URL? { coverPath.map { URL(fileURLWithPath: $0) } }
    var hasAccompaniment: Bool { accompanimentPath != nil }
    var hasLyrics: Bool { lyricPath != nil }
    var isVideo: Bool { MediaRules.videoExtensions.contains(originalURL.pathExtension.lowercased()) }
    var displayArtist: String { artist.isEmpty ? "未知歌手" : artist }
}

enum MediaRules {
    static let videoExtensions: Set<String> = ["mp4", "mkv", "mov", "m4v", "avi", "mpg", "mpeg", "webm"]
    static let audioExtensions: Set<String> = ["mp3", "m4a", "aac", "wav", "aiff", "flac", "ogg", "opus"]
    static let lyricExtensions: Set<String> = ["lrc", "txt"]
    static let imageExtensions: Set<String> = ["jpg", "jpeg", "png", "webp", "heic", "bmp"]
    static let accompanimentMarkers = ["伴奏", "纯音乐", "消音", "无人声", "instrumental", "karaoke", "backing track", "backing", "off vocal", "no vocal"]
    static let originalMarkers = ["原唱", "原版", "original", "mv"]

    static func isMedia(_ url: URL) -> Bool {
        let ext = url.pathExtension.lowercased()
        return videoExtensions.contains(ext) || audioExtensions.contains(ext)
    }

    static func isAccompaniment(_ url: URL) -> Bool {
        let name = url.deletingPathExtension().lastPathComponent.lowercased()
        return accompanimentMarkers.contains { name.contains($0) }
    }

    static func titleAndArtist(folder: URL, primary: URL) -> (String, String) {
        let folderName = trimRole(folder.lastPathComponent)
        if let parsed = parseArtistAndTitle(folderName) { return parsed }
        let fileName = trimRole(primary.deletingPathExtension().lastPathComponent)
        if let parsed = parseArtistAndTitle(fileName) { return parsed }
        return (fileName.isEmpty ? folder.lastPathComponent : fileName, "未知歌手")
    }

    private static func parseArtistAndTitle(_ name: String) -> (String, String)? {
        let separators = [" - ", "－", "—", "-"]
        for separator in separators {
            let pieces = name.components(separatedBy: separator).map { $0.trimmingCharacters(in: .whitespaces) }
            if pieces.count == 2, !pieces[0].isEmpty, !pieces[1].isEmpty {
                return (pieces[1], pieces[0])
            }
        }
        return nil
    }

    private static func trimRole(_ text: String) -> String {
        var result = text
        for marker in accompanimentMarkers + originalMarkers {
            result = result.replacingOccurrences(of: marker, with: "", options: .caseInsensitive)
        }
        return result.trimmingCharacters(in: CharacterSet(charactersIn: " -_()[]（）【】"))
    }
}
