// The shipped CEF must write encrypted HTTP cache and reuse it after restart.
// The launcher gives this probe a disposable Keychain service and profile.
#include "exclr8cef.h"
#include <CoreFoundation/CoreFoundation.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
static atomic_int initialized;
static atomic_int loaded;
static atomic_int stopped;
static void timeout(int sig) { (void)sig; _exit(124); }
static void paint(int id, const void* data, int width, int height) {
    (void)id; (void)data; (void)width; (void)height;
}
static void ready(int id) { (void)id; atomic_store(&initialized, 1); }
static void closed(int id) { (void)id; atomic_store(&stopped, 1); }
static void ended(int id, int main, const char* url, int status) {
    (void)id;
    if (main && strstr(url, "/cache-fixture")) atomic_store(&loaded, status == 200 ? 1 : -1);
}
static int wait_for(atomic_int* flag) {
    time_t deadline = time(NULL) + 30;
    while (!atomic_load(flag) && time(NULL) < deadline) {
        excef_do_message_loop_work();
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, true);
    }
    return atomic_load(flag) == 1;
}
int main(int argc, char** argv) {
    if (argc != 4) return 64;
    signal(SIGALRM, timeout); alarm(60);
    char profile[4096];
    if (snprintf(profile, sizeof(profile), "%s/profile", argv[1]) >= (int)sizeof(profile)) return 64;
    excef_init_settings settings = {0};
    settings.root_cache_path = argv[1];
    settings.log_severity = 3;
    excef_set_init_settings(&settings);
    excef_add_command_line_switch("enable-features", "EnableCacheEncryption,EnableCacheEncryptionForTesting");
    excef_add_command_line_switch("disable-features", "OptimizationGuideOnDeviceModel,LogOnDeviceMetricsOnStartup");
    excef_add_command_line_switch("profile-directory", "Default");
    fprintf(stderr, "cache probe: initialize\n");
    if (excef_initialize_offscreen(1, argv, argv[2], NULL)) return 2;
    signal(SIGALRM, timeout);
    fprintf(stderr, "cache probe: initialized engine\n");
    // DevTools can make a durable request context Chrome's last-used profile.
    // Engine startup must not populate that directory before Asura restores it.
    char deferred_preferences[4096];
    if (snprintf(deferred_preferences, sizeof(deferred_preferences), "%s/deferred-profile/Preferences", argv[1]) >= (int)sizeof(deferred_preferences)) return 64;
    time_t startup_until = time(NULL) + 1;
    while (time(NULL) < startup_until) { excef_do_message_loop_work(); CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, true); }
    if (access(deferred_preferences, F_OK) == 0) {
        fprintf(stderr, "cache probe: engine opened deferred profile before restoration\n");
        excef_shutdown();
        return 4;
    }
    excef_set_browser_initialized_callback(ready);
    excef_set_browser_closed_callback(closed);
    excef_set_load_end_callback(ended);
    int context = excef_create_request_context(profile);
    int browser = excef_create_offscreen_browser_in_context(320, 240, 1, "about:blank", paint, context);
    fprintf(stderr, "cache probe: browser %d context %d\n", browser, context);
    int success = browser > 0 && wait_for(&initialized);
    fprintf(stderr, "cache probe: ready=%d\n", success);
    if (success) { excef_load_url(browser, argv[3]); success = wait_for(&loaded); fprintf(stderr, "cache probe: loaded=%d\n", success); }
    // Allow asynchronous cache writes to drain before initiating close.
    time_t until = time(NULL) + 2;
    while (time(NULL) < until) { excef_do_message_loop_work(); CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, true); }
    if (browser > 0) { excef_close_browser(browser, 1); success = wait_for(&stopped) && success; }
    excef_release_request_context(context);
    fprintf(stderr, "cache probe: shutdown\n");
    excef_shutdown();
    fprintf(stderr, "cache probe: done\n");
    return success ? 0 : 3;
}
