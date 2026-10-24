// Native ABI regression: response filters must receive decoded gzip bytes,
// while oversized CDP results must be rejected before binding-owned copies.
// Managed aggregate policy has separate unit coverage; registration was source-reviewed.
#include "exclr8cef.h"
#include <CoreFoundation/CoreFoundation.h>
#include <arpa/inet.h>
#include <pthread.h>
#include <poll.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <zlib.h>
#define DECODED_BYTE_LIMIT (16 * 1024 * 1024)
static atomic_int completed;
static atomic_int stopped;
static atomic_int received;
static atomic_int rejected;
static atomic_int loaded;
static unsigned char* compressed;
static size_t compressed_size;
static char target_url[128];
static void paint(int id, const void* data, int width, int height) {
    (void)id; (void)data; (void)width; (void)height;
}
static void resource_request(int browser, uint64_t token, const char* url, const char* method,
        int type, const char* headers) {
    (void)browser; (void)url; (void)method; (void)type; (void)headers;
    excef_resolve_resource_request(token, 0, NULL);
}
static int should_filter(int browser, uint64_t token, const char* url, int status, const char* mime) {
    (void)browser; (void)token; (void)url; (void)mime;
    return status == 200;
}
static int filter(int browser, uint64_t token, const unsigned char* input, int input_size,
        unsigned char* output, int output_size, int* read, int* written) {
    (void)browser; (void)token;
    *read = *written = 0;
    int count = input_size < output_size ? input_size : output_size;
    if (atomic_load(&received) + count > DECODED_BYTE_LIMIT) {
        atomic_store(&rejected, 1);
        atomic_store(&completed, 1);
        return -1;
    }
    for (int i=0; i<count; i++) {
        if (input[i] != 'x') { atomic_store(&completed, -1); return -1; }
    }
    if (count) memcpy(output, input, (size_t)count);
    atomic_fetch_add(&received, count);
    *read = *written = count;
    if (!input_size) return 1;
    return 0;
}
static void load_failed(int id, int main_frame, int error, const char* text, const char* url) {
    (void)id;
    fprintf(stderr, "Native budget load failed: %d %s %s\n", error, text, url);
    if (main_frame && strcmp(url, target_url) == 0 && !atomic_load(&rejected))
        atomic_store(&completed, -1);
}
static void load_ended(int id, int main_frame, const char* url, int status) {
    (void)id;
    if (main_frame && status == 200 && strcmp(url, target_url) == 0) {
        atomic_store(&loaded, 1);
        atomic_store(&completed, 1);
    }
}
static void devtools(int browser, int is_event, int id, const char* json) {
    (void)browser;
    if (is_event || id != 73) return;
    int success = strlen(json) < 256 && strstr(json, "CDP response exceeds the host byte limit.") != NULL;
    fprintf(stderr, "Native CDP cap: reply-bytes=%zu over-limit-error=%d\n", strlen(json), success);
    atomic_store(&completed, success ? 1 : -1);
}
static void initialized(int id) { (void)id; atomic_store(&completed, 1); }
static void closed(int id) { (void)id; atomic_store(&completed, 1); }
static int wait_for_completion(void) {
    time_t deadline = time(NULL) + 20;
    while (!atomic_load(&completed) && time(NULL) < deadline) {
        excef_do_message_loop_work();
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, true);
    }
    return atomic_load(&completed) == 1;
}
static void respond(int client) {
        char request[2048], header[256];
        ssize_t request_size = recv(client, request, sizeof(request)-1, 0);
        if (request_size > 0) {
            request[request_size] = 0;
            if (strncmp(request, "GET /payload ", 13) != 0) {
                const char missing[] = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                send(client, missing, sizeof(missing)-1, 0);
                close(client);
                return;
            }
            int count = snprintf(header, sizeof(header), "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Encoding: gzip\r\nContent-Length: %zu\r\nConnection: close\r\n\r\n", compressed_size);
            send(client, header, (size_t)count, 0);
            for (size_t sent=0; sent<compressed_size;) {
                ssize_t written = send(client, compressed+sent, compressed_size-sent, 0);
                if (written <= 0) break;
                sent += (size_t)written;
            }
        }
    close(client);
}
static void* serve(void* argument) {
    struct pollfd sockets[16] = {0};
    for (int i=0; i<16; i++) sockets[i].fd = -1;
    sockets[0].fd = *(int*)argument;
    sockets[0].events = POLLIN;
    while (!atomic_load(&stopped)) {
        if (poll(sockets, 16, 100) <= 0) continue;
        if (sockets[0].revents & POLLIN) {
            int client = accept(sockets[0].fd, NULL, NULL);
            if (client >= 0) {
                int slot = 1;
                while (slot < 16 && sockets[slot].fd >= 0) slot++;
                if (slot == 16) close(client);
                else { sockets[slot].fd = client; sockets[slot].events = POLLIN; sockets[slot].revents = 0; }
            }
        }
        for (int i=1; i<16; i++) {
            if (sockets[i].fd >= 0 && sockets[i].revents) {
                if (sockets[i].revents & POLLIN) respond(sockets[i].fd);
                else close(sockets[i].fd);
                sockets[i].fd = -1;
            }
        }
    }
    for (int i=1; i<16; i++) if (sockets[i].fd >= 0) close(sockets[i].fd);
    return NULL;
}

