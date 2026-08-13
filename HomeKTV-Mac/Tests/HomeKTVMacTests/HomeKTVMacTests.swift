import XCTest
@testable import HomeKTVMac

final class HomeKTVMacTests: XCTestCase {
    func testParsesMultipleLrcTimestamps() {
        let lines = LyricsParser.parse(content: "[00:01.20][00:02.30]Hello")
        XCTAssertEqual(lines.count, 2)
        XCTAssertEqual(lines[0].time, 1.2, accuracy: 0.001)
        XCTAssertEqual(lines[1].time, 2.3, accuracy: 0.001)
    }

    func testRejectsInvalidLrcSecondsAndParsesMillisecondPrecision() {
        let lines = LyricsParser.parse(content: "[00:60.00]invalid\n[01:02.345]valid")

        XCTAssertEqual(lines.count, 1)
        XCTAssertEqual(lines[0].text, "valid")
        XCTAssertEqual(lines[0].time, 62.345, accuracy: 0.001)
    }

    func testRecognizesAccompanimentName() {
        XCTAssertTrue(MediaRules.isAccompaniment(URL(fileURLWithPath: "/music/test-伴奏.mp3")))
        XCTAssertTrue(MediaRules.isAccompaniment(URL(fileURLWithPath: "/music/test instrumental.flac")))
        XCTAssertFalse(MediaRules.isAccompaniment(URL(fileURLWithPath: "/music/test-原唱.mp4")))
    }

    func testParsesArtistAndTitleFromSongFolder() {
        let result = MediaRules.titleAndArtist(
            folder: URL(fileURLWithPath: "/Music/周杰伦 - 七里香 伴奏"),
            primary: URL(fileURLWithPath: "/Music/周杰伦 - 七里香 伴奏/周杰伦 - 七里香 原唱.mp4")
        )

        XCTAssertEqual(result.0, "七里香")
        XCTAssertEqual(result.1, "周杰伦")
    }

    func testScannerUsesOriginalMediaAndMatchesSidecars() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let songFolder = root.appendingPathComponent("歌手 - 歌名", isDirectory: true)
        try FileManager.default.createDirectory(at: songFolder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }

        let original = songFolder.appendingPathComponent("歌手 - 歌名 原唱.mp4")
        let accompaniment = songFolder.appendingPathComponent("歌手 - 歌名 伴奏.mp3")
        let lyrics = songFolder.appendingPathComponent("歌手 - 歌名 原唱.lrc")
        let cover = songFolder.appendingPathComponent("cover.jpg")
        for file in [original, accompaniment, lyrics, cover] {
            XCTAssertTrue(FileManager.default.createFile(atPath: file.path, contents: Data()))
        }

        let songs = try LibraryScanner.scan(folder: root)
        XCTAssertEqual(songs.count, 1)
        assertSameFile(songs[0].originalPath, original)
        assertSameFile(songs[0].accompanimentPath, accompaniment)
        assertSameFile(songs[0].lyricPath, lyrics)
        assertSameFile(songs[0].coverPath, cover)
    }

    func testScannerPrefersVideoOriginalAndLrcOverTextLyrics() throws {
        let root = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let folder = root.appendingPathComponent("歌手 - 歌名", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

        let video = folder.appendingPathComponent("歌手 - 歌名 original.mp4")
        let audio = folder.appendingPathComponent("歌手 - 歌名 vocal.mp3")
        let lrc = folder.appendingPathComponent("歌手 - 歌名 original.lrc")
        let text = folder.appendingPathComponent("lyrics.txt")
        createEmptyFiles([video, audio, lrc, text])

        let songs = try LibraryScanner.scan(folder: root)
        XCTAssertEqual(songs.count, 1)
        assertSameFile(songs[0].originalPath, video)
        assertSameFile(songs[0].lyricPath, lrc)
    }

    func testScannerUsesSingleUnmarkedAudioAsVideoAccompanimentFallback() throws {
        let root = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        let folder = root.appendingPathComponent("测试歌曲", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

        let original = folder.appendingPathComponent("测试歌曲.mp4")
        let fallback = folder.appendingPathComponent("测试歌曲.mp3")
        createEmptyFiles([original, fallback])

        let songs = try LibraryScanner.scan(folder: root)
        XCTAssertEqual(songs.count, 1)
        assertSameFile(songs[0].originalPath, original)
        assertSameFile(songs[0].accompanimentPath, fallback)
    }

    func testScannerSkipsEmptyFolders() throws {
        let root = temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: root) }
        try FileManager.default.createDirectory(at: root.appendingPathComponent("空目录"), withIntermediateDirectories: true)

        XCTAssertTrue(try LibraryScanner.scan(folder: root).isEmpty)
    }

    private func temporaryDirectory() -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
    }

    private func createEmptyFiles(_ files: [URL]) {
        for file in files {
            XCTAssertTrue(FileManager.default.createFile(atPath: file.path, contents: Data()))
        }
    }

    private func assertSameFile(_ actualPath: String?, _ expectedURL: URL, file: StaticString = #filePath, line: UInt = #line) {
        guard let actualPath else {
            XCTFail("Expected a file path", file: file, line: line)
            return
        }
        let actual = URL(fileURLWithPath: actualPath).resolvingSymlinksInPath().standardizedFileURL
        let expected = expectedURL.resolvingSymlinksInPath().standardizedFileURL
        XCTAssertEqual(actual, expected, file: file, line: line)
    }
}
