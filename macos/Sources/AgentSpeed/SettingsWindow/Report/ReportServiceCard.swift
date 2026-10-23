import AgentSpeedCore
import SwiftUI

/// The service card (DESIGN 11, Service): the service's own name and words as plain text, the host that receives the
/// data, retention and the `https` links — shown only after its discovery document is known.
struct ReportServiceCard: View {
    let model: AppModel
    let service: ReportServiceURL
    let discovery: ReportDiscovery

    var body: some View {
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: 5) {
            Text(discovery.name)
                .font(.system(size: 13, weight: .semibold))
                .lineLimit(1)
            Text(l10n("report.service.host", hosts))
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .textSelection(.enabled)
            if discovery.api.isLoopbackHTTP {
                ReportLoopbackNote(model: model)
            }
            if let summary = discovery.summary {
                Text(summary)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let days = discovery.retentionDays {
                Text(l10n("report.service.retention", days))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
            HStack(spacing: Spacing.m) {
                ReportLink(title: l10n("report.service.terms"), url: discovery.terms)
                ReportLink(title: l10n("report.service.privacy"), url: discovery.privacy)
                if let leaderboard = discovery.leaderboard {
                    ReportLink(title: l10n("report.service.leaderboard"), url: leaderboard)
                }
            }
            .padding(.top, 2)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 2)
    }

    /// The API host, then the service host when it differs (REPORTING 3.3).
    private var hosts: String {
        let api = discovery.api.authority
        return api == service.authority ? api : "\(api) · \(service.authority)"
    }
}

/// A borderless link that opens only `https` URLs, in the browser, on click (REPORTING 2.3).
struct ReportLink: View {
    let title: String
    let url: URL

    var body: some View {
        if url.scheme?.lowercased() == "https" {
            Link(destination: url) {
                HStack(spacing: 2) {
                    Text(title)
                    Image(systemName: "arrow.up.right")
                        .imageScale(.small)
                        .font(.system(size: 9, weight: .semibold))
                }
            }
            .font(.system(size: 11))
            .help(url.absoluteString)
        }
    }
}

/// `report.url.loopback`: the amber note of an unencrypted local test server.
struct ReportLoopbackNote: View {
    let model: AppModel

    var body: some View {
        Label {
            Text(model.l10n("report.url.loopback"))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        } icon: {
            Image(systemName: "lock.open.fill")
                .foregroundStyle(.orange)
                .imageScale(.small)
        }
        .font(.system(size: 11))
    }
}

/// The service card's place while the document is fetched, failed or missing.
struct ReportServiceState: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        Group {
            switch report.serviceState {
            case .checking:
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text(l10n("report.service.checking"))
                }
                .foregroundStyle(.secondary)
            case .failed(let reason):
                Label(l10n("report.service.failed", reason), systemImage: "exclamationmark.triangle.fill")
                    .symbolRenderingMode(.multicolor)
            case .notSupported:
                Label(l10n("report.service.notSupported"), systemImage: "xmark.octagon.fill")
                    .symbolRenderingMode(.multicolor)
            case .gone:
                Label(l10n("report.status.gone"), systemImage: "xmark.octagon.fill")
                    .symbolRenderingMode(.multicolor)
            case .unknown, .ready:
                EmptyView()
            }
        }
        .font(.system(size: 11))
        .fixedSize(horizontal: false, vertical: true)
    }
}
