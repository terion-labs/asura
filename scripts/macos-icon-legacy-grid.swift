// Places a full-bleed macOS 26 icon rendition on the pre-26 icon grid.
//
// Icon Composer exports the Liquid Glass design, whose body fills the whole
// canvas. macOS 13 through 15 draw application icons on the older grid: an
// 824-point body centred on a 1024-point canvas with a soft drop shadow below
// it. An unconverted rendition looks oversized next to every other icon in the
// Dock and in Finder there, so the fallback .icns is built from this output.
//
// Usage: swift macos-icon-legacy-grid.swift <input-1024.png> <output-1024.png>

import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

let canvasSize = 1024
let bodySize: CGFloat = 824
let shadowOffsetY: CGFloat = -12
let shadowBlur: CGFloat = 24
let shadowAlpha: CGFloat = 0.3

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(1)
}

let arguments = CommandLine.arguments
guard arguments.count == 3 else {
    fail("Usage: macos-icon-legacy-grid.swift <input-1024.png> <output-1024.png>")
}

let inputURL = URL(fileURLWithPath: arguments[1])
let outputURL = URL(fileURLWithPath: arguments[2])

guard let source = CGImageSourceCreateWithURL(inputURL as CFURL, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil)
else {
    fail("Could not read \(inputURL.path).")
}

guard image.width == canvasSize, image.height == canvasSize else {
    fail("Expected a \(canvasSize)x\(canvasSize) rendition, got \(image.width)x\(image.height).")
}

let colorSpace = image.colorSpace ?? CGColorSpace(name: CGColorSpace.displayP3)!
guard let context = CGContext(
    data: nil,
    width: canvasSize,
    height: canvasSize,
    bitsPerComponent: 8,
    bytesPerRow: 0,
    space: colorSpace,
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
else {
    fail("Could not create the drawing context.")
}

let inset = (CGFloat(canvasSize) - bodySize) / 2
context.clear(CGRect(x: 0, y: 0, width: canvasSize, height: canvasSize))
context.interpolationQuality = .high
context.setShadow(
    offset: CGSize(width: 0, height: shadowOffsetY),
    blur: shadowBlur,
    color: CGColor(gray: 0, alpha: shadowAlpha))
context.draw(image, in: CGRect(x: inset, y: inset, width: bodySize, height: bodySize))

guard let result = context.makeImage(),
      let destination = CGImageDestinationCreateWithURL(
        outputURL as CFURL,
        UTType.png.identifier as CFString,
        1,
        nil)
else {
    fail("Could not create \(outputURL.path).")
}

CGImageDestinationAddImage(destination, result, nil)
guard CGImageDestinationFinalize(destination) else {
    fail("Could not write \(outputURL.path).")
}
