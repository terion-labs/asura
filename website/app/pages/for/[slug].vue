<script setup lang="ts">
import { findUseCase, useCases } from '~/content/use-cases'
import { siteUrl } from '~/content/facts'

const route = useRoute()
const base = useRuntimeConfig().app.baseURL
const useCase = findUseCase(String(route.params.slug))

if (!useCase) {
  throw createError({ statusCode: 404, statusMessage: 'Page not found', fatal: true })
}

const pageUrl = `${siteUrl}/for/${useCase.slug}`

useHead({ link: [{ rel: 'canonical', href: pageUrl }] })

useSeoMeta({
  title: `${useCase.title} — Asura`,
  description: useCase.description,
  ogTitle: useCase.title,
  ogDescription: useCase.description,
  ogType: 'website',
  ogUrl: pageUrl,
  ogSiteName: 'Asura',
  ogImage: `${siteUrl}/share.png`,
  twitterCard: 'summary_large_image',
  twitterTitle: useCase.title,
  twitterDescription: useCase.description,
})

const others = useCases.filter((c) => c.slug !== useCase.slug)
</script>

<template>
  <div>
    <SiteHeader />

    <main>
      <section class="section section--flush hero">
        <div class="wrap">
          <div class="hero__inner">
            <p class="eyebrow">{{ useCase.hero.eyebrow }}</p>
            <h1 class="hero__title">{{ useCase.hero.title }}</h1>
            <p class="hero__lede">{{ useCase.hero.lede }}</p>
            <div class="ctas">
              <a class="btn btn--primary" href="#download">
                Download for macOS
                <span class="btn__sub">Apple silicon</span>
              </a>
              <NuxtLink class="btn" to="/">Everything Asura does</NuxtLink>
            </div>
          </div>
          <div class="shot hero__shot">
            <img
              :src="`${base}shots/${useCase.hero.shot}`"
              :alt="useCase.hero.alt"
              width="1760"
              height="1100"
              fetchpriority="high"
            />
          </div>
        </div>
      </section>

      <SplitSection
        v-for="(s, i) in useCase.sections"
        :key="s.title"
        v-bind="s"
        :flip="i % 2 === 1"
        :alt_bg="i % 2 === 1"
      />

      <FaqList :faqs="useCase.faqs" />

      <section class="section section--alt">
        <div class="wrap">
          <p class="eyebrow">Also</p>
          <h2 data-reveal class="section-title">Other ways people use Asura</h2>
          <div class="grid others">
            <NuxtLink
              v-for="c in others"
              :key="c.slug"
              :to="`/for/${c.slug}`"
              class="card other"
              data-reveal
            >
              <h3>{{ c.label }}</h3>
              <p>{{ c.teaser }}</p>
            </NuxtLink>
          </div>
        </div>
      </section>

      <DownloadSection />
    </main>

    <SiteFooter />
  </div>
</template>

<style scoped>
.hero { padding-top: 88px; }

.hero__inner {
  max-width: 820px;
  margin-inline: auto;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.hero__title {
  font-size: clamp(36px, 5.4vw, 60px);
  letter-spacing: -0.03em;
}

.hero__lede {
  margin-top: 22px;
  font-size: clamp(16px, 2vw, 18.5px);
  color: var(--muted);
  max-width: 62ch;
}

.hero__shot { margin-top: 64px; }

.others {
  grid-template-columns: repeat(2, 1fr);
  margin-top: 44px;
}

@media (max-width: 720px) {
  .others { grid-template-columns: 1fr; }
}

.other { text-decoration: none; }
</style>
