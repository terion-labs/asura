using Avalonia.Input;
using Exclr8Cef.WebView;

namespace Asura.BrowserAgentAcceptance;

internal static class BrowserKeyTranslationAcceptance
{
    private const string FrameArea = "frame-area";

    public static async Task VerifyAsync(WebView view, CancellationToken cancellationToken)
    {
        var browser = view.Browser ?? throw new InvalidOperationException("The fixture browser is not ready.");
        await browser.EvaluateJavaScriptAsync("""
            (() => {
              const input = document.createElement('input');
              input.id = 'key-translation-probe';
              document.body.append(input);
              const next = document.createElement('input');
              next.id = 'key-translation-next';
              document.body.append(next);
              const area = document.createElement('textarea');
              area.id = 'key-translation-area';
              document.body.append(area);
              window.keyProbeEvents = [];
              for (const type of ['keydown', 'keyup']) {
                for (const target of [input, next]) {
                  target.addEventListener(type, e => window.keyProbeEvents.push([e.type, e.key, e.code]));
                }
              }
            })()
            """).WaitAsync(cancellationToken);
        view.Focus();
        var failures = new List<string>();
        try
        {
            // Enter at the Avalonia boundary, not SendKeyEvent or CDP input:
            // both bypass the translation that physical keystrokes use.
            await CheckAsync(Key.Left, KeyModifiers.None, "ArrowLeft", "ArrowLeft", 4);
            await CheckAsync(Key.Right, KeyModifiers.None, "ArrowRight", "ArrowRight", 6);
            await CheckAsync(Key.Up, KeyModifiers.None, "ArrowUp", "ArrowUp", 0);
            await CheckAsync(Key.Down, KeyModifiers.None, "ArrowDown", "ArrowDown", 10);
            await CheckAsync(Key.Tab, KeyModifiers.None, "Tab", "Tab", 5);
            await CheckAsync(Key.Enter, KeyModifiers.None, "Enter", "Enter", 5);
            await CheckAsync(Key.Escape, KeyModifiers.None, "Escape", "Escape", 5);
            await CheckAsync(Key.F1, KeyModifiers.None, "F1", "F1", 5);
            await CheckAsync(Key.Back, KeyModifiers.None, "Backspace", "Backspace", 4);
            await CheckAsync(Key.Delete, KeyModifiers.None, "Delete", "Delete", 5);
            await CheckAsync(Key.LWin, KeyModifiers.Meta, "Meta", "MetaLeft", 5);
            await CheckAsync(Key.RWin, KeyModifiers.Meta, "Meta", "MetaRight", 5);
            await CheckAsync(Key.LeftShift, KeyModifiers.Shift, "Shift", "ShiftLeft", 5);
            await CheckAsync(Key.RightShift, KeyModifiers.Shift, "Shift", "ShiftRight", 5);
            await CheckAsync(Key.LeftCtrl, KeyModifiers.Control, "Control", "ControlLeft", 5);
            await CheckAsync(Key.RightCtrl, KeyModifiers.Control, "Control", "ControlRight", 5);
            await CheckAsync(Key.LeftAlt, KeyModifiers.Alt, "Alt", "AltLeft", 5);
            await CheckAsync(Key.RightAlt, KeyModifiers.Alt, "Alt", "AltRight", 5);

            // Mac text navigation: Cocoa delivers the Command ones as editing
            // commands (moveToBeginningOfLine: and the like), which an
            // off-screen browser never receives; Asura performs them.
            // In several lines, the line and the whole text are different ends.
            await CaretAsync(Key.Left, KeyModifiers.Meta, "ab\\ncd\\nef", 4, 3, 3, element: "key-translation-area");
            await CaretAsync(Key.Right, KeyModifiers.Meta, "ab\\ncd\\nef", 4, 5, 5, element: "key-translation-area");
            await CaretAsync(Key.Up, KeyModifiers.Meta, "ab\\ncd\\nef", 4, 0, 0, element: "key-translation-area");
            await CaretAsync(Key.Down, KeyModifiers.Meta, "ab\\ncd\\nef", 4, 8, 8, element: "key-translation-area");
            await CaretAsync(Key.Down, KeyModifiers.Meta | KeyModifiers.Shift, "ab\\ncd\\nef", 4, 4, 8, element: "key-translation-area");
            // In a frame, which only the focused frame's own editing reaches.
            // Made here: its document loads on its own schedule, and the
            // earlier checks must not share the page with that load.
            await browser.EvaluateJavaScriptAsync("""
                (() => {
                  const frame = document.createElement('iframe');
                  frame.id = 'key-translation-frame';
                  frame.srcdoc = '<textarea id="frame-area"></textarea>';
                  document.body.append(frame);
                })()
                """).WaitAsync(cancellationToken);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var loaded = await browser.EvaluateJavaScriptAsync(
                    "!!document.getElementById('key-translation-frame').contentDocument?.getElementById('frame-area')")
                    .WaitAsync(cancellationToken);
                if (loaded.Contains("true", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(50, cancellationToken);
            }

            await CaretAsync(Key.Up, KeyModifiers.Meta, "ab\\ncd\\nef", 4, 0, 0, element: FrameArea);
            await CaretAsync(Key.Down, KeyModifiers.Meta, "ab\\ncd\\nef", 4, 8, 8, element: FrameArea);
            await CaretAsync(Key.Up, KeyModifiers.Meta | KeyModifiers.Shift, "ab\\ncd\\nef", 4, 0, 4, element: FrameArea);
            await CaretAsync(Key.Left, KeyModifiers.Meta, "abcdefghij", 5, 0, 0);
            await CaretAsync(Key.Right, KeyModifiers.Meta, "abcdefghij", 5, 10, 10);
            await CaretAsync(Key.Up, KeyModifiers.Meta, "abcdefghij", 5, 0, 0);
            await CaretAsync(Key.Down, KeyModifiers.Meta, "abcdefghij", 5, 10, 10);
            await CaretAsync(Key.Left, KeyModifiers.Meta | KeyModifiers.Shift, "abcdefghij", 5, 0, 5);
            await CaretAsync(Key.Right, KeyModifiers.Meta | KeyModifiers.Shift, "abcdefghij", 5, 5, 10);
            await CaretAsync(Key.Left, KeyModifiers.Alt, "abc def ghi", 6, 4, 4);
            await CaretAsync(Key.Right, KeyModifiers.Alt, "abc def ghi", 5, 7, 7);
            await CaretAsync(Key.Back, KeyModifiers.Meta, "abc def ghi", 7, 0, 0, expectedValue: " ghi");
            await CaretAsync(Key.Back, KeyModifiers.Alt, "abc def ghi", 7, 4, 4, expectedValue: "abc  ghi");

            // The page sees the original Command key, and a page that uses it
            // keeps it: only a key nothing used is performed as navigation.
            await browser.EvaluateJavaScriptAsync("""
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  input.value = 'abcdefghij'; input.focus(); input.setSelectionRange(5, 5);
                  window.commandKeys = [];
                  input.onkeydown = e => {
                    if (e.metaKey) window.commandKeys.push(e.key);
                    if (e.metaKey && e.key === 'ArrowRight') e.preventDefault();
                  };
                })()
                """).WaitAsync(cancellationToken);
            Send(Key.Left, KeyModifiers.Meta, false);
            Send(Key.Left, KeyModifiers.Meta, true);
            Send(Key.Right, KeyModifiers.Meta, false);
            Send(Key.Right, KeyModifiers.Meta, true);
            const string identityScript = """
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  const ok = JSON.stringify(window.commandKeys) === '["ArrowLeft","ArrowRight"]'
                    && input.selectionStart === 0 && input.selectionEnd === 0;
                  return ok ? 'PASS' : JSON.stringify({keys: window.commandKeys, start: input.selectionStart, end: input.selectionEnd});
                })()
                """;
            var identity = await ReadUntilPassAsync(identityScript);
            if (identity.Contains("PASS", StringComparison.Ordinal))
            {
                // The page kept Cmd+Right, so nothing may move the caret
                // afterwards: give a wrongly performed one time to land.
                await Task.Delay(150, cancellationToken);
                identity = await browser.EvaluateJavaScriptAsync(identityScript).WaitAsync(cancellationToken);
            }

            await browser.EvaluateJavaScriptAsync("document.getElementById('key-translation-probe').onkeydown = null")
                .WaitAsync(cancellationToken);
            if (!identity.Contains("PASS", StringComparison.Ordinal))
            {
                failures.Add("Command keys reach the page and a handled one stays the page's: " + identity);
            }

            // A real shortcut must still work after a standalone modifier.
            Send(Key.LWin, KeyModifiers.Meta, false);
            Send(Key.A, KeyModifiers.Meta, false);
            Send(Key.A, KeyModifiers.Meta, true);
            Send(Key.LWin, KeyModifiers.None, true);
            var selected = await ReadUntilPassAsync("""
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  return input.selectionStart === 0 && input.selectionEnd === input.value.length
                    ? 'PASS'
                    : JSON.stringify({start: input.selectionStart, end: input.selectionEnd});
                })()
                """);
            if (!selected.Contains("PASS", StringComparison.Ordinal))
            {
                failures.Add("Cmd+A did not select the fixture text: " + selected);
            }
            if (failures.Count != 0)
            {
                throw new InvalidOperationException("Keyboard translation failed: " + string.Join("; ", failures));
            }
            Console.WriteLine("PASS Avalonia-to-CEF navigation, editing, modifiers, Mac Command text navigation and Cmd+A with balanced DOM key events");
        }
        finally
        {
            await browser.EvaluateJavaScriptAsync("document.getElementById('key-translation-probe').remove(); document.getElementById('key-translation-area').remove(); document.getElementById('key-translation-frame').remove(); document.getElementById('key-translation-next').remove()")
                .WaitAsync(cancellationToken);
        }

        async Task CheckAsync(Key key, KeyModifiers modifiers, string domKey, string domCode, int caret)
        {
            var selectionEnd = modifiers == KeyModifiers.None ? 5 : 7;
            await browser.EvaluateJavaScriptAsync($$"""
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  input.value = 'abcdefghij'; input.focus(); input.setSelectionRange(5, {{selectionEnd}});
                  window.keyProbeEvents = [];
                })()
                """).WaitAsync(cancellationToken);
            Send(key, modifiers, false);
            Send(key, KeyModifiers.None, true);
            var result = await ReadUntilPassAsync($$"""
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  const expected = [['keydown', '{{domKey}}', '{{domCode}}'], ['keyup', '{{domKey}}', '{{domCode}}']];
                  const ok = input.selectionStart === {{caret}} && input.selectionEnd === {{(modifiers == KeyModifiers.None ? caret : selectionEnd)}}
                    && JSON.stringify(window.keyProbeEvents) === JSON.stringify(expected);
                  return ok ? 'PASS' : JSON.stringify({start: input.selectionStart, end: input.selectionEnd, events: window.keyProbeEvents});
                })()
                """);
            if (!result.Contains("PASS", StringComparison.Ordinal))
            {
                failures.Add(key + ": " + result);
            }
        }

        async Task CaretAsync(
            Key key,
            KeyModifiers modifiers,
            string value,
            int caret,
            int expectedStart,
            int expectedEnd,
            string? expectedValue = null,
            string element = "key-translation-probe")
        {
            var target = string.Equals(element, FrameArea, StringComparison.Ordinal)
                ? "document.getElementById('key-translation-frame').contentDocument.getElementById('frame-area')"
                : $"document.getElementById('{element}')";
            await browser.EvaluateJavaScriptAsync($$"""
                (() => {
                  const input = {{target}};
                  input.value = '{{value}}'; input.focus(); input.setSelectionRange({{caret}}, {{caret}});
                })()
                """).WaitAsync(cancellationToken);
            Send(key, modifiers, false);
            Send(key, modifiers, true);
            var result = await ReadUntilPassAsync($$"""
                (() => {
                  const input = {{target}};
                  const ok = input.selectionStart === {{expectedStart}} && input.selectionEnd === {{expectedEnd}}
                    && input.value === '{{expectedValue ?? value}}';
                  return ok ? 'PASS' : JSON.stringify({start: input.selectionStart, end: input.selectionEnd, value: input.value});
                })()
                """);
            if (!result.Contains("PASS", StringComparison.Ordinal))
            {
                failures.Add(element + " " + modifiers + "+" + key + ": " + result);
            }
        }

        // Key events reach the renderer asynchronously, and a Command key the
        // page leaves unhandled returns to the browser process before its
        // navigation is sent back. A fixed pause read the caret too early on
        // a slow release runner, so each check waits for its expected state.
        async Task<string> ReadUntilPassAsync(string script)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                var result = await browser.EvaluateJavaScriptAsync(script).WaitAsync(cancellationToken);
                if (result.Contains("PASS", StringComparison.Ordinal) || DateTime.UtcNow >= deadline)
                {
                    return result;
                }

                await Task.Delay(50, cancellationToken);
            }
        }

        void Send(Key key, KeyModifiers modifiers, bool release) => view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = release ? InputElement.KeyUpEvent : InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
    }
}
