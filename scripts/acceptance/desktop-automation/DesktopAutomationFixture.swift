import AppKit
import Foundation

// The target application for ADR 0061 evidence runs. Asura's governed
// desktop.* tools observe and operate this window; the event log this process
// writes is the independent witness of what actually reached each control.
//
// Every control is stock AppKit with no accessibility overrides, so what a
// snapshot sees here is what it sees in an ordinary Mac application. Delivery
// is observed from the inside instead: target/action counts each activation,
// and a state reconciliation records every value change no matter which path
// (keystroke, AXValue, AXSelectedText, AXPress) produced it.

private let fixtureBundleIdentifier = "dev.asura-fixture.desktop-automation"
private let fixtureDisplayName = "Desktop Automation Fixture"
private let eventLogFormatVersion = 1
private let scrollRowCount = 40
private let popUpOptions = ["Vanilla", "Chocolate", "Strawberry", "Pistachio"]

private enum Identifier {
    static let window = "fixture.window"
    static let firstTextField = "fixture.text-field-1"
    static let secondTextField = "fixture.text-field-2"
    static let secureField = "fixture.secure-field"
    static let checkbox = "fixture.checkbox"
    static let popUp = "fixture.popup"
    static let disclosure = "fixture.disclosure"
    static let disclosureContent = "fixture.disclosure-content"
    static let scrollView = "fixture.scroll-view"
    static let table = "fixture.table"
    static let actionButton = "fixture.action-button"
    static let slowButton = "fixture.slow-button"
    static let disabledButton = "fixture.disabled-button"
    static let sheetButton = "fixture.sheet-button"
    static let alertButton = "fixture.alert-button"
    static let staleTarget = "fixture.stale-target"
    static let twinButton = "fixture.twin-button"
    static let status = "fixture.status"
    static let sheet = "fixture.sheet"
    static let sheetSave = "fixture.sheet.save"
    static let sheetCancel = "fixture.sheet.cancel"
    static let sheetDontSave = "fixture.sheet.dont-save"
    static let alert = "fixture.alert"
    static let alertOK = "fixture.alert.ok"
    static let alertCancel = "fixture.alert.cancel"
    static let menu = "fixture.menu"
    static let menuApplication = "fixture.menu.application"
    static let menuEdit = "fixture.menu.edit"
    static let menuRecord = "fixture.menu.record"
    static let menuVariants = "fixture.menu.variants"
    static let menuQuit = "fixture.menu.quit"

    static func row(_ number: Int) -> String { "fixture.row-\(number)" }
    static func menuVariant(_ number: Int) -> String { "fixture.menu.variant-\(number)" }
    static func popUpOption(_ title: String) -> String { "fixture.popup.\(title.lowercased())" }
}

private struct UsageError: Error {
    let message: String
}

private struct LaunchOptions {
    var eventLogPath: String
    var pidFilePath: String
    var controlFilePath: String
    var quitAfterSeconds = 600
    var slowActionSeconds = 8
    var selfExercise = false

    static let usage = """
        Usage: DesktopAutomationFixture [--event-log <path>] [--pid-file <path>]
                                        [--control-file <path>] [--quit-after <seconds>]
                                        [--slow-action-seconds <seconds>] [--self-exercise]
        """

    /// A launch without arguments is how desktop.launch_app starts the fixture,
    /// so every path has a default the evidence driver can predict.
    static func parse(_ arguments: [String]) throws -> LaunchOptions {
        let defaultDirectory = userTemporaryDirectory() + fixtureBundleIdentifier
        var options = LaunchOptions(
            eventLogPath: defaultDirectory + "/events.jsonl",
            pidFilePath: defaultDirectory + "/fixture.pid",
            controlFilePath: defaultDirectory + "/control")
        var remaining = arguments[...]
        while let argument = remaining.popFirst() {
            switch argument {
            case "--event-log":
                options.eventLogPath = try path(after: argument, in: &remaining)
            case "--pid-file":
                options.pidFilePath = try path(after: argument, in: &remaining)
            case "--control-file":
                options.controlFilePath = try path(after: argument, in: &remaining)
            case "--quit-after":
                options.quitAfterSeconds = try seconds(after: argument, in: &remaining, range: 1...86_400)
            case "--slow-action-seconds":
                options.slowActionSeconds = try seconds(after: argument, in: &remaining, range: 1...60)
            case "--self-exercise":
                options.selfExercise = true
            default:
                throw UsageError(message: "Unknown argument: \(argument)")
            }
        }
        return options
    }

    private static func path(after option: String, in remaining: inout ArraySlice<String>) throws -> String {
        guard let value = remaining.popFirst(), value.hasPrefix("/") else {
            throw UsageError(message: "\(option) requires an absolute path.")
        }
        return value
    }

    private static func seconds(
        after option: String,
        in remaining: inout ArraySlice<String>,
        range: ClosedRange<Int>
    ) throws -> Int {
        guard let text = remaining.popFirst(), let value = Int(text), range.contains(value) else {
            throw UsageError(
                message: "\(option) requires whole seconds from \(range.lowerBound) to \(range.upperBound).")
        }
        return value
    }

    /// The per-user temporary directory, read from the system rather than from
    /// TMPDIR: a launch through LaunchServices and a launch from a shell must
    /// agree on where the default files are.
    private static func userTemporaryDirectory() -> String {
        var buffer = [CChar](repeating: 0, count: Int(PATH_MAX))
        guard confstr(_CS_DARWIN_USER_TEMP_DIR, &buffer, buffer.count) > 0 else {
            return "/tmp/"
        }
        return String(cString: buffer)
    }
}

