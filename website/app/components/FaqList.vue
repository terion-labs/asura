<script setup lang="ts">
const props = defineProps<{
  faqs: { q: string; a: string }[]
  title?: string
}>()

// Only one answer is open at a time; the first one starts open.
const open = ref<number | null>(0)
</script>

<template>
  <section id="faq" class="section">
    <div class="wrap">
      <p class="eyebrow">Questions</p>
      <h2 data-reveal class="section-title">{{ props.title ?? 'FAQ' }}</h2>
      <div class="faq__list" data-reveal>
        <details
          v-for="(f, i) in props.faqs"
          :key="f.q"
          :open="open === i"
          class="faq__item"
          @toggle="(e: Event) => { if ((e.target as HTMLDetailsElement).open) open = i }"
        >
          <summary>{{ f.q }}</summary>
          <p>{{ f.a }}</p>
        </details>
      </div>
    </div>
  </section>
</template>
