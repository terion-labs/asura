<script setup lang="ts">
// One argument with one screenshot: text on one side, the window on the
// other. `flip` puts the shot first on wide screens; narrow screens always
// show the text first.
const props = defineProps<{
  eyebrow: string
  title: string
  lede: string
  points?: string[]
  shot: string
  alt: string
  flip?: boolean
  alt_bg?: boolean
  id?: string
}>()

const base = useRuntimeConfig().app.baseURL
</script>

<template>
  <section
    :id="props.id"
    :class="['section', { 'section--alt': props.alt_bg }]"
  >
    <div :class="['wrap', 'split', { 'split--flip': props.flip }]">
      <div class="split__text" data-reveal>
        <p class="eyebrow">{{ props.eyebrow }}</p>
        <h2 class="section-title">{{ props.title }}</h2>
        <p class="section-lede">{{ props.lede }}</p>
        <ul v-if="props.points?.length" class="checks">
          <li v-for="p in props.points" :key="p">{{ p }}</li>
        </ul>
        <slot />
      </div>
      <div class="shot split__shot" data-reveal style="--rd: 1">
        <img
          :src="`${base}shots/${props.shot}`"
          :alt="props.alt"
          width="1760"
          height="1100"
          loading="lazy"
        />
      </div>
    </div>
  </section>
</template>