/// The files this process leaves for an evidence driver: the append-only event
/// log and the pid file whose existence means the window is up.
///
/// Safe from any thread. The safety timer and signal handlers run off the main
/// thread on purpose, so the fixture still records its exit and goes away when
/// the main thread is stalled or inside a modal loop.
private final class RunRecord: @unchecked Sendable {
    private let gate = NSLock()
    private let logDescriptor: Int32
    private let pidFilePath: String
    private let processIdentifier = Int(ProcessInfo.processInfo.processIdentifier)
    private let timestamps: ISO8601DateFormatter
    private var sequence = 0
    private var finished = false

    init(options: LaunchOptions) throws {
        for path in [options.eventLogPath, options.pidFilePath, options.controlFilePath] {
            try FileManager.default.createDirectory(
                atPath: (path as NSString).deletingLastPathComponent,
                withIntermediateDirectories: true)
        }
        // O_APPEND with one write(2) per line keeps each line whole and never
        // destroys an earlier run's lines; readers tell runs apart by pid.
        logDescriptor = open(options.eventLogPath, O_WRONLY | O_APPEND | O_CREAT | O_CLOEXEC, 0o600)
        guard logDescriptor >= 0 else {
            throw UsageError(message: "Cannot open the event log: \(String(cString: strerror(errno)))")
        }
        pidFilePath = options.pidFilePath
        timestamps = ISO8601DateFormatter()
        timestamps.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    }

    /// Appends one event line and returns its sequence number.
    @discardableResult
    func append(_ event: String, _ fields: [String: Any] = [:]) -> Int {
        gate.lock()
        defer { gate.unlock() }
        return appendLocked(event, fields)
    }

    func writePidFile() throws {
        try Data("\(processIdentifier)\n".utf8).write(
            to: URL(fileURLWithPath: pidFilePath),
            options: .atomic)
    }

    /// Records the exit once and removes the pid file if it is still ours.
    func finish(reason: String) {
        gate.lock()
        defer { gate.unlock() }
        guard !finished else { return }
        finished = true
        appendLocked("exiting", ["reason": reason])
        if let recorded = try? String(contentsOfFile: pidFilePath, encoding: .utf8),
           recorded.trimmingCharacters(in: .whitespacesAndNewlines) == String(processIdentifier) {
            try? FileManager.default.removeItem(atPath: pidFilePath)
        }
    }

    @discardableResult
    private func appendLocked(_ event: String, _ fields: [String: Any]) -> Int {
        sequence += 1
        var line = fields
        line["event"] = event
        line["pid"] = processIdentifier
        line["seq"] = sequence
        line["time"] = timestamps.string(from: Date())
        var bytes = jsonData(line)
        bytes.append(0x0A)
        bytes.withUnsafeBytes { buffer in
            var offset = 0
            while offset < buffer.count {
                let written = write(logDescriptor, buffer.baseAddress! + offset, buffer.count - offset)
                if written < 0 {
                    if errno == EINTR { continue }
                    return
                }
                offset += written
            }
        }
        return sequence
    }
}

private func jsonData(_ value: Any) -> Data {
    do {
        return try JSONSerialization.data(
            withJSONObject: value,
            options: [.sortedKeys, .withoutEscapingSlashes, .fragmentsAllowed])
    } catch {
        fatalError("The fixture produced a value JSON cannot represent: \(error)")
    }
}

/// What one control currently shows, as the fields of its `state` event.
private typealias ControlState = [String: AnyHashable]