int main(int argc, char** argv) {
    if (argc != 4) return 64;
    signal(SIGPIPE, SIG_IGN);
    alarm(90);
    int oversized = strcmp(argv[3], "large") == 0;
    size_t plain_size = oversized ? DECODED_BYTE_LIMIT + 65536 : 32768;
    unsigned char* plain = malloc(plain_size);
    compressed = malloc(compressBound(plain_size)+32);
    if (!plain || !compressed) return 2;
    memset(plain, 'x', plain_size);
    z_stream stream = {0};
    stream.next_in = plain;
    stream.avail_in = (uInt)plain_size;
    stream.next_out = compressed;
    stream.avail_out = (uInt)compressBound(plain_size)+32;
    if (deflateInit2(&stream, 9, Z_DEFLATED, 15+16, 8, Z_DEFAULT_STRATEGY) != Z_OK
        || deflate(&stream, Z_FINISH) != Z_STREAM_END) return 2;
    compressed_size = stream.total_out;
    deflateEnd(&stream);
    free(plain);
    int listener = socket(AF_INET, SOCK_STREAM, 0);
    struct sockaddr_in address = { .sin_family = AF_INET, .sin_addr.s_addr = htonl(INADDR_LOOPBACK) };
    if (listener < 0 || bind(listener, (struct sockaddr*)&address, sizeof(address)) || listen(listener, 16)) return 2;
    socklen_t size = sizeof(address);
    if (getsockname(listener, (struct sockaddr*)&address, &size)) return 2;
    snprintf(target_url, sizeof(target_url), "http://127.0.0.1:%d/payload", ntohs(address.sin_port));
    pthread_t server;
    if (pthread_create(&server, NULL, serve, &listener)) return 2;
    excef_init_settings settings = {0};
    settings.root_cache_path = argv[1];
    settings.log_severity = 3;
    excef_set_init_settings(&settings);
    excef_add_command_line_switch("use-mock-keychain", NULL);
    if (excef_initialize_offscreen(1, argv, argv[2], NULL)) return 3;
    excef_set_resource_request_callback(resource_request);
    excef_set_should_filter_response_callback(should_filter);
    excef_set_response_filter_callback(filter);
    excef_set_load_error_callback(load_failed);
    excef_set_load_end_callback(load_ended);
    excef_set_devtools_message_callback(devtools);
    excef_set_browser_initialized_callback(initialized);
    excef_set_browser_closed_callback(closed);
    int context = excef_create_request_context(NULL);
    int browser = excef_create_offscreen_browser_in_context(320, 240, 1, "about:blank", paint, context);
    int success = browser > 0 && wait_for_completion();
    if (success) {
        atomic_store(&completed, 0);
        excef_load_url(browser, target_url);
        success = wait_for_completion();
    }
    success = success
        && atomic_load(&rejected) == oversized
        && atomic_load(&received) <= DECODED_BYTE_LIMIT
        && (oversized || (size_t)atomic_load(&received) == plain_size);
    fprintf(stderr, "Native gzip filter: wire=%zu decoded=%zu delivered=%d rejected=%d success=%d\n", compressed_size, plain_size, atomic_load(&received), atomic_load(&rejected), success);
    if (success && !oversized) {
        time_t deadline = time(NULL) + 20;
        while (!atomic_load(&loaded) && time(NULL) < deadline) {
            excef_do_message_loop_work();
            CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.005, true);
        }
        atomic_store(&completed, 0);
        success = atomic_load(&loaded) && excef_execute_devtools_method(browser, 73, "Runtime.evaluate",
            "{\"expression\":\"'x'.repeat(9*1024*1024)\",\"returnByValue\":true}")
            && wait_for_completion();
    }
    if (browser > 0) {
        atomic_store(&completed, 0);
        excef_close_browser(browser, 1);
        if (!wait_for_completion()) success = 0;
    }
    atomic_store(&stopped, 1);
    pthread_join(server, NULL); close(listener);
    excef_release_request_context(context);
    excef_shutdown();
    free(compressed);
    return success ? 0 : 1;
}
