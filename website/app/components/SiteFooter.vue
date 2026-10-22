<script setup lang="ts">
import { releasesUrl, repoUrl } from '~/content/facts'
import { useCases } from '~/content/use-cases'

const base = useRuntimeConfig().app.baseURL
const year = new Date().getFullYear()
</script>

<template>
  <footer class="footer">
    <div class="wrap footer__row">
      <div class="footer__brand">
        <AsuraLogo class="footer__logo" />
        <div>
          <div class="footer__name"><b>Asura</b></div>
          <p class="footer__tag">Terminals, servers, and an agent in one window.</p>
        </div>
      </div>
      <nav class="footer__links" aria-label="Use cases">
        <span class="footer__heading">Use cases</span>
        <NuxtLink
          v-for="c in useCases"
          :key="c.slug"
          :to="`/for/${c.slug}`"
        >
          {{ c.label }}
        </NuxtLink>
      </nav>
      <nav class="footer__links" aria-label="Project">
        <span class="footer__heading">Project</span>
        <a :href="repoUrl">GitHub</a>
        <a :href="releasesUrl">Releases</a>
        <a :href="`${repoUrl}/issues`">Issues</a>
        <a :href="`${repoUrl}/blob/main/LICENSE`">MIT license</a>
      </nav>
    </div>
    <div class="wrap footer__meta">
      <span>© {{ year }} Terion</span>
      <span>Built with .NET, Avalonia, libghostty-vt, and Chromium.</span>
    </div>
  </footer>
</template>

<style scoped>
.footer {
  border-top: 1px solid var(--line-soft);
  padding: 56px 0 40px;
  background: var(--bg-alt);
}

.footer__row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 32px;
  flex-wrap: wrap;
}

.footer__brand { display: flex; gap: 12px; align-items: flex-start; max-width: 280px; }
.footer__logo { width: 30px; height: 24px; color: var(--accent); margin-top: 3px; flex-shrink: 0; }
.footer__name { font-size: 16px; }
.footer__name b { font-weight: 700; }
.footer__tag { color: var(--faint); font-size: 13.5px; margin-top: 2px; }

.footer__links {
  display: flex;
  flex-direction: column;
  gap: 10px;
  font-size: 13.5px;
}

.footer__heading {
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--faint);
  margin-bottom: 4px;
}

.footer__links a { color: var(--muted); text-decoration: none; }
.footer__links a:hover { color: var(--text); }

.footer__meta {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  margin-top: 40px;
  color: var(--faint);
  font-size: 12.5px;
}
</style>
