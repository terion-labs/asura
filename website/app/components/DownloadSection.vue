<script setup lang="ts">
import {
  downloadUrl,
  linuxDownloads,
  linuxInstallUrl,
  platforms,
  releasesUrl,
  repoUrl,
} from '~/content/facts'

const base = useRuntimeConfig().app.baseURL
</script>

<template>
  <section id="download" class="section section--alt download">
    <div class="wrap download__inner">
      <img
        class="download__icon"
        :src="`${base}icon.png`"
        alt=""
        width="96"
        height="96"
        loading="lazy"
      />
      <h2 data-reveal class="section-title download__title">Get Asura</h2>
      <p data-reveal style="--rd: 1" class="section-lede download__lede">
        Free and open source under the MIT license. A signed and notarized
        build for {{ platforms.shipped }}, and an early build for
        {{ platforms.linux }}. {{ platforms.upcoming }}.
      </p>
      <div class="ctas download__ctas">
        <a class="btn btn--primary" :href="downloadUrl">
          Download for macOS
          <span class="btn__sub">.zip · Apple silicon</span>
        </a>
        <a
          v-for="build in linuxDownloads"
          :key="build.arch"
          class="btn"
          :href="build.url"
        >
          Download for Linux
          <span class="btn__sub">.tar.gz · {{ build.arch }}</span>
        </a>
        <a class="btn" :href="repoUrl">Source on GitHub</a>
      </div>
      <p class="download__checksum">
        Every archive has a SHA-256 checksum next to it on the
        <a :href="releasesUrl">release</a>. On macOS, updates arrive in-app:
        Settings, About, Check for Updates. The Linux archive is unsigned and
        has no updater; the <a :href="linuxInstallUrl">install steps</a>
        cover the libraries it needs and the browser sandbox.
      </p>
    </div>
  </section>
</template>

<style scoped>
.download__inner {
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.download__icon {
  width: 96px;
  height: 96px;
  border-radius: 22px;
  margin-bottom: 26px;
  filter: drop-shadow(0 18px 40px rgba(195, 117, 41, 0.28));
}

.download__title { max-width: none; }
.download__lede { text-align: center; }
.download__ctas { margin-top: 30px; }

.download__checksum {
  margin-top: 18px;
  font-size: 12.5px;
  color: var(--faint);
  max-width: 60ch;
}

.download__checksum a { color: var(--muted); }
</style>
