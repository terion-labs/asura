// CEF subprocess entry point for Linux.
//
// Chromium's zygote and its renderers must be single threaded when they
// enter their sandboxes (EnterLayerOneSandbox checks it and aborts), and a
// managed host process never is: the .NET runtime starts its own threads
// before any host code runs. So CEF's Linux subprocesses start here instead
// of re-executing the host (CefSettings.browser_subprocess_path). The shim
// library supplies the same Exclr8CefApp the browser process uses, so the
// render-process handlers are identical.

#include "exclr8cef.h"

int main(int argc, char** argv) {
    int code = excef_execute_process(argc, argv);
    // A subprocess returns its own exit code. Run by hand, with no --type,
    // CEF reports -1; answer with the same sentinel as the macOS helper.
    return code >= 0 ? code : 255;
}
