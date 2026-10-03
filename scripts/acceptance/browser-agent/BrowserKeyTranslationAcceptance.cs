using Avalonia.Input;
using Exclr8Cef.WebView;

namespace Asura.BrowserAgentAcceptance;

internal static class BrowserKeyTranslationAcceptance
{
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

            // A real shortcut must still work after a standalone modifier.
            Send(Key.LWin, KeyModifiers.Meta, false);
            Send(Key.A, KeyModifiers.Meta, false);
            Send(Key.A, KeyModifiers.Meta, true);
            Send(Key.LWin, KeyModifiers.None, true);
            await Task.Delay(80, cancellationToken);
            var selected = await browser.EvaluateJavaScriptAsync("""
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  return input.selectionStart === 0 && input.selectionEnd === input.value.length;
                })()
                """).WaitAsync(cancellationToken);
            if (!string.Equals(selected, "true", StringComparison.Ordinal))
            {
                failures.Add("Cmd+A did not select the fixture text: " + selected);
            }
            if (failures.Count != 0)
            {
                throw new InvalidOperationException("Keyboard translation failed: " + string.Join("; ", failures));
            }
            Console.WriteLine("PASS Avalonia-to-CEF navigation, editing, modifiers and Cmd+A with balanced DOM key events");
        }
        finally
        {
            await browser.EvaluateJavaScriptAsync("document.getElementById('key-translation-probe').remove(); document.getElementById('key-translation-next').remove()")
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
            // SendKeyEvent is asynchronous; allow the renderer to consume both
            // messages before reading its actual selection and DOM event log.
            await Task.Delay(80, cancellationToken);
            var result = await browser.EvaluateJavaScriptAsync($$"""
                (() => {
                  const input = document.getElementById('key-translation-probe');
                  const expected = [['keydown', '{{domKey}}', '{{domCode}}'], ['keyup', '{{domKey}}', '{{domCode}}']];
                  const ok = input.selectionStart === {{caret}} && input.selectionEnd === {{(modifiers == KeyModifiers.None ? caret : selectionEnd)}}
                    && JSON.stringify(window.keyProbeEvents) === JSON.stringify(expected);
                  return ok ? 'PASS' : JSON.stringify({start: input.selectionStart, end: input.selectionEnd, events: window.keyProbeEvents});
                })()
                """).WaitAsync(cancellationToken);
            if (!result.Contains("PASS", StringComparison.Ordinal))
            {
                failures.Add(key + ": " + result);
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
