<script setup lang="ts">
import { useCases } from '~/content/use-cases'

const base = useRuntimeConfig().app.baseURL
</script>

<template>
  <section id="use-cases" class="section section--alt">
    <div class="wrap">
      <p class="eyebrow">Who it is for</p>
      <h2 data-reveal class="section-title">Five ways people use Asura</h2>
      <div class="grid cases">
        <NuxtLink
          v-for="c in useCases"
          :key="c.slug"
          :to="`/for/${c.slug}`"
          class="card case"
          data-reveal
        >
          <h3>{{ c.label }}</h3>
          <p>{{ c.teaser }}</p>
          <span class="case__more">Read more</span>
        </NuxtLink>
      </div>
    </div>
  </section>
</template>

<style scoped>
.cases {
  grid-template-columns: repeat(3, 1fr);
  margin-top: 44px;
}

@media (max-width: 980px) {
  .cases { grid-template-columns: repeat(2, 1fr); }
}

@media (max-width: 640px) {
  .cases { grid-template-columns: 1fr; }
}

.case {
  text-decoration: none;
  display: flex;
  flex-direction: column;
}

.case p { flex: 1; }

.case__more {
  margin-top: 18px;
  font-size: 13.5px;
  font-weight: 500;
  color: var(--accent);
}
</style>
