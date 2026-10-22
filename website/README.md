# Asura website

The marketing site for Asura, served from GitHub Pages.

Nuxt with a static build target. No server; `pnpm generate` writes plain
files to `.output/public`.

## Structure

- `/` sells the whole product: hero, use-case cards, the four differentiators,
  the agent, the panel tour, session continuity, workspaces, isolation,
  networking, security, FAQ, download.
- `/for/<slug>` is one landing per kind of reader (servers over SSH, the AI
  agent, Kubernetes, client work, full-stack development). Each is one
  argument: hero, three or four split sections with a screenshot, its own
  FAQ, links to the other landings, download.

Copy lives in `app/content/`: `use-cases.ts` holds every landing, and
`facts.ts` holds the lists both the home page and the landings print
(providers, databases, file providers, network routes, platforms). Each list
names the registry in `src/` it mirrors; update the list when the registry
changes, not the prose. Claims on the site are checked against the code; the
writing follows `docs/ui-writing-guide.md` where it applies.

## Develop

```sh
pnpm install
pnpm dev
```

## Build

```sh
pnpm generate
```

The site is built for `https://asura.sh/`. `NUXT_APP_BASE_URL` can override
the root path for previews. `public/CNAME` declares the production domain.
The repository owner manages GitHub Pages custom-domain and DNS settings.

## Deploy

`.github/workflows/website.yml` builds and deploys on every push to `main`
that touches `website/`. It is separate from the application workflows: the
repository gate runs on version tags and the database-viewer workflow is
path-filtered to application sources, so website changes trigger neither.

## Screenshots

Images in `public/shots/` are WebP copies of the website export of the design
QA harness (`artifacts/design-qa/website/`), downscaled to 1760px wide. One
command regenerates all of them, or the routes named as arguments:

```sh
./scripts/export-website-shots.sh
./scripts/export-website-shots.sh workspace-agent settings-networking
```

It needs `cwebp` (`brew install webp`) and SF Pro Text on the Mac; see
`tools/Asura.DesignQa/README.md`, "Website assets", for where the font is
looked up. Add a route to the script's list when a page starts using it.
