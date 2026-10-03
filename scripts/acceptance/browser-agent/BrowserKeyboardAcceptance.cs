using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Exclr8Cef;
using Exclr8Cef.WebView;

namespace Asura.BrowserAgentAcceptance;

internal static class BrowserKeyboardAcceptance
{
    public static async Task VerifyAsync(Control surface, Window window, CancellationToken cancellationToken)
    {
        var browser = surface.GetVisualDescendants().OfType<WebView>().Single().Browser
            ?? throw new InvalidOperationException("The fixture browser is not ready.");
        var menuInvocations = 0;
        var returnedFromPage = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReturnedKey(object? sender, PreKeyEventArgs args)
        {
            if (args.WindowsKeyCode == 0x44)
            {
                returnedFromPage.TrySetResult(true);
            }
        }
        browser.KeyEvent += OnReturnedKey;
        var item = new NativeMenuItem("Keyboard fallback sentinel")
        {
            Gesture = new KeyGesture(Key.D, KeyModifiers.None),
        };
        item.Click += (_, _) => menuInvocations++;
        var submenu = new NativeMenu();
        submenu.Items.Add(item);
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Keyboard probe") { Menu = submenu });
        NativeMenu.SetMenu(window, menu);
        try
        {
            // An unconsumed raw key is allowed to reach the page, but must not
            // be replayed against the host menu by CEF's macOS fallback.
            await Task.Delay(100, cancellationToken);
            browser.SendKeyEvent(Cef.CefKeyEventType.RawKeyDown, 0x44, 0x02,
                Cef.CefModifiers.None, 'd', 'd', false);
            browser.SendKeyEvent(Cef.CefKeyEventType.KeyUp, 0x44, 0x02,
                Cef.CefModifiers.None, 'd', 'd', false);
            await returnedFromPage.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            // Menu fallback follows the managed callback on the native stack.
            await Task.Delay(100, cancellationToken);
            if (menuInvocations != 0)
            {
                throw new InvalidOperationException("A browser key escaped into the native application menu.");
            }
            Console.WriteLine("PASS unhandled browser typing cannot activate native menu shortcuts");
        }
        finally
        {
            browser.KeyEvent -= OnReturnedKey;
            menu.Items.Clear();
        }

        var browserSurface = (Asura.Browser.BrowserSurface)surface;
        var view = surface.GetVisualDescendants().OfType<WebView>().Single();
        var blockedKeys = 0;
        void OnBlockedKey(object? sender, PreKeyEventArgs args) => blockedKeys++;
        browser.PreKeyEvent += OnBlockedKey;
        try
        {
            // The host rejects physical input by default. Tunnel forwarding
            // must not bypass that decision, including host-owned shortcuts.
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K });
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.K });
            await Task.Delay(100, cancellationToken);
            if (blockedKeys != 0)
            {
                throw new InvalidOperationException("Handled host keystrokes reached the browser.");
            }
            Console.WriteLine("PASS host-consumed keyboard events are not replayed into Chromium");
        }
        finally
        {
            browser.PreKeyEvent -= OnBlockedKey;
        }
        browserSurface.BindPhysicalInputGate(_ => true);
        try
        {
            await BrowserKeyTranslationAcceptance.VerifyAsync(view, cancellationToken);
        }
        finally
        {
            browserSurface.BindPhysicalInputGate(null);
        }
    }
}
