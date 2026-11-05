<script setup lang="ts">
// A split button: the primary half downloads the build that fits the
// visitor's machine, the chevron opens a menu with every build. When no
// build fits, the whole button opens the menu.
import { builds, platforms } from '~/content/facts'

const { current } = useDetectedBuild()

const root = ref<HTMLElement | null>(null)
const menu = ref<HTMLElement | null>(null)
const open = ref(false)
const menuId = useId()

function close() {
  open.value = false
}

function toggle() {
  open.value = !open.value
  if (open.value) {
    nextTick(() => focusItem(0))
  }
}

function items(): HTMLElement[] {
  return Array.from(menu.value?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])
}

function focusItem(index: number) {
  const list = items()
  if (list.length === 0) return
  list[((index % list.length) + list.length) % list.length]?.focus()
}

function onMenuKeydown(event: KeyboardEvent) {
  const list = items()
  const at = list.indexOf(document.activeElement as HTMLElement)
  switch (event.key) {
    case 'ArrowDown':
      event.preventDefault()
      focusItem(at + 1)
      break
    case 'ArrowUp':
      event.preventDefault()
      focusItem(at - 1)
      break
    case 'Home':
      event.preventDefault()
      focusItem(0)
      break
    case 'End':
      event.preventDefault()
      focusItem(list.length - 1)
      break
    case 'Escape':
      event.preventDefault()
      close()
      root.value?.querySelector<HTMLElement>('.dl__toggle, .dl__choose')?.focus()
      break
    case 'Tab':
      close()
      break
  }
}

function onDocumentPointer(event: PointerEvent) {
  if (open.value && root.value && !root.value.contains(event.target as Node)) {
    close()
  }
}

onMounted(() => document.addEventListener('pointerdown', onDocumentPointer))
onBeforeUnmount(() => document.removeEventListener('pointerdown', onDocumentPointer))
</script>

<template>
  <div ref="root" class="dl" :class="{ 'dl--open': open }">
    <template v-if="current">
      <a class="btn btn--primary dl__main" :href="current.url">
        Download for {{ current.os }}
        <span class="btn__sub">{{ current.file }} · {{ current.arch }}</span>
      </a>
      <button
        type="button"
        class="btn btn--primary dl__toggle"
        aria-haspopup="menu"
        :aria-expanded="open"
        :aria-controls="menuId"
        aria-label="Other downloads"
        @click="toggle"
      >
        <svg width="14" height="14" viewBox="0 0 16 16" aria-hidden="true">
          <path d="M3 6l5 5 5-5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </button>
    </template>
    <button
      v-else
      type="button"
      class="btn btn--primary dl__choose"
      aria-haspopup="menu"
      :aria-expanded="open"
      :aria-controls="menuId"
      @click="toggle"
    >
      Download
      <span class="btn__sub">macOS · Linux</span>
      <svg width="14" height="14" viewBox="0 0 16 16" aria-hidden="true">
        <path d="M3 6l5 5 5-5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    </button>

    <ul
      v-show="open"
      :id="menuId"
      ref="menu"
      class="dl__menu"
      role="menu"
      aria-label="Downloads"
      @keydown="onMenuKeydown"
    >
      <li v-for="build in builds" :key="build.id" role="none">
        <a
          class="dl__item"
          role="menuitem"
          :href="build.url"
          :aria-current="current?.id === build.id ? 'true' : undefined"
          @click="close"
        >
          <span class="dl__os">{{ build.os }} <span class="dl__fits">{{ build.fits }}</span></span>
          <span class="dl__meta">{{ build.arch }} · {{ build.file }}</span>
        </a>
      </li>
      <li class="dl__note" role="none">{{ platforms.upcoming }}.</li>
    </ul>
  </div>
</template>

<style scoped>
.dl {
  position: relative;
  display: inline-flex;
  align-items: stretch;
}

.dl__main {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
}

.dl__toggle {
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
  border-left: 1px solid rgba(22, 18, 12, 0.28);
  padding: 12px 13px;
  cursor: pointer;
  font: inherit;
}

.dl__choose {
  cursor: pointer;
  font: inherit;
}

.dl__toggle svg,
.dl__choose svg {
  transition: transform 0.15s ease;
}

.dl--open .dl__toggle svg,
.dl--open .dl__choose svg {
  transform: rotate(180deg);
}

.dl__menu {
  position: absolute;
  top: calc(100% + 8px);
  left: 0;
  z-index: 20;
  min-width: 100%;
  margin: 0;
  padding: 6px;
  list-style: none;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 12px;
  box-shadow: 0 18px 48px rgba(0, 0, 0, 0.45);
  text-align: left;
}

.dl__item {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 18px;
  padding: 10px 12px;
  border-radius: 8px;
  color: var(--text);
  text-decoration: none;
  font-size: 14px;
  font-weight: 500;
  white-space: nowrap;
}

.dl__item:hover,
.dl__item:focus-visible {
  background: var(--surface-2);
  outline: none;
}

.dl__item[aria-current='true'] .dl__os::before {
  content: '';
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-right: 9px;
  vertical-align: middle;
  border-radius: 50%;
  background: var(--accent);
}

.dl__fits {
  margin-left: 6px;
  font-weight: 400;
  color: var(--muted);
}

.dl__meta {
  font-family: var(--mono);
  font-size: 11.5px;
  color: var(--muted);
}

.dl__note {
  padding: 8px 12px 6px;
  font-size: 12px;
  color: var(--faint);
  white-space: nowrap;
}

@media (max-width: 520px) {
  .dl {
    width: 100%;
  }

  .dl__main,
  .dl__choose {
    flex: 1;
    flex-direction: column;
    align-items: flex-start;
    gap: 3px;
    white-space: nowrap;
  }

  .dl__choose {
    flex-direction: row;
    align-items: center;
    justify-content: center;
    gap: 9px;
  }

  .dl__menu {
    right: 0;
  }
}
</style>
