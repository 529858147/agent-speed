import AgentSpeedCore
import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// The single Usage History window (DESIGN 4b): SwiftUI content in an `NSWindow` with a unified toolbar, frame
/// remembered between launches. Reopening focuses it and applies the new filter.
@MainActor
final class HistoryWindowController: NSObject, NSWindowDelegate {
    nonisolated static let defaultSize = NSSize(width: 920, height: 680)
    nonisolated static let minimumSize = NSSize(width: 720, height: 540)
    private static let autosaveName = "AgentSpeedHistoryWindow"

    private let model: AppModel
    private var window: NSWindow?
    private var toolbar: HistoryToolbar?
    private var titleLoop: ObservationLoop?
    private(set) var viewModel: HistoryViewModel?

    init(model: AppModel) {
        self.model = model
    }

    var isVisible: Bool { window?.isVisible ?? false }

    /// Shows the window filtered to `source` (all sources when nil).
    func show(source: SourceKind?) {
        let (window, viewModel) = ensureWindow()
        viewModel.select(source: source)
        if !window.isVisible, !window.setFrameUsingName(Self.autosaveName) {
            window.setContentSize(Self.defaultSize)
            window.center()
        }
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        if !NSApp.isActive { window.orderFrontRegardless() }
    }

    func close() {
        window?.close()
    }

    private func ensureWindow() -> (NSWindow, HistoryViewModel) {
        if let window, let viewModel { return (window, viewModel) }
        let viewModel = HistoryViewModel(store: model.history, l10n: model.l10n, accessibility: model.accessibility)
        let root = HistoryView(model: viewModel).appEnvironment(model)
        let host = NSHostingController(rootView: root)
        host.sizingOptions = []

        let window = Self.makeWindow(content: host)
        window.delegate = self

        let toolbar = HistoryToolbar(model: viewModel) { [weak self] in self?.export() }
        window.toolbar = toolbar.toolbar
        if !window.setFrameUsingName(Self.autosaveName) { window.center() }
        window.setFrameAutosaveName(Self.autosaveName)

        self.window = window
        self.toolbar = toolbar
        self.viewModel = viewModel
        titleLoop = ObservationLoop { [weak self] in
            guard let self, let window = self.window else { return }
            let title = self.model.l10n("history.title")
            if window.title != title { window.title = title }
        }
        return (window, viewModel)
    }

    /// The window as the app shows it; previews build the same one.
    static func makeWindow(content: NSViewController) -> NSWindow {
        let window = NSWindow(
            contentRect: NSRect(origin: .zero, size: defaultSize),
            styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
            backing: .buffered,
            defer: true
        )
        window.contentViewController = content
        window.setContentSize(defaultSize)
        window.minSize = minimumSize
        window.titleVisibility = .hidden
        window.toolbarStyle = .unified
        window.isReleasedWhenClosed = false
        window.isRestorable = false
        window.tabbingMode = .disallowed
        window.collectionBehavior = [.moveToActiveSpace, .fullScreenPrimary]
        return window
    }

    // MARK: Export

    /// Saves the rows the table shows as CSV (UTF-8 with BOM, locale-independent columns).
    private func export() {
        guard let viewModel, let window else { return }
        let content = viewModel.content
        guard !content.rows.isEmpty else { return }
        let l10n = model.l10n
        let data = HistoryCSV.encode(HistoryCSV.build(content.rows, source: content.source))
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = HistoryCSV.fileName(content.range, source: content.source)
        panel.canCreateDirectories = true
        panel.isExtensionHidden = false
        panel.beginSheetModal(for: window) { response in
            MainActor.assumeIsolated {
                guard response == .OK, let url = panel.url else { return }
                do {
                    try data.write(to: url, options: .atomic)
                } catch {
                    let alert = NSAlert()
                    alert.alertStyle = .warning
                    alert.messageText = l10n("mac.history.export.failed")
                    alert.addButton(withTitle: l10n("mac.update.ok"))
                    alert.beginSheetModal(for: window)
                }
            }
        }
    }
}
