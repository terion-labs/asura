import ApplicationServices
import Foundation

/// A one-purpose app that runs the ADR 0061 evidence collector under its own
/// Accessibility grant.
///
/// macOS attributes Accessibility to the app responsible for a process, and a
/// process started from a shell belongs to whatever app hosts that shell: a
/// terminal or an IDE. Granting one of those would let anything run from it
/// operate every application. Launched through LaunchServices, this bundle is
/// its own responsible app, so the grant names exactly this runner and the
/// processes it starts.
///
/// Usage, through `open -W -n <bundle> --args`:
///   --status <file>  where the outcome is written: "untrusted", or "exit <code>"
///   --log <file>     where the command's output is written
///   --cwd <dir>      the command's working directory
///   --prompt         when not trusted, ask macOS to show its grant prompt
///   -- <command...>  an absolute executable path and its arguments
@main
enum EvidenceRunner {
    static func main() {
        var status: String?
        var log: String?
        var directory: String?
        var prompt = false
        var command: [String] = []
        var arguments = CommandLine.arguments.dropFirst()
        while let argument = arguments.popFirst() {
            switch argument {
            case "--status": status = arguments.popFirst()
            case "--log": log = arguments.popFirst()
            case "--cwd": directory = arguments.popFirst()
            case "--prompt": prompt = true
            case "--": command = Array(arguments); arguments = []
            default: exit(64)
            }
        }

        guard let status, let log, let directory, let executable = command.first,
              executable.hasPrefix("/") else {
            exit(64)
        }

        guard AXIsProcessTrusted() else {
            if prompt {
                let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
                _ = AXIsProcessTrustedWithOptions(options)
            }
            write("untrusted\n", to: status)
            exit(3)
        }

        FileManager.default.createFile(atPath: log, contents: nil)
        guard let output = FileHandle(forWritingAtPath: log) else {
            write("exit 70\n", to: status)
            exit(70)
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = Array(command.dropFirst())
        process.currentDirectoryURL = URL(fileURLWithPath: directory)
        process.standardOutput = output
        process.standardError = output
        do {
            try process.run()
        } catch {
            write("exit 71\n", to: status)
            exit(71)
        }

        process.waitUntilExit()
        write("exit \(process.terminationStatus)\n", to: status)
        exit(process.terminationStatus)
    }

    private static func write(_ text: String, to path: String) {
        FileManager.default.createFile(atPath: path, contents: Data(text.utf8))
    }
}
