import Foundation

enum LibraryScanner {
    static func scan(folder: URL) throws -> [Song] {
        let keys: Set<URLResourceKey> = [.isRegularFileKey, .isDirectoryKey, .isHiddenKey]
        guard let enumerator = FileManager.default.enumerator(
            at: folder,
            includingPropertiesForKeys: Array(keys),
            options: [.skipsHiddenFiles, .skipsPackageDescendants]
        ) else { return [] }

        var filesByFolder: [URL: [URL]] = [:]
        for case let url as URL in enumerator {
            let values = try? url.resourceValues(forKeys: keys)
            guard values?.isRegularFile == true else { continue }
            let ext = url.pathExtension.lowercased()
            guard MediaRules.videoExtensions.contains(ext) || MediaRules.audioExtensions.contains(ext) ||
                    MediaRules.lyricExtensions.contains(ext) || MediaRules.imageExtensions.contains(ext) else { continue }
            filesByFolder[url.deletingLastPathComponent(), default: []].append(url)
        }

        return filesByFolder.compactMap { directory, files in
            let media = files.filter(MediaRules.isMedia)
            guard !media.isEmpty else { return nil }
            let primary = media.sorted { lhs, rhs in
                if MediaRules.isAccompaniment(lhs) != MediaRules.isAccompaniment(rhs) { return !MediaRules.isAccompaniment(lhs) }
                let lhsVideo = MediaRules.videoExtensions.contains(lhs.pathExtension.lowercased())
                let rhsVideo = MediaRules.videoExtensions.contains(rhs.pathExtension.lowercased())
                if lhsVideo != rhsVideo { return lhsVideo }
                return lhs.lastPathComponent.localizedStandardCompare(rhs.lastPathComponent) == .orderedAscending
            }.first!
            let accompaniment = media.filter { $0 != primary }.sorted { lhs, rhs in
                if MediaRules.isAccompaniment(lhs) != MediaRules.isAccompaniment(rhs) { return MediaRules.isAccompaniment(lhs) }
                return lhs.lastPathComponent.localizedStandardCompare(rhs.lastPathComponent) == .orderedAscending
            }.first(where: MediaRules.isAccompaniment)
                ?? fallbackAccompaniment(primary: primary, media: media)
            let identity = MediaRules.titleAndArtist(folder: directory, primary: primary)
            let lyric = bestSidecar(files, primary: primary, extensions: MediaRules.lyricExtensions, preferLrc: true)
            let cover = bestSidecar(files, primary: primary, extensions: MediaRules.imageExtensions, preferLrc: false)
            return Song(title: identity.0, artist: identity.1, sourceFolder: directory.path,
                        originalPath: primary.path, accompanimentPath: accompaniment?.path,
                        lyricPath: lyric?.path, coverPath: cover?.path)
        }.sorted { lhs, rhs in
            lhs.title.localizedStandardCompare(rhs.title) == .orderedAscending
        }
    }

    private static func fallbackAccompaniment(primary: URL, media: [URL]) -> URL? {
        guard MediaRules.videoExtensions.contains(primary.pathExtension.lowercased()) else { return nil }
        let audio = media.filter { $0 != primary && MediaRules.audioExtensions.contains($0.pathExtension.lowercased()) }
        return audio.count == 1 ? audio[0] : nil
    }

    private static func bestSidecar(_ files: [URL], primary: URL, extensions: Set<String>, preferLrc: Bool) -> URL? {
        let stem = primary.deletingPathExtension().lastPathComponent.lowercased()
        return files.filter { extensions.contains($0.pathExtension.lowercased()) }.sorted { lhs, rhs in
            let lhsMatches = lhs.deletingPathExtension().lastPathComponent.lowercased() == stem
            let rhsMatches = rhs.deletingPathExtension().lastPathComponent.lowercased() == stem
            if lhsMatches != rhsMatches { return lhsMatches }
            if preferLrc && lhs.pathExtension.lowercased() != rhs.pathExtension.lowercased() {
                return lhs.pathExtension.lowercased() == "lrc"
            }
            return lhs.lastPathComponent.localizedStandardCompare(rhs.lastPathComponent) == .orderedAscending
        }.first
    }
}
