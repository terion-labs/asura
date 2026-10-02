// Exercise the packaged Metal bridge with real drawables, including frames
// rendered before AppKit delivers a main-thread paint for the new window size.
#define COM_GUIDS_MATERIALIZE
#include "comimpl.h"
#include "avalonia-native.h"
#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#include "rendertarget.h"
#include <cstdio>
#include <cstdlib>
#include <thread>

extern IAvnMetalDisplay* GetMetalDisplay();

static void require(bool condition, const char* message)
{
    if (!condition) { std::fprintf(stderr, "%s\n", message); std::exit(1); }
}

static void draw(IAvnMetalRenderTarget* target, AvnPixelSize expected, double scaling)
{
    @autoreleasepool
    {
        ComPtr<IAvnMetalRenderingSession> session;
        require(target->BeginDrawing(&session) == S_OK && session,
            "Metal did not provide a rendering session");
        AvnPixelSize size;
        require(session->GetPixelSize(&size) == S_OK, "Cannot read Metal session dimensions");
        id<MTLTexture> texture = (__bridge id<MTLTexture>)session->GetTexture();
        if (size.Width != expected.Width || size.Height != expected.Height
            || texture.width != static_cast<NSUInteger>(expected.Width)
            || texture.height != static_cast<NSUInteger>(expected.Height)
            || session->GetScaling() != scaling)
        {
            std::fprintf(stderr,
                "Stale Metal frame (main=%d): session=%dx%d texture=%lux%lu scale=%.1f; expected=%dx%d scale=%.1f\n",
                [NSThread isMainThread], size.Width, size.Height,
                texture.width, texture.height, session->GetScaling(),
                expected.Width, expected.Height, scaling);
            std::exit(1);
        }
    }
}

int main()
{
    @autoreleasepool
    {
        // Hosted macOS VMs can build the native library without exposing a
        // Metal device. Only that hardware absence skips the GPU regression;
        // bridge/device creation and drawing failures on a capable Mac fail.
        if (MTLCreateSystemDefaultDevice() == nil)
        {
            std::puts("SKIP macOS Metal resize: this host exposes no Metal device");
            return 0;
        }
        ComPtr<IAvnMetalDevice> device;
        require(GetMetalDisplay()->CreateDevice(&device) == S_OK && device,
            "A Metal device is required for the native resize regression");
        MetalRenderTarget* target = [[MetalRenderTarget alloc] initWithDevice:device];
        CAMetalLayer* layer = (CAMetalLayer*)target.layer;
        layer.frame = CGRectMake(0, 0, 800, 600);
        ComPtr<IAvnMetalRenderTarget> rendering;
        [target getRenderTarget:&rendering];

        [target resize:{800, 600} withScale:1];
        draw(rendering, {800, 600}, 1);
        require(!layer.presentsWithTransaction, "Main-thread frame left synchronous presentation enabled");

        // Magnet-style abrupt resize: the next frame is on the compositor,
        // with no intervening main-thread BeginDrawing to apply the size.
        [target resize:{1200, 450} withScale:1];
        std::thread firstFrame([&] { draw(rendering, {1200, 450}, 1); });
        firstFrame.join();

        // Includes a backing-scale change without a pixel-size change, rapid
        // coalesced resizes, a minimum-size surface, and both drawing threads.
        const AvnPixelSize sizes[] = {{1200, 450}, {1200, 450}, {1, 1}, {640, 960}};
        for (int iteration = 0; iteration < 64; ++iteration)
        {
            auto size = sizes[iteration % 4];
            double scaling = iteration % 2 == 0 ? 2 : 1;
            [target resize:{320, 240} withScale:1];
            [target resize:size withScale:scaling];
            std::thread frame([&] { draw(rendering, size, scaling); });
            frame.join();
            require(!layer.presentsWithTransaction, "Background frame enabled synchronous presentation");
            draw(rendering, size, scaling);
            require(!layer.presentsWithTransaction, "Main-thread frame did not restore asynchronous presentation");
        }
    }
    std::puts("PASS macOS Metal: abrupt/coalesced resizes and backing-scale changes update real textures on both rendering threads");
}
