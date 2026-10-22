<script setup lang="ts">
import { repoUrl } from '~/content/facts'
import { useCases } from '~/content/use-cases'

const base = useRuntimeConfig().app.baseURL
</script>

<template>
  <header class="header">
    <div class="wrap header__row">
      <NuxtLink class="header__brand" to="/">
        <AsuraLogo class="header__logo" />
        <span><b>Asura</b></span>
      </NuxtLink>
      <nav class="header__nav" aria-label="Sections">
        <NuxtLink to="/#agent">Agent</NuxtLink>
        <NuxtLink to="/#panels">Panels</NuxtLink>
        <NuxtLink to="/#security">Security</NuxtLink>
        <div class="header__menu">
          <button type="button" class="header__menu-btn" aria-haspopup="true">
            Use cases
            <svg viewBox="0 0 10 6" width="10" height="6" aria-hidden="true">
              <path d="M1 1l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.5" />
            </svg>
          </button>
          <div class="header__menu-list">
            <NuxtLink
              v-for="c in useCases"
              :key="c.slug"
              :to="`/for/${c.slug}`"
            >
              {{ c.label }}
            </NuxtLink>
          </div>
        </div>
        <NuxtLink to="/#faq">FAQ</NuxtLink>
      </nav>
      <div class="header__actions">
        <a class="header__gh" :href="repoUrl" aria-label="Asura on GitHub">
          <svg viewBox="0 0 16 16" width="18" height="18" fill="currentColor" aria-hidden="true">
            <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/>
          </svg>
        </a>
        <a class="btn btn--primary header__cta" href="#download">Download</a>
      </div>
    </div>
  </header>
</template>

<style scoped>
.header {
  position: sticky;
  top: 0;
  z-index: 40;
  backdrop-filter: blur(14px);
  background: color-mix(in srgb, var(--bg) 78%, transparent);
  border-bottom: 1px solid var(--line-soft);
}

.header__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  height: 60px;
}

.header__brand {
  display: flex;
  align-items: center;
  gap: 10px;
  text-decoration: none;
  font-size: 16px;
  letter-spacing: 0.01em;
}

.header__brand b { font-weight: 700; }

.header__logo {
  width: 24px;
  height: 20px;
  color: var(--accent);
}

.header__nav {
  display: flex;
  align-items: center;
  gap: 26px;
  font-size: 13.5px;
}

.header__nav a,
.header__menu-btn {
  text-decoration: none;
  color: var(--muted);
  transition: color 0.15s ease;
}

.header__nav a:hover,
.header__menu-btn:hover { color: var(--text); }

.header__menu { position: relative; }

.header__menu-btn {
  font: inherit;
  background: none;
  border: 0;
  padding: 0;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.header__menu-list {
  position: absolute;
  top: calc(100% + 14px);
  left: -14px;
  min-width: 220px;
  display: none;
  flex-direction: column;
  padding: 8px;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 12px;
  box-shadow: 0 18px 50px -20px rgba(0, 0, 0, 0.9);
}

/* a transparent bridge so the pointer can travel from the button to the list */
.header__menu-list::before {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  top: -16px;
  height: 16px;
}

.header__menu:hover .header__menu-list,
.header__menu:focus-within .header__menu-list { display: flex; }

.header__menu-list a {
  padding: 8px 12px;
  border-radius: 8px;
  white-space: nowrap;
}

.header__menu-list a:hover { background: var(--surface-2); }

.header__actions {
  display: flex;
  align-items: center;
  gap: 14px;
}

.header__gh {
  color: var(--muted);
  display: inline-flex;
  transition: color 0.15s ease;
}

.header__gh:hover { color: var(--text); }

.header__cta { padding: 8px 15px; font-size: 13.5px; }

@media (max-width: 760px) {
  .header__nav { display: none; }
}
</style>
