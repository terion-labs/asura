#!/usr/bin/env bash
# Source this before invoking macOS build tools. GUI Git clients may inherit a
# Command Line Tools SDK even when Swift Testing requires full Xcode.
if [[ "$(uname -s)" == Darwin ]]; then
    if [[ -z "${DEVELOPER_DIR:-}" && -d /Applications/Xcode.app/Contents/Developer ]]; then
        export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
    fi
    # An explicit SDK lookup uses the selected developer tree, not SDKROOT.
    # Keep assignment separate from export so lookup failure stops the caller.
    SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"
    export SDKROOT
fi
