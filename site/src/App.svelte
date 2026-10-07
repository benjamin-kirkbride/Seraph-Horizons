<script lang="ts">
  import { onMount, tick } from "svelte";
  import { loadIconIndex, loadVersions, versionData } from "./lib/data.ts";
  import type { Meta, VersionsFile } from "./lib/format.ts";
  import { formatRoute, parseRoute, withVersion, type Route } from "./lib/route.ts";
  import { icons, startClock } from "./lib/state.svelte.ts";
  import { NEW_ISSUE_URL, t } from "./lib/strings.ts";
  import { THEMES, loadTheme, parseTheme, saveTheme } from "./lib/theme.ts";
  import Credits from "./components/Credits.svelte";
  import EntityList from "./components/EntityList.svelte";
  import EntityPage from "./components/EntityPage.svelte";
  import Home from "./components/Home.svelte";
  import ItemPage from "./components/ItemPage.svelte";
  import SearchResults from "./components/SearchResults.svelte";
  import TypePage from "./components/TypePage.svelte";
  import ValuesPage from "./components/ValuesPage.svelte";

  let route = $state<Route>(parseRoute(location.hash));
  let versions = $state<VersionsFile | null>(null);
  let versionsFailed = $state(false);
  let meta = $state<{ id: string; meta: Meta } | null>(null);
  let metaFailed = $state<string | null>(null);
  let flash = $state<string | null>(null);
  // Filled from the route by the effect below.
  let query = $state("");
  let main: HTMLElement | undefined = $state();
  let theme = $state(loadTheme());
  // The model viewer is loaded only when a reader opens it, with three.js after it.
  let modelsView: Promise<typeof import("./components/ModelsRoute.svelte")> | null = null;
  const loadModelsView = () => (modelsView ??= import("./components/ModelsRoute.svelte"));

  const versionId = $derived("version" in route ? (route.version ?? null) : null);
  const known = $derived(versions !== null && versionId !== null && versions.versions.some((v) => v.id === versionId));
  const data = $derived(known && versionId ? versionData(versionId) : null);
  // Pages without a version (the model viewer) search the default version.
  const searchVersion = $derived(
    known ? versionId : versions && versions.versions.some((v) => v.id === versions!.default) ? versions.default : (versions?.versions[0]?.id ?? null),
  );

  function go(next: Route, replace = false) {
    const hash = formatRoute(next);
    if (replace) history.replaceState(null, "", hash);
    else history.pushState(null, "", hash);
    route = next;
    flash = null;
  }

  onMount(() => {
    const onHash = () => {
      route = parseRoute(location.hash);
      flash = null;
    };
    addEventListener("hashchange", onHash);
    addEventListener("popstate", onHash);
    loadVersions().then(
      (v) => (versions = v),
      () => (versionsFailed = true),
    );
    loadIconIndex().then((index) => (icons.index = index));
    startClock();
    return () => {
      removeEventListener("hashchange", onHash);
      removeEventListener("popstate", onHash);
    };
  });

  // The bare address goes to the default version; the redirect replaces the history
  // entry so Back still leaves the site.
  $effect(() => {
    if (route.view === "root" && versions && versions.versions.length > 0) {
      const id = versions.versions.some((v) => v.id === versions!.default) ? versions.default : versions.versions[0]!.id;
      go({ view: "home", version: id }, true);
    }
  });

  $effect(() => {
    const d = data;
    if (!d) return;
    if (meta?.id === d.id) return;
    metaFailed = null;
    d.meta().then(
      (m) => (meta = { id: d.id, meta: m }),
      () => (metaFailed = d.id),
    );
    // Fetch the search index early; the first keystroke should not wait for it.
    d.searchFile().catch(() => {});
  });

  // Keep the box in step with the address when the reader goes back or follows a link.
  $effect(() => {
    if (route.view === "search") query = route.query;
  });

  // Move focus to the new page for keyboard and screen reader users, except while they
  // type in the search box.
  let lastKey = "";
  $effect(() => {
    // The values page's filters are in its address too; changing them is not a new page.
    const key = route.view === "search" ? "search" : route.view === "values" ? `values|${route.version}` : formatRoute(route);
    if (key === lastKey) return;
    const first = lastKey === "";
    lastKey = key;
    if (first || route.view === "search") return;
    tick().then(() => {
      scrollTo({ top: 0 });
      main?.focus({ preventScroll: true });
    });
  });

  $effect(() => {
    if (route.view !== "item" && route.view !== "type" && route.view !== "entity" && route.view !== "models" && route.view !== "model") document.title = t.siteTitle;
  });

  let debounce: ReturnType<typeof setTimeout> | undefined;
  function onSearchInput() {
    clearTimeout(debounce);
    debounce = setTimeout(submitSearch, 150);
  }
  function submitSearch(e?: Event) {
    e?.preventDefault();
    clearTimeout(debounce);
    if (!searchVersion) return;
    // A new query keeps the order the reader picked for the results.
    const sort = route.view === "search" ? route.sort : undefined;
    go({ view: "search", version: searchVersion, query, ...(sort ? { sort } : {}) }, route.view === "search");
  }

  function switchTheme(e: Event) {
    theme = parseTheme((e.currentTarget as HTMLSelectElement).value);
    saveTheme(theme);
  }

  async function switchVersion(e: Event) {
    const next = (e.currentTarget as HTMLSelectElement).value;
    if (route.view === "item") {
      const code = route.code;
      const target = versionData(next);
      try {
        await target.ready();
      } catch {
        go({ view: "home", version: next });
        return;
      }
      if (target.indexOf(code) >= 0) {
        go(withVersion(route, next));
        flash = t.keptItem(code);
      } else {
        go({ view: "home", version: next });
        flash = t.lostItem(code);
      }
      return;
    }
    go(withVersion(route, next));
  }
