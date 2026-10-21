import Foundation

/// One transcript file being followed: its tailer, its parser and the last observed stat.
final class TrackedFile {
    let path: String
    let family: TranscriptFamily
    let fileID: FileID
    var tailer: FileTailer
    var parser: TranscriptParser
    var lastStat: FileStat

    init(
        path: String,
        family: TranscriptFamily,
        fileID: FileID,
        stat: FileStat,
        start: FileTailer.Start,
        maxLineBytes: Int = FileTailer.defaultMaxLineBytes
    ) {
        self.path = path
        self.family = family
        self.fileID = fileID
        lastStat = stat
        tailer = FileTailer(path: path, start: start, maxLineBytes: maxLineBytes)
        parser = TranscriptParser(family: family, agentId: path, fileID: fileID)
    }

    /// Parser state restarts after truncation or replacement; the global dedupe set is kept.
    func resetParser() {
        parser = TranscriptParser(family: family, agentId: path, fileID: fileID)
    }

    func hasChanged(comparedTo stat: FileStat) -> Bool {
        stat.size != lastStat.size || stat.modifiedNs != lastStat.modifiedNs || stat.identity != lastStat.identity
    }
}
