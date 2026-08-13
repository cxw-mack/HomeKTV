import Foundation

enum LyricsParser {
    static func parse(file: URL?) -> [LyricLine] {
        guard let file else { return [] }
        // 0x80000632 is the NSString encoding value for GB18030, common in older Chinese LRC files.
        let encodings: [String.Encoding] = [.utf8, .utf16, String.Encoding(rawValue: 0x80000632)]
        guard let content = encodings.lazy.compactMap({ try? String(contentsOf: file, encoding: $0) }).first else { return [] }
        return parse(content: content)
    }

    static func parse(content: String) -> [LyricLine] {
        let pattern = #"\[(\d{1,3}):(\d{1,2})(?:[\.:](\d{1,3}))?\]"#
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        var output: [LyricLine] = []
        for rawLine in content.components(separatedBy: .newlines) {
            let range = NSRange(rawLine.startIndex..., in: rawLine)
            let matches = regex.matches(in: rawLine, range: range)
            guard !matches.isEmpty else { continue }
            let text = regex.stringByReplacingMatches(in: rawLine, range: range, withTemplate: "").trimmingCharacters(in: .whitespaces)
            guard !text.isEmpty else { continue }
            for match in matches {
                let minutes = Double((rawLine as NSString).substring(with: match.range(at: 1))) ?? 0
                let seconds = Double((rawLine as NSString).substring(with: match.range(at: 2))) ?? 0
                guard seconds < 60 else { continue }
                let fractionText = match.range(at: 3).location == NSNotFound ? "" : (rawLine as NSString).substring(with: match.range(at: 3))
                let fraction: Double
                switch fractionText.count {
                case 1: fraction = (Double(fractionText) ?? 0) / 10
                case 2: fraction = (Double(fractionText) ?? 0) / 100
                default: fraction = (Double(fractionText) ?? 0) / 1000
                }
                output.append(LyricLine(time: minutes * 60 + seconds + fraction, text: text))
            }
        }
        return output.sorted { $0.time < $1.time }
    }
}
