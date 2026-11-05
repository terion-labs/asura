// Which release download fits the visitor's machine. The prerendered page
// shows the macOS build; after hydration the button switches to the
// visitor's platform, or to a generic "choose" state where no build fits
// (Windows, phones, ChromeOS). A browser cannot tell the distribution, so
// Linux gets the .deb for its CPU and the menu offers the .rpm and archive.
import { builds, defaultBuild, type Build, type BuildId } from '~/content/facts'

export type DetectedBuild = { state: 'pending' } | { state: 'known'; build: Build } | { state: 'none' }

interface ClientHints {
  platform?: string
  architecture?: string
  /** navigator.maxTouchPoints: an iPad asking for desktop pages says it is a Mac. */
  touchPoints?: number
}

/** Pure so it can be reasoned about and tested without a browser. */
export function detectBuildId(userAgent: string, platform: string, hints: ClientHints = {}): BuildId | null {
  const ua = userAgent
  const plat = (hints.platform || platform || '').toLowerCase()

  if (/android|iphone|ipad|ipod|cros/i.test(ua) || /android|ios|chrome os/.test(plat)) {
    return null
  }
  if (plat.startsWith('mac') || /macintosh|mac os x/i.test(ua)) {
    return (hints.touchPoints ?? 0) > 1 ? null : 'macos-arm64'
  }
  if (plat.startsWith('linux') || /\b(x11|linux)\b/i.test(ua)) {
    // Chromium freezes its Linux user agent to "x86_64" whatever the CPU;
    // the client hint carries the real architecture when it is available.
    if (hints.architecture) {
      return hints.architecture.toLowerCase() === 'arm' ? 'linux-arm64-deb' : 'linux-x64-deb'
    }
    return /aarch64|arm64|armv8/i.test(ua) ? 'linux-arm64-deb' : 'linux-x64-deb'
  }
  return null
}

interface UserAgentData {
  platform?: string
  getHighEntropyValues?(hints: string[]): Promise<{ architecture?: string; platform?: string }>
}

export function useDetectedBuild() {
  const detected = ref<DetectedBuild>({ state: 'pending' })

  onMounted(async () => {
    const nav = navigator as Navigator & { userAgentData?: UserAgentData }
    const hints: ClientHints = {
      platform: nav.userAgentData?.platform,
      touchPoints: nav.maxTouchPoints,
    }
    try {
      const values = await nav.userAgentData?.getHighEntropyValues?.(['architecture', 'platform'])
      if (values) {
        hints.architecture = values.architecture
        hints.platform = values.platform ?? hints.platform
      }
    } catch {
      // The browser declined the hint; the user agent string decides.
    }
    const id = detectBuildId(nav.userAgent, nav.platform ?? '', hints)
    const build = id ? builds.find((candidate) => candidate.id === id) : undefined
    detected.value = build ? { state: 'known', build } : { state: 'none' }
  })

  /** The build the primary button offers, or null when none fits. */
  const current = computed<Build | null>(() => {
    switch (detected.value.state) {
      case 'pending':
        return defaultBuild
      case 'known':
        return detected.value.build
      case 'none':
        return null
    }
  })

  return { detected, current }
}