</script>

<a class="skip" href="#main" onclick={(e) => { e.preventDefault(); main?.focus(); }}>{t.skipToContent}</a>

<header class="top">
  <div class="bar">
    <a class="brand" href={versionId ? formatRoute({ view: "home", version: versionId }) : "#/"}>{t.siteTitle}</a>
    <form class="search" role="search" onsubmit={submitSearch}>
      <label for="q" class="visually-hidden">{t.searchLabel}</label>
      <input
        id="q"
        type="search"
        bind:value={query}
        oninput={onSearchInput}
        placeholder={t.searchPlaceholder}
        autocomplete="off"
        spellcheck="false"
        disabled={!searchVersion}
      />
      <button type="submit" disabled={!searchVersion}>{t.searchButton}</button>
    </form>
    {#if versionId && known}
      <a class="nav" href={formatRoute({ view: "entities", version: versionId })}>{t.entitiesLink}</a>
      {#if meta?.id === versionId && (meta.meta.valueCount ?? 0) > 0}
        <a class="nav" href={formatRoute({ view: "values", version: versionId })} aria-current={route.view === "values" ? "page" : undefined}>{t.valuesLink}</a>
      {/if}
    {/if}
    <a class="nav" href={formatRoute({ view: "models" })} aria-current={route.view === "models" || route.view === "model" ? "page" : undefined}>{t.modelsLink}</a>
    {#if versions && versions.versions.length > 0}
      <label class="picker">
        <span>{t.versionLabel}</span>
        <select value={known ? versionId : ""} onchange={switchVersion}>
          {#if !known}<option value="" disabled>–</option>{/if}
          {#each versions.versions as v (v.id)}
            <option value={v.id}>{v.label}</option>
          {/each}
        </select>
      </label>
    {/if}
    <label class="picker">
      <span>{t.themeLabel}</span>
      <select value={theme} onchange={switchTheme}>
        {#each THEMES as th (th)}
          <option value={th}>{t.themes[th]}</option>
        {/each}
      </select>
    </label>
  </div>
  <p class="notice" data-testid="unofficial-notice">{t.unofficial}</p>
</header>

<main id="main" tabindex="-1" bind:this={main}>
  {#if flash}<p class="flash" role="status">{flash}</p>{/if}
  {#if route.view === "models" || route.view === "model"}
    {#await loadModelsView()}
      <p class="muted">{t.loading}</p>
    {:then mod}
      <mod.default id={route.view === "model" ? route.id : null} />
    {:catch}
      <p role="alert">{t.loadFailed}</p>
    {/await}
  {:else if versionsFailed}
    <p role="alert">{t.loadFailed}</p>
  {:else if !versions}
    <p class="muted">{t.loading}</p>
  {:else if versions.versions.length === 0}
    <p>{t.noVersions}</p>
  {:else if route.view === "root"}
    <p class="muted">{t.loading}</p>
  {:else if route.view === "notfound" || !known || !data}
    <h1>{t.notFound}</h1>
    {#if versionId && !known}<p>{t.unknownVersion(versionId)}</p>{/if}
    <p><a href="#/">{t.backHome}</a></p>
  {:else if metaFailed === data.id}
    <p role="alert">{t.loadFailed}</p>
  {:else if !meta || meta.id !== data.id}
    <p class="muted">{t.loading}</p>
  {:else if route.view === "home"}
    <Home meta={meta.meta} version={data.id} />
  {:else if route.view === "search"}
    <SearchResults {data} meta={meta.meta} query={route.query} sort={route.sort} />
  {:else if route.view === "item"}
    {#key `${data.id}|${route.code}`}
      <ItemPage {data} meta={meta.meta} code={route.code} />
    {/key}
  {:else if route.view === "type"}
    {#key `${data.id}|${route.code}`}
      <TypePage {data} meta={meta.meta} code={route.code} page={route.page} />
    {/key}
  {:else if route.view === "entities"}
    <EntityList {data} meta={meta.meta} />
  {:else if route.view === "entity"}
    {#key `${data.id}|${route.code}`}
      <EntityPage {data} meta={meta.meta} code={route.code} variant={route.variant} />
    {/key}
  {:else if route.view === "credits"}
    <Credits meta={meta.meta} />
  {:else if route.view === "values"}
    <ValuesPage {data} meta={meta.meta} view={route} />
  {/if}
</main>

<footer>
  <p>{t.unofficial}</p>
  <p>
    {#if versionId && known}<a href={formatRoute({ view: "credits", version: versionId })}>{t.credits} &amp; {t.about.toLowerCase()}</a> ·{/if}
    <a href={NEW_ISSUE_URL} rel="noopener noreferrer" target="_blank">{t.removal}</a>
  </p>
</footer>

<style>
  .skip {
    position: absolute;
    left: -999px;
    top: 0;
    background: var(--surface);
    padding: 0.5rem;
    z-index: 10;
  }
  .skip:focus {
    left: 0.5rem;
  }
  .top {
    background: var(--surface);
    border-bottom: 1px solid var(--border);
  }
  .bar {
    max-width: 72rem;
    margin: 0 auto;
    padding: 0.6rem 1rem;
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.6rem 1rem;
  }
  .brand {
    font-weight: 700;
    color: var(--text);
    text-decoration: none;
    white-space: nowrap;
  }
  .nav {
    white-space: nowrap;
  }
  .search {
    flex: 1 1 18rem;
    display: flex;
    gap: 0.4rem;
    min-width: 0;
  }
  .search input {
    flex: 1;
    min-width: 0;
    padding: 0.35rem 0.6rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  .picker {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    font-size: 0.9rem;
  }
  .picker select {
    padding: 0.25rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  .notice {
    margin: 0;
    padding: 0.4rem 1rem;
    text-align: center;
    font-size: 0.9rem;
    background: var(--notice-bg);
    border-top: 1px solid var(--notice-border);
  }
  main {
    max-width: 72rem;
    margin: 0 auto;
    padding: 1rem;
    min-height: 60vh;
  }
  main:focus {
    outline: none;
  }
  .flash {
    padding: 0.4rem 0.6rem;
    background: var(--surface-2);
    border-radius: var(--radius);
  }
  footer {
    max-width: 72rem;
    margin: 2rem auto 0;
    padding: 1rem;
    border-top: 1px solid var(--border);
    font-size: 0.85rem;
    color: var(--muted);
  }
  footer p {
    margin: 0.3rem 0;
  }
</style>