@MainActor
private final class FixtureController: NSObject, NSApplicationDelegate, NSTableViewDataSource,
    NSTableViewDelegate
{
    private let options: LaunchOptions
    private let record: RunRecord

    private let window = NSWindow(
        contentRect: NSRect(x: 0, y: 0, width: 580, height: 600),
        styleMask: [.titled, .closable, .miniaturizable],
        backing: .buffered,
        defer: false)
    private let firstTextField = NSTextField(string: "")
    private let secondTextField = NSTextField(string: "")
    private let secureField = NSSecureTextField(string: "")
    private let checkbox = NSButton(checkboxWithTitle: "Enable Option", target: nil, action: nil)
    private let popUp = NSPopUpButton(frame: .zero, pullsDown: false)
    private let disclosure = NSButton(title: "", target: nil, action: nil)
    private let disclosureContent = NSTextField(labelWithString: "Details are expanded.")
    private let scrollView = NSScrollView()
    private let tableView = NSTableView()
    private let staleTargetRow = NSStackView()
    private let statusLabel = NSTextField(labelWithString: "")
    private var staleTargetButton: NSButton?
    private var sheetAlert: NSAlert?
    private var modalAlert: NSAlert?

    private var actionCounts: [String: Int] = [:]
    private var recordedStates: [String: ControlState] = [:]
    private var controlFileOffset: UInt64 = 0
    private var controlFileRemainder = Data()
    private var exerciseSteps: [() -> Void] = []
    private var exitSources: [DispatchSourceProtocol] = []
    private var activity: NSObjectProtocol?

    init(options: LaunchOptions, record: RunRecord) {
        self.options = options
        self.record = record
    }

    // MARK: Launch and exit

    func applicationDidFinishLaunching(_ notification: Notification) {
        // App Nap would stretch the timers below to seconds as soon as Asura
        // covers this window, and the control channel would appear to hang.
        activity = ProcessInfo.processInfo.beginActivity(
            options: .userInitiatedAllowingIdleSystemSleep,
            reason: "Desktop automation evidence run")
        NSApp.mainMenu = makeMainMenu()
        buildWindow()
        prepareControlFile()

        if let screen = NSScreen.main {
            window.setFrameTopLeftPoint(
                NSPoint(x: screen.visibleFrame.minX + 80, y: screen.visibleFrame.maxY - 80))
        }
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(firstTextField)
        NSApp.activate(ignoringOtherApps: true)
        reconcileState()

        NSAppleEventManager.shared().setEventHandler(
            self,
            andSelector: #selector(handleQuitAppleEvent(_:withReplyEvent:)),
            forEventClass: AEEventClass(kCoreEventClass),
            andEventID: AEEventID(kAEQuitApplication))
        NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            self?.recordKey(event)
            return event
        }
        // Common modes keep the tick alive while a menu is tracking or the
        // modal alert is up.
        let tick = Timer(
            timeInterval: 0.05, target: self, selector: #selector(tick), userInfo: nil, repeats: true)
        RunLoop.main.add(tick, forMode: .common)
        scheduleExits()

        do {
            try record.writePidFile()
        } catch {
            record.finish(reason: "pid-file-failed")
            exit(73)
        }
        recordEvent("ready", ["windowNumber": window.windowNumber, "windowVisible": window.isVisible])

        if options.selfExercise {
            exerciseSteps = makeExerciseSteps()
            let exercise = Timer(
                timeInterval: 0.25, target: self, selector: #selector(runNextExerciseStep),
                userInfo: nil, repeats: true)
            RunLoop.main.add(exercise, forMode: .common)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
        true
    }

    /// Every quit request lands here: the Quit menu item, and the quit Apple
    /// event that NSRunningApplication.terminate(), and so desktop.close_app,
    /// sends. AppKit's own handler drops that event without a trace while a
    /// sheet is attached; taking it over keeps the attempt on record, which is
    /// what lets a log without this event prove that nothing was dispatched.
    @objc private func requestQuit() {
        let sheetAttached = window.attachedSheet != nil
        recordEvent("quit-requested", ["sheetAttached": sheetAttached])
        // A document application keeps running until its prompt is answered.
        if !sheetAttached {
            NSApp.terminate(nil)
        }
    }

    @objc private func handleQuitAppleEvent(
        _ event: NSAppleEventDescriptor,
        withReplyEvent reply: NSAppleEventDescriptor
    ) {
        requestQuit()
    }

    func applicationWillTerminate(_ notification: Notification) {
        record.finish(reason: "quit-requested")
    }

    private func scheduleExits() {
        let record = record
        let exitQueue = DispatchQueue.global(qos: .userInitiated)
        let safetyTimer = DispatchSource.makeTimerSource(queue: exitQueue)
        safetyTimer.schedule(deadline: .now() + .seconds(options.quitAfterSeconds))
        safetyTimer.setEventHandler {
            record.finish(reason: "quit-after")
            _exit(0)
        }
        safetyTimer.resume()
        exitSources.append(safetyTimer)

        for signalNumber in [SIGTERM, SIGINT] {
            signal(signalNumber, SIG_IGN)
            let source = DispatchSource.makeSignalSource(signal: signalNumber, queue: exitQueue)
            source.setEventHandler {
                record.finish(reason: "signal")
                _exit(0)
            }
            source.resume()
            exitSources.append(source)
        }
    }

    // MARK: Interface

    private func makeMainMenu() -> NSMenu {
        let mainMenu = NSMenu()

        let applicationMenu = NSMenu()
        let quit = NSMenuItem(
            title: "Quit \(fixtureDisplayName)", action: #selector(requestQuit), keyEquivalent: "q")
        quit.target = self
        identify(quit, Identifier.menuQuit)
        applicationMenu.addItem(quit)
        let application = submenuItem(applicationMenu)
        identify(application, Identifier.menuApplication)
        mainMenu.addItem(application)

        // Without these items the field editor ignores Command-A, Command-V
        // and the other editing chords a keyboard-driven clear relies on.
        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        editMenu.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "Z")
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(
            withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        let edit = submenuItem(editMenu)
        identify(edit, Identifier.menuEdit)
        mainMenu.addItem(edit)

        let fixtureMenu = NSMenu(title: "Fixture")
        fixtureMenu.addItem(
            recordingMenuItem("Record Menu Action", Identifier.menuRecord, keyEquivalent: "r"))
        let variantsMenu = NSMenu(title: "Variants")
        for (index, name) in ["One", "Two", "Three"].enumerated() {
            variantsMenu.addItem(
                recordingMenuItem("Variant \(name)", Identifier.menuVariant(index + 1)))
        }
        let variants = submenuItem(variantsMenu)
        identify(variants, Identifier.menuVariants)
        fixtureMenu.addItem(variants)
        let fixture = submenuItem(fixtureMenu)
        identify(fixture, Identifier.menu)
        mainMenu.addItem(fixture)

        return mainMenu
    }

    private func submenuItem(_ submenu: NSMenu) -> NSMenuItem {
        let item = NSMenuItem(title: submenu.title, action: nil, keyEquivalent: "")
        item.submenu = submenu
        return item
    }

    private func recordingMenuItem(
        _ title: String,
        _ identifier: String,
        keyEquivalent: String = ""
    ) -> NSMenuItem {
        let item = NSMenuItem(
            title: title, action: #selector(controlActivated(_:)), keyEquivalent: keyEquivalent)
        item.target = self
        identify(item, identifier)
        return item
    }

    private func buildWindow() {
        window.title = fixtureDisplayName
        window.isReleasedWhenClosed = false
        window.isRestorable = false
        window.identifier = NSUserInterfaceItemIdentifier(Identifier.window)
        window.setAccessibilityIdentifier(Identifier.window)

        configureTextField(firstTextField, Identifier.firstTextField, label: "First Text")
        configureTextField(secondTextField, Identifier.secondTextField, label: "Second Text")
        configureTextField(secureField, Identifier.secureField, label: "Secret")

        configureControl(checkbox, Identifier.checkbox)

        popUp.addItems(withTitles: popUpOptions)
        for item in popUp.itemArray {
            identify(item, Identifier.popUpOption(item.title))
        }
        popUp.setAccessibilityLabel("Flavor")
        configureControl(popUp, Identifier.popUp)

        disclosure.bezelStyle = .disclosure
        disclosure.setButtonType(.pushOnPushOff)
        disclosure.setAccessibilityLabel("Details")
        configureControl(disclosure, Identifier.disclosure)
        identify(disclosureContent, Identifier.disclosureContent)
        disclosureContent.isHidden = true

        buildScrollView()

        let disabledButton = recordingButton("Disabled Action", Identifier.disabledButton)
        disabledButton.isEnabled = false
        staleTargetRow.orientation = .horizontal
        staleTargetRow.spacing = 8
        // The twins share role, title and identifier on purpose: nothing in the
        // accessibility tree tells them apart, only the log's "instance" does.
        for tag in [1, 2] {
            let twin = recordingButton("Twin Action", Identifier.twinButton)
            twin.tag = tag
            staleTargetRow.addArrangedSubview(twin)
        }
        restoreStaleTarget()

        identify(statusLabel, Identifier.status)
        statusLabel.setAccessibilityLabel("Status")
        statusLabel.lineBreakMode = .byTruncatingTail
        statusLabel.font = .monospacedSystemFont(ofSize: 11, weight: .regular)
        statusLabel.widthAnchor.constraint(equalToConstant: 548).isActive = true

        let rows = NSStackView(views: [
            labeledRow("First Text", firstTextField),
            labeledRow("Second Text", secondTextField),
            labeledRow("Secret", secureField),
            labeledRow("Option", checkbox),
            labeledRow("Flavor", popUp),
            labeledRow("Details", buttonRow([disclosure, disclosureContent])),
            scrollView,
            buttonRow([
                recordingButton("Record Action", Identifier.actionButton),
                recordingButton("Slow Action", Identifier.slowButton),
                disabledButton,
            ]),
            buttonRow([
                recordingButton("Open Unsaved Changes Sheet", Identifier.sheetButton),
                recordingButton("Show Modal Alert", Identifier.alertButton),
            ]),
            staleTargetRow,
            statusLabel,
        ])
        rows.orientation = .vertical
        rows.alignment = .leading
        rows.spacing = 12
        rows.edgeInsets = NSEdgeInsets(top: 16, left: 16, bottom: 16, right: 16)
        rows.translatesAutoresizingMaskIntoConstraints = false

        let content = NSView()
        content.addSubview(rows)
        NSLayoutConstraint.activate([
            rows.topAnchor.constraint(equalTo: content.topAnchor),
            rows.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            rows.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            rows.bottomAnchor.constraint(lessThanOrEqualTo: content.bottomAnchor),
        ])
        window.contentView = content
        window.initialFirstResponder = firstTextField
        makeTypingLiteral()
    }

    private func buildScrollView() {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("row"))
        column.width = 520
        tableView.addTableColumn(column)
        tableView.headerView = nil
        tableView.style = .plain
        tableView.rowHeight = 22
        tableView.allowsEmptySelection = true
        tableView.allowsMultipleSelection = false
        tableView.dataSource = self
        tableView.delegate = self
        tableView.setAccessibilityLabel("Rows")
        identify(tableView, Identifier.table)

        scrollView.documentView = tableView
        scrollView.hasVerticalScroller = true
        scrollView.borderType = .bezelBorder
        scrollView.setAccessibilityLabel("Rows")
        identify(scrollView, Identifier.scrollView)
        NSLayoutConstraint.activate([
            scrollView.widthAnchor.constraint(equalToConstant: 548),
            scrollView.heightAnchor.constraint(equalToConstant: 134),
        ])
    }

    /// Typed text must arrive exactly as sent. The window's field editor would
    /// otherwise apply the user's autocorrection and smart-punctuation
    /// preferences to it.
    private func makeTypingLiteral() {
        guard let editor = window.fieldEditor(true, for: nil) as? NSTextView else { return }
        editor.isAutomaticSpellingCorrectionEnabled = false
        editor.isAutomaticQuoteSubstitutionEnabled = false
        editor.isAutomaticDashSubstitutionEnabled = false
        editor.isAutomaticTextReplacementEnabled = false
        editor.isAutomaticTextCompletionEnabled = false
        editor.isContinuousSpellCheckingEnabled = false
    }

    private func configureTextField(_ field: NSTextField, _ identifier: String, label: String) {
        field.setAccessibilityLabel(label)
        field.usesSingleLineMode = true
        // The action then means exactly one thing: Return was pressed here.
        field.cell?.sendsActionOnEndEditing = false
        field.widthAnchor.constraint(equalToConstant: 320).isActive = true
        configureControl(field, identifier)
    }

    private func configureControl(_ control: NSControl, _ identifier: String) {
        control.target = self
        control.action = #selector(controlActivated(_:))
        identify(control, identifier)
    }

    private func recordingButton(_ title: String, _ identifier: String) -> NSButton {
        let button = NSButton(title: title, target: nil, action: nil)
        configureControl(button, identifier)
        return button
    }

    private func labeledRow(_ label: String, _ control: NSView) -> NSStackView {
        let caption = NSTextField(labelWithString: label)
        caption.widthAnchor.constraint(equalToConstant: 96).isActive = true
        return buttonRow([caption, control])
    }

    private func buttonRow(_ views: [NSView]) -> NSStackView {
        let row = NSStackView(views: views)
        row.orientation = .horizontal
        row.spacing = 8
        return row
    }

    private func identify(_ view: NSView, _ identifier: String) {
        view.identifier = NSUserInterfaceItemIdentifier(identifier)
        view.setAccessibilityIdentifier(identifier)
    }

    private func identify(_ item: NSMenuItem, _ identifier: String) {
        item.identifier = NSUserInterfaceItemIdentifier(identifier)
        item.setAccessibilityIdentifier(identifier)
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        scrollRowCount
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        // A fresh label per row, never a reused cell, so each row keeps its own
        // accessibility identifier.
        let label = NSTextField(labelWithString: "Row \(row + 1)")
        label.setAccessibilityIdentifier(Identifier.row(row + 1))
        return label
    }

    // MARK: Actions

    /// The single action every fixture control and menu item sends. An entry in
    /// the log therefore means AppKit delivered one activation to that control.
    @objc private func controlActivated(_ sender: NSObject) {
        guard let identifier = (sender as? NSUserInterfaceItemIdentification)?.identifier?.rawValue
        else { return }
        var fields: [String: Any] = [:]
        if identifier == Identifier.twinButton {
            fields["instance"] = (sender as? NSButton)?.tag == 1 ? "first" : "second"
        }
        if identifier == Identifier.slowButton {
            fields["stallSeconds"] = options.slowActionSeconds
        }
        disclosureContent.isHidden = disclosure.state != .on
        recordAction(identifier, fields)

        switch identifier {
        case Identifier.sheetButton:
            presentUnsavedChangesSheet()
        case Identifier.alertButton:
            presentModalAlert()
        case Identifier.slowButton:
            // The press is already delivered when the application stops
            // answering, which is the outcome-unknown case.
            stallMainThread()
            recordEvent("stall-ended", ["control": identifier])
        default:
            break
        }
    }

    /// Holds the main thread, so the application answers no accessibility
    /// message until this returns.
    private func stallMainThread() {
        Thread.sleep(forTimeInterval: TimeInterval(options.slowActionSeconds))
    }

    /// The stall a control command asks for. No input is in flight when it
    /// starts, so an action attempted during it times out before delivery.
    @objc private func runCommandedStall() {
        stallMainThread()
        recordEvent("stall-ended", ["command": "stall"])
    }

    private func recordAction(_ control: String, _ extra: [String: Any] = [:]) {
        let count = actionCounts[control, default: 0] + 1
        actionCounts[control] = count
        var fields = extra
        fields["control"] = control
        fields["count"] = count
        recordEvent("action", fields)
        reconcileState()
    }

    @discardableResult
    private func presentUnsavedChangesSheet() -> Bool {
        guard sheetAlert == nil, window.isVisible else { return false }
        let alert = NSAlert()
        alert.messageText = "Do you want to save the changes made to the document “Untitled”?"
        alert.informativeText = "Your changes will be lost if you don’t save them."
        identify(alert.addButton(withTitle: "Save"), Identifier.sheetSave)
        identify(alert.addButton(withTitle: "Cancel"), Identifier.sheetCancel)
        // A plain apostrophe, unlike the system's own prompt: an evidence
        // driver has to name this button.
        let dontSave = alert.addButton(withTitle: "Don't Save")
        dontSave.hasDestructiveAction = true
        identify(dontSave, Identifier.sheetDontSave)
        alert.window.setAccessibilityIdentifier(Identifier.sheet)
        sheetAlert = alert
        alert.beginSheetModal(for: window) { [weak self] response in
            self?.sheetEnded(response)
        }
        reconcileState()
        return true
    }

    private func sheetEnded(_ response: NSApplication.ModalResponse) {
        sheetAlert = nil
        switch response {
        case .alertFirstButtonReturn: recordAction(Identifier.sheetSave)
        case .alertSecondButtonReturn: recordAction(Identifier.sheetCancel)
        case .alertThirdButtonReturn: recordAction(Identifier.sheetDontSave)
        default: reconcileState()
        }
    }

    /// The alert starts once the press that asked for it has returned. A press
    /// animates the button inside an event-tracking loop, and a modal loop
    /// started in there would hold the caller's accessibility message until
    /// the alert was dismissed; leaving event tracking out of the modes makes
    /// the run loop wait for the press to finish.
    private func presentModalAlert() {
        perform(
            #selector(runModalAlert), with: nil, afterDelay: 0, inModes: [.default, .modalPanel])
    }

    @objc private func runModalAlert() {
        guard modalAlert == nil else { return }
        let alert = NSAlert()
        alert.messageText = "Fixture Alert"
        alert.informativeText = "This alert is application-modal. Choose a button to dismiss it."
        identify(alert.addButton(withTitle: "OK"), Identifier.alertOK)
        identify(alert.addButton(withTitle: "Cancel"), Identifier.alertCancel)
        alert.window.title = "Fixture Alert"
        alert.window.setAccessibilityIdentifier(Identifier.alert)
        modalAlert = alert
        reconcileState()
        // Runs inside the modal session, so only once the alert window is on
        // screen: the state logged above precedes that, and a driver that
        // needs the second window waits for this line instead.
        perform(#selector(recordAlertOnScreen), with: nil, afterDelay: 0, inModes: [.modalPanel])
        let response = alert.runModal()
        modalAlert = nil
        switch response {
        case .alertFirstButtonReturn: recordAction(Identifier.alertOK)
        case .alertSecondButtonReturn: recordAction(Identifier.alertCancel)
        default: reconcileState()
        }
    }

    @objc private func recordAlertOnScreen() {
        guard let alert = modalAlert, alert.window.isVisible else { return }
        recordEvent("alert-on-screen", ["control": Identifier.alert])
    }

    // MARK: Observation

    @objc private func tick() {
        readControlFile()
        reconcileState()
    }

    /// Logs one `state` event for every control whose observed state differs
    /// from the last one logged.
    private func reconcileState() {
        for (control, state) in observeState() where recordedStates[control] != state {
            recordedStates[control] = state
            var fields: [String: Any] = state.mapValues { $0.base }
            fields["control"] = control
            recordEvent("state", fields)
        }
    }

    private func observeState() -> [(String, ControlState)] {
        let visibleRows = tableView.rows(in: scrollView.documentVisibleRect)
        let selectedRow: AnyHashable =
            tableView.selectedRow >= 0 ? "Row \(tableView.selectedRow + 1)" : NSNull()
        return [
            (Identifier.window, ["value": window.isVisible]),
            (Identifier.firstTextField, ["value": currentText(firstTextField)]),
            (Identifier.secondTextField, ["value": currentText(secondTextField)]),
            // The secret itself never leaves the field: only its length is logged.
            (Identifier.secureField, ["length": currentText(secureField).count]),
            (Identifier.checkbox, ["value": checkbox.state == .on]),
            (Identifier.popUp, ["value": popUp.titleOfSelectedItem ?? ""]),
            (Identifier.disclosure, ["value": disclosure.state == .on]),
            (Identifier.scrollView, [
                "firstVisibleRow": visibleRows.location + 1,
                "lastVisibleRow": visibleRows.location + visibleRows.length,
            ]),
            (Identifier.table, ["value": selectedRow]),
            (Identifier.staleTarget, ["value": staleTargetButton != nil]),
            (Identifier.sheet, ["value": sheetAlert != nil]),
            (Identifier.alert, ["value": modalAlert != nil]),
        ]
    }

    /// While a field is being edited its text lives in the field editor, and
    /// asking the field itself would commit the edit as a side effect.
    private func currentText(_ field: NSTextField) -> String {
        field.currentEditor()?.string ?? field.stringValue
    }

    private func recordKey(_ event: NSEvent) {
        let focus = focusedControl(in: event.window ?? window)
        let focusIdentifier: Any = focus?.identifier?.rawValue ?? NSNull()
        // A key code names the character as surely as the character does.
        if focus is NSSecureTextField {
            recordEvent("key", ["focus": focusIdentifier, "redacted": true])
            return
        }
        var modifiers: [String] = []
        if event.modifierFlags.contains(.command) { modifiers.append("command") }
        if event.modifierFlags.contains(.control) { modifiers.append("control") }
        if event.modifierFlags.contains(.option) { modifiers.append("option") }
        if event.modifierFlags.contains(.shift) { modifiers.append("shift") }
        recordEvent("key", [
            "focus": focusIdentifier,
            "keyCode": Int(event.keyCode),
            "characters": event.charactersIgnoringModifiers ?? "",
            "modifiers": modifiers,
        ])
    }

    /// The fixture control that owns keyboard focus. The first responder is
    /// usually a field editor nested inside its text field, so the search walks
    /// outwards to the first identified view.
    private func focusedControl(in window: NSWindow) -> NSView? {
        var candidate = window.firstResponder as? NSView
        while let view = candidate {
            if view.identifier?.rawValue.hasPrefix("fixture.") == true {
                return view
            }
            candidate = view.superview
        }
        return nil
    }

    /// Appends an event and mirrors it in the status label, which is how the
    /// same fact becomes visible to a snapshot and to desktop.wait.
    private func recordEvent(_ event: String, _ fields: [String: Any] = [:]) {
        let sequence = record.append(event, fields)
        let summary = fields.keys.sorted().map { key in
            "\(key)=\(String(decoding: jsonData(fields[key] ?? NSNull()), as: UTF8.self))"
        }
        statusLabel.stringValue = (["#\(sequence)", event] + summary).joined(separator: " ")
    }

    // MARK: Out-of-band control

    /// Commands are only ever read from beyond the end the file had at launch,
    /// so an earlier run's commands are never replayed.
    private func prepareControlFile() {
        if !FileManager.default.fileExists(atPath: options.controlFilePath) {
            FileManager.default.createFile(atPath: options.controlFilePath, contents: nil)
        }
        let attributes = try? FileManager.default.attributesOfItem(atPath: options.controlFilePath)
        controlFileOffset = (attributes?[.size] as? NSNumber)?.uint64Value ?? 0
    }

    private func readControlFile() {
        guard let handle = FileHandle(forReadingAtPath: options.controlFilePath) else { return }
        defer { try? handle.close() }
        guard let size = try? handle.seekToEnd() else { return }
        if size < controlFileOffset {
            controlFileOffset = 0
            controlFileRemainder.removeAll()
        }
        guard size > controlFileOffset,
              (try? handle.seek(toOffset: controlFileOffset)) != nil,
              let appended = try? handle.readToEnd()
        else { return }
        controlFileOffset += UInt64(appended.count)
        controlFileRemainder.append(appended)
        while let newline = controlFileRemainder.firstIndex(of: 0x0A) {
            let line = controlFileRemainder[controlFileRemainder.startIndex..<newline]
            controlFileRemainder.removeSubrange(controlFileRemainder.startIndex...newline)
            let command = String(decoding: line, as: UTF8.self).trimmingCharacters(in: .whitespaces)
            if !command.isEmpty {
                runControlCommand(command)
            }
        }
    }

    private func runControlCommand(_ command: String) {
        var fields: [String: Any] = ["command": String(command.prefix(64))]
        let accepted: Bool
        switch command {
        case "remove-stale-target":
            accepted = removeStaleTarget()
        case "show-sheet":
            accepted = presentUnsavedChangesSheet()
        case "show-alert":
            accepted = modalAlert == nil
            presentModalAlert()
        case "close-window":
            accepted = window.isVisible && sheetAlert == nil
            if accepted { window.close() }
        case "stall":
            accepted = true
            fields["stallSeconds"] = options.slowActionSeconds
            // Deferred by one run-loop turn so this command's own line is in the log
            // before the main thread stops; a driver waits for that line.
            perform(
                #selector(runCommandedStall), with: nil, afterDelay: 0,
                inModes: [.default, .modalPanel])
        case "reset":
            accepted = true
            resetInterface()
        default:
            accepted = false
        }
        fields["accepted"] = accepted
        recordEvent("control", fields)
        reconcileState()
    }

    private func removeStaleTarget() -> Bool {
        guard let button = staleTargetButton else { return false }
        staleTargetRow.removeArrangedSubview(button)
        button.removeFromSuperview()
        staleTargetButton = nil
        return true
    }

    private func restoreStaleTarget() {
        guard staleTargetButton == nil else { return }
        let button = recordingButton("Stale Target", Identifier.staleTarget)
        staleTargetRow.insertArrangedSubview(button, at: 0)
        staleTargetButton = button
    }

    /// Returns every control to its launch state. Action counts and sequence
    /// numbers keep running: they describe the process, not the interface.
    private func resetInterface() {
        if let sheet = sheetAlert {
            window.endSheet(sheet.window, returnCode: .abort)
        }
        if modalAlert != nil {
            NSApp.abortModal()
        }
        for field in [firstTextField, secondTextField, secureField] {
            field.abortEditing()
            field.stringValue = ""
        }
        checkbox.state = .off
        popUp.selectItem(at: 0)
        disclosure.state = .off
        disclosureContent.isHidden = true
        tableView.deselectAll(nil)
        tableView.scrollRowToVisible(0)
        restoreStaleTarget()
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(firstTextField)
    }

    // MARK: Inventory

    /// Logs how every identified element answers an accessibility client, by
    /// reading this process's own tree through the client API. A process may
    /// always read itself, so this needs no Accessibility grant; it reads no
    /// other process and performs no action. It is what keeps the README's
    /// control inventory true.
    private func recordInventory(_ stage: String) {
        let application = AXUIElementCreateApplication(getpid())
        let focusedWindow = accessibilityElements(application, kAXFocusedWindowAttribute, single: true).first
        var roots = accessibilityElements(application, kAXWindowsAttribute)
        // Only the menus this fixture created: the Apple menu holds the user's
        // recent documents, which are none of an inventory's business.
        if let menuBar = accessibilityElements(application, kAXMenuBarAttribute, single: true).first {
            roots += accessibilityElements(menuBar, kAXChildrenAttribute).filter {
                fixtureIdentifier(of: $0) != nil
            }
        }

        var pending = roots.reversed().map { (element: $0, depth: 0) }
        while let (element, depth) = pending.popLast() {
            if let identifier = fixtureIdentifier(of: element) {
                var actions: CFArray?
                _ = AXUIElementCopyActionNames(element, &actions)
                record.append("inventory", [
                    "stage": stage,
                    "control": identifier,
                    "depth": depth,
                    "role": accessibilityValue(element, kAXRoleAttribute) ?? NSNull(),
                    "subrole": accessibilityValue(element, kAXSubroleAttribute) ?? NSNull(),
                    "title": accessibilityValue(element, kAXTitleAttribute) ?? NSNull(),
                    "description": accessibilityValue(element, kAXDescriptionAttribute) ?? NSNull(),
                    "modal": accessibilityValue(element, kAXModalAttribute) ?? NSNull(),
                    "actions": (actions as? [String] ?? []).sorted(),
                    "focusedWindow": focusedWindow.map { CFEqual($0, element) } ?? false,
                ])
            }
            guard depth < 8 else { continue }
            pending += accessibilityElements(element, kAXChildrenAttribute).reversed().map { ($0, depth + 1) }
        }
    }

    private func fixtureIdentifier(of element: AXUIElement) -> String? {
        guard let identifier = accessibilityValue(element, kAXIdentifierAttribute) as? String,
              identifier.hasPrefix("fixture.")
        else { return nil }
        return identifier
    }

    private func accessibilityValue(_ element: AXUIElement, _ attribute: String) -> AnyObject? {
        var value: AnyObject?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else {
            return nil
        }
        return value
    }

    private func accessibilityElements(
        _ element: AXUIElement,
        _ attribute: String,
        single: Bool = false
    ) -> [AXUIElement] {
        guard let value = accessibilityValue(element, attribute) else { return [] }
        if single {
            return CFGetTypeID(value) == AXUIElementGetTypeID() ? [value as! AXUIElement] : []
        }
        return value as? [AXUIElement] ?? []
    }

    // MARK: Self-exercise

    /// Activates every control from inside the process and inventories the
    /// tree an accessibility client is shown, so the event log format and the
    /// control inventory can be checked on a machine with no Accessibility
    /// grant. The `launched` event says so, and evidence drivers must reject
    /// such a log: nothing in it was delivered by an accessibility client.
    private func makeExerciseSteps() -> [() -> Void] {
        [
            { self.recordInventory("launch") },
            { self.replaceText(of: self.firstTextField, with: "alpha") },
            { self.replaceText(of: self.secondTextField, with: "beta") },
            { self.replaceText(of: self.secureField, with: "s3cret-fixture-value") },
            { self.checkbox.performClick(nil) },
            { self.popUp.menu?.performActionForItem(at: 2) },
            { self.disclosure.performClick(nil) },
            { self.recordInventory("expanded") },
            { self.performMenuItem(Identifier.menuRecord) },
            { self.performMenuItem(Identifier.menuVariant(2)) },
            { self.tableView.scrollRowToVisible(scrollRowCount - 1) },
            { self.tableView.selectRowIndexes([scrollRowCount - 1], byExtendingSelection: false) },
            { self.button(Identifier.actionButton)?.performClick(nil) },
            { self.button(Identifier.actionButton)?.performClick(nil) },
            { self.button(Identifier.disabledButton)?.performClick(nil) },
            { self.twin(tag: 1)?.performClick(nil) },
            { self.twin(tag: 2)?.performClick(nil) },
            { self.staleTargetButton?.performClick(nil) },
            { self.window.makeFirstResponder(self.secondTextField) },
            { self.postKeyDown("x", keyCode: 7) },
            { self.postKeyDown("\r", keyCode: 36) },
            { self.window.makeFirstResponder(self.secureField) },
            { self.postKeyDown("z", keyCode: 6) },
            { self.runControlCommand("remove-stale-target") },
            { self.runControlCommand("not-a-command") },
            { self.runControlCommand("reset") },
            { self.button(Identifier.sheetButton)?.performClick(nil) },
            { self.recordInventory("sheet") },
            { self.sheetAlert?.buttons[2].performClick(nil) },
            { self.button(Identifier.alertButton)?.performClick(nil) },
            { self.recordInventory("alert") },
            { self.modalAlert?.buttons[0].performClick(nil) },
            { self.runControlCommand("show-sheet") },
            { self.requestQuit() },
            { self.runControlCommand("reset") },
            { self.runControlCommand("show-alert") },
            { self.runControlCommand("reset") },
            { self.runControlCommand("close-window") },
            { self.runControlCommand("reset") },
            { self.runControlCommand("stall") },
            { self.button(Identifier.slowButton)?.performClick(nil) },
        ]
    }

    @objc private func runNextExerciseStep() {
        reconcileState()
        guard !exerciseSteps.isEmpty else {
            record.finish(reason: "self-exercise-complete")
            exit(0)
        }
        exerciseSteps.removeFirst()()
    }

    private func replaceText(of field: NSTextField, with text: String) {
        field.abortEditing()
        field.stringValue = text
    }

    private func button(_ identifier: String) -> NSButton? {
        descendants(of: window.contentView).first { $0.identifier?.rawValue == identifier }
    }

    private func twin(tag: Int) -> NSButton? {
        descendants(of: window.contentView).first {
            $0.identifier?.rawValue == Identifier.twinButton && $0.tag == tag
        }
    }

    private func descendants(of view: NSView?) -> [NSButton] {
        guard let view else { return [] }
        return view.subviews.flatMap { subview in
            ((subview as? NSButton).map { [$0] } ?? []) + descendants(of: subview)
        }
    }

    private func performMenuItem(_ identifier: String) {
        func search(_ menu: NSMenu) -> Bool {
            for (index, item) in menu.items.enumerated() {
                if item.identifier?.rawValue == identifier {
                    menu.performActionForItem(at: index)
                    return true
                }
                if let submenu = item.submenu, search(submenu) {
                    return true
                }
            }
            return false
        }
        if let mainMenu = NSApp.mainMenu {
            _ = search(mainMenu)
        }
    }

    private func postKeyDown(_ characters: String, keyCode: UInt16) {
        guard let event = NSEvent.keyEvent(
            with: .keyDown,
            location: .zero,
            modifierFlags: [],
            timestamp: ProcessInfo.processInfo.systemUptime,
            windowNumber: window.windowNumber,
            context: nil,
            characters: characters,
            charactersIgnoringModifiers: characters,
            isARepeat: false,
            keyCode: keyCode)
        else { return }
        NSApp.postEvent(event, atStart: false)
    }
}

@main
private enum DesktopAutomationFixture {
    @MainActor
    static func main() {
        let options: LaunchOptions
        let record: RunRecord
        do {
            options = try LaunchOptions.parse(Array(CommandLine.arguments.dropFirst()))
            record = try RunRecord(options: options)
        } catch {
            let message = (error as? UsageError)?.message ?? String(describing: error)
            FileHandle.standardError.write(Data("\(message)\n\(LaunchOptions.usage)\n".utf8))
            exit(64)
        }
        record.append("launched", [
            "bundleIdentifier": Bundle.main.bundleIdentifier ?? NSNull(),
            "formatVersion": eventLogFormatVersion,
            "quitAfterSeconds": options.quitAfterSeconds,
            "selfExercise": options.selfExercise,
            "slowActionSeconds": options.slowActionSeconds,
        ])

        let application = NSApplication.shared
        let controller = FixtureController(options: options, record: record)
        application.delegate = controller
        application.setActivationPolicy(.regular)
        withExtendedLifetime(controller) {
            application.run()
        }
    }
}
