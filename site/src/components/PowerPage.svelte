<script lang="ts">
  // #/<version>/power: what every power source gives and every machine takes, from the
  // export's power section (power.json). The export carries only parameters; every figure
  // here is worked out by src/lib/power.ts. docs/recipe-browser/site.md describes the page.
  import type { Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import type { Consumer, PowerData, Producer, WindModel, WindPattern } from "../lib/power-data.ts";
  import {
    consumerLoad,
    equilibriumSpeed,
    fmt,
    FULL_WIND,
    isWind,
    meanWind,
    patternDuration,
    pct,
    producerFigures,
    referenceLoad,
    shareAtLeast,
    stalls,
    totalLoad,
    windAverages,
    windDistribution,
    windHistogram,
    windUnderLoad,
    withSails,
    type LoadPick,
  } from "../lib/power.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import ItemLink from "./ItemLink.svelte";
  import ModLink from "./ModLink.svelte";
  import PowerBars, { type Bar, type BarGroup } from "./PowerBars.svelte";
  import PowerSources from "./PowerSources.svelte";
  import PowerTorqueChart, { type Curve } from "./PowerTorqueChart.svelte";
  import PowerWindHistogram from "./PowerWindHistogram.svelte";

  let { data, meta }: { data: VersionData; meta: Meta } = $props();

  type Family = Producer["family"];
  const FAMILIES: readonly Family[] = ["water", "steam", "wind", "muscle"];
  // Fixed per family, in the palette's order, so a hidden family never repaints the others.
  const COLOR: Record<Family, string> = { water: "var(--series-1)", steam: "var(--series-2)", wind: "var(--series-3)", muscle: "var(--series-4)" };

  let power = $state.raw<PowerData | null>(null);
  let loaded = $state(false);
  let failed = $state(false);

  $effect(() => {
    // meta.json says whether the export had a power section; an older one has no power.json.
    if (!meta.power) {
      power = null;
      loaded = true;
      return;
    }
    data.power().then(
      (p) => {
        power = p;
        loaded = true;
      },
      () => (failed = true),
    );
  });

  // Controls.
  let shown = $state<Record<Family, boolean>>({ water: true, steam: true, wind: true, muscle: true });
  let wind = $state(FULL_WIND);
  let sails = $state<number | "listed">("listed");
  let height = $state(0);
  let picks = $state<LoadPick[]>([]);
  let dry = $state(false);

  const producers = $derived(power?.producers ?? []);
  const consumers = $derived(power?.consumers ?? []);
  const families = $derived(FAMILIES.filter((f) => producers.some((p) => p.family === f)));
  const maxSails = $derived(Math.max(0, ...producers.map((p) => (isWind(p) ? (p.model.sails?.max ?? 0) : 0))));
  const reference = $derived(referenceLoad(consumers));
  const machines = $derived(consumers.filter((c) => c.category === "machine"));
  const others = $derived(consumers.filter((c) => c.category !== "machine"));
  // The load explorer's machine picker, grouped under each mod's name.
  const machinesByMod = $derived.by(() => {
    const groups = new Map<string, Consumer[]>();
    for (const c of machines) groups.set(c.mod, [...(groups.get(c.mod) ?? []), c]);
    return [...groups].sort(([a], [b]) => modName(a).localeCompare(modName(b), "en"));
  });
  const hasOil = $derived(consumers.some((c) => (c.oil?.dryMultiplier ?? 1) !== 1));
  const dryMultiplier = $derived(Math.max(1, ...consumers.map((c) => c.oil?.dryMultiplier ?? 1)));

  const sailed = (p: Producer) => (p.model.kind === "wind" && sails !== "listed" ? withSails(p.model, sails) : p.model);
  // Mods ticked off; the chart's axes fit whatever is left.
  let hiddenMods = $state<string[]>([]);
  const producerMods = $derived([...new Set(producers.map((p) => p.mod))]);
  const modName = (id: string) => meta.mods[id]?.name || id;
  // A chart row's link to the item's page, where there is an item.
  const link = (item: string | null) => (item ? { href: formatRoute({ view: "item", version: data.id, code: item }) } : {});
  const visible = $derived(producers.filter((p) => shown[p.family] && !hiddenMods.includes(p.mod)));
  const curves = $derived<Curve[]>(visible.map((p) => ({ id: p.id, name: p.name, mod: modName(p.mod), color: COLOR[p.family], model: sailed(p), wind })));
  const peakBars = $derived<BarGroup[]>([
    {
      bars: visible
        .map((p) => ({ p, peak: producerFigures(sailed(p), wind).peak }))
        .sort((a, b) => b.peak - a.peak)
        .map(({ p, peak }): Bar => ({ key: p.id, label: p.name, ...link(p.item), mod: modName(p.mod), value: peak, color: COLOR[p.family], text: fmt(peak) })),
    },
  ]);
  const windText = $derived(wind === FULL_WIND ? `${fmt(wind)} (${t.power.windFull})` : fmt(wind));

  // Wind.
  const dist = $derived(power?.wind ? windDistribution(power.wind, height) : []);
  const bins = $derived(power?.wind ? windHistogram(power.wind, height) : []);
  const windmills = $derived(producers.filter(isWind));
  const heightText = $derived(t.power.blocks(height));

  // Load explorer.
  const load = $derived(totalLoad(consumers, picks, dry));
  const byId = $derived(new Map(consumers.map((c) => [c.id, c])));
  const results = $derived(
    producers.map((p) => {
      const full = { speed: equilibriumSpeed(p.model, load, FULL_WIND), stalled: stalls(p.model, load, FULL_WIND) };
      const avg = isWind(p) && dist.length > 0 ? windUnderLoad(p.model, load, dist) : null;
      return { p, full, avg };
    }),
  );
  const explorerBars = $derived<BarGroup[]>([
    {
      bars: results.flatMap(({ p, full, avg }): Bar[] => {
        const bars: Bar[] = [];
        if (avg)
          bars.push({
            key: `${p.id}|avg`,
            label: p.name,
            ...link(p.item),
            mod: modName(p.mod),
            sub: t.power.averaged,
            value: avg.speed,
            color: COLOR[p.family],
            text: avg.stalled > 0 ? `${fmt(avg.speed)} · ${t.power.stalledShare(pct(avg.stalled))}` : fmt(avg.speed),
            ...(avg.stalled >= 1 - 1e-9 ? { status: t.power.stalls } : {}),
          });
        bars.push({
          key: `${p.id}|full`,
          label: p.name,
          ...link(p.item),
          mod: modName(p.mod),
          ...(avg ? { sub: t.power.atFull, light: true } : {}),
          value: full.speed,
          color: COLOR[p.family],
          text: fmt(full.speed),
          ...(full.stalled ? { status: t.power.stalls } : {}),
        });
        return bars;
      }),
    },
  ]);

  function setPreset(id: string | undefined, count: number) {
    if (id) picks = [{ id, count }];
  }
  const quern = $derived(machines.find((c) => /(^|:)quern/.test(c.id)));
  function addRow() {
    const used = new Set(picks.map((p) => p.id));
    const next = machines.find((c) => !used.has(c.id)) ?? machines[0];
    if (next) picks = [...picks, { id: next.id, count: 1 }];
  }

  // Consumers.
  const consumerGroups = $derived.by<BarGroup[]>(() => {
    const groups = new Map<string, Consumer[]>();
    for (const c of machines) groups.set(c.mod, [...(groups.get(c.mod) ?? []), c]);
    return [...groups]
      .map(([mod, list]) => ({
        name: modName(mod),
        bars: list
          .sort((a, b) => consumerLoad(b, false) - consumerLoad(a, false))
          .map(
            (c): Bar => ({
              key: c.id,
              label: c.name,
              ...link(c.item),
              value: c.load,
              ...(c.loadMax !== undefined ? { range: c.loadMax } : {}),
              ...(c.oil ? { dry: consumerLoad(c, true) } : {}),
              color: "var(--series-1)",
              text: loadText(c),
              ...(c.oil ? { spoken: `${loadText(c)}, ${t.power.legendDry.toLowerCase()} ${dryText(c)}` } : {}),
            }),
          ),
      }))
      .sort((a, b) => a.name.localeCompare(b.name, "en"));
  });
  const loadText = (c: Consumer) => (c.loadMax !== undefined ? t.power.range(fmt(c.load), fmt(c.loadMax)) : fmt(c.load));
  const dryText = (c: Consumer) =>
    c.oil ? (c.loadMax !== undefined ? t.power.range(fmt(consumerLoad(c, true, "low")), fmt(consumerLoad(c, true))) : fmt(consumerLoad(c, true))) : "";
  const consumerLegend = $derived([
    { label: t.power.legendLoad, color: "var(--series-1)" },
    ...(machines.some((c) => c.loadMax !== undefined) ? [{ label: t.power.legendRange, color: "var(--series-1)", kind: "light" as const }] : []),
    ...(machines.some((c) => c.oil) ? [{ label: t.power.legendDry, color: "var(--series-1)", kind: "hatch" as const }] : []),
  ]);

  const fellBack = (sources: { fallback?: boolean }[]) => sources.some((s) => s.fallback);
  const durationText = (p: WindPattern) => {
    const d = patternDuration(p);
    return t.power.duration(fmt(d.mean), fmt(d.min), fmt(d.max));
  };
</script>

{#snippet name(item: string | null, label: string, sources: { fallback?: boolean }[])}
  {#if item}<ItemLink code={item} {data} {label} />{:else}{label}{/if}
  {#if fellBack(sources)}<span class="fallback" title={t.power.fallbackHint}>{t.power.fallback}</span>{/if}
{/snippet}

<h1>{t.power.heading}</h1>

{#if failed}
  <p role="alert">{t.loadFailed}</p>
{:else if !loaded}
  <p class="muted">{t.loading}</p>
{:else if !power}
  <p data-testid="power-none">{t.power.none}</p>
{:else}
  <p>{t.power.intro}</p>
  <nav class="toc" aria-label="Sections">
    {#each Object.entries(t.power.sections) as [key, label] (key)}
      <a href="#power-{key}" onclick={(e) => { e.preventDefault(); document.getElementById(`power-${key}`)?.scrollIntoView(); }}>{label}</a>
    {/each}
  </nav>

  <section aria-labelledby="power-overview" data-testid="power-overview">
    <h2 id="power-overview">{t.power.sections.overview}</h2>
    <p>{t.power.units}</p>
    <p data-testid="reference-load">{reference ? t.power.reference(reference.name, fmt(reference.load)) : t.power.referenceNone}</p>
  </section>

  <section aria-labelledby="power-producers" data-testid="power-producers">
    <h2 id="power-producers">{t.power.sections.producers}</h2>
    <p class="muted note">{t.power.producersNote}</p>
    <div class="scroll">
      <table class="wide-table" data-testid="producers">
        <thead>
          <tr>
            <th scope="col">{t.power.cols.name}</th>
            <th scope="col">{t.power.cols.mod}</th>
            <th scope="col">{t.power.cols.family}</th>
            <th scope="col" class="num">{t.power.cols.free}</th>
            <th scope="col" class="num">{t.power.cols.stall}</th>
            <th scope="col" class="num">{t.power.cols.peak}</th>
            <th scope="col" class="num">{t.power.cols.kn}</th>
            <th scope="col">{t.power.cols.conditions}</th>
            <th scope="col">{t.power.cols.cost}</th>
          </tr>
        </thead>
        <tbody>
          {#each producers as p (p.id)}
            {@const f = producerFigures(p.model)}
            <tr data-producer={p.id}>
              <td>{@render name(p.item, p.name, p.sources)}</td>
              <td><ModLink id={p.mod} mods={meta.mods} /></td>
              <td class="nowrap"><span class="key" style:background={COLOR[p.family]}></span>{t.power.families[p.family]}</td>
              <td class="num" data-col="free">{fmt(f.free)}</td>
              <td class="num" data-col="stall">{fmt(f.stall)}</td>
              <td class="num" data-col="peak">{fmt(f.peak)}</td>
              <td class="num">{p.shownKN === null ? "–" : fmt(p.shownKN)}</td>
              <td class="text">{p.conditions}</td>
              <td>{p.cost ?? "–"}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>

    <div class="controls">
      <fieldset class="families">
        <legend>{t.power.showFamilies}</legend>
        {#each families as f (f)}
          <label><input type="checkbox" bind:checked={shown[f]} /><span class="key" style:background={COLOR[f]}></span>{t.power.families[f]}</label>
        {/each}
      </fieldset>
      {#if producerMods.length > 1}
        <fieldset class="families" data-testid="mod-toggles">
          <legend>{t.power.showMods}</legend>
          {#each producerMods as m (m)}
            <label
              ><input
                type="checkbox"
                checked={!hiddenMods.includes(m)}
                onchange={(e) => (hiddenMods = e.currentTarget.checked ? hiddenMods.filter((x) => x !== m) : [...hiddenMods, m])}
              />{modName(m)}</label
            >
          {/each}
        </fieldset>
      {/if}
      {#if windmills.length > 0}
        <label class="slider">
          <span>{t.power.windControl} <output>{windText}</output></span>
          <input type="range" min="0" max="1.5" step="0.05" bind:value={wind} />
        </label>
      {/if}
      {#if maxSails > 0}
        <label class="select">
          <span>{t.power.sailsControl}</span>
          <select bind:value={sails}>
            <option value="listed">{t.power.sailsAsListed}</option>
            {#each Array.from({ length: maxSails }, (_, i) => maxSails - i) as n (n)}<option value={n}>{n}</option>{/each}
          </select>
        </label>
      {/if}
    </div>
    <PowerTorqueChart {curves} title={t.power.torqueTitle} desc={t.power.torqueDesc(windText)} />
    <PowerBars title={t.power.peakTitle} desc={t.power.peakDesc} axis={t.power.powerAxis} groups={peakBars} testid="peak-chart" />
    <PowerSources entries={producers.map((p) => ({ name: p.name, ...link(p.item), mod: modName(p.mod), sources: p.sources }))} />
  </section>

  <section aria-labelledby="power-wind" data-testid="power-wind">
    <h2 id="power-wind">{t.power.sections.wind}</h2>
    {#if !power.wind}
      <p>{t.power.noWind}</p>
    {:else}
      {@const w = power.wind}
      <p>{t.power.windIntro(w.simulation.years, w.simulation.samplesPerHour, w.simulation.seed)}</p>
      <div class="scroll">
        <table data-testid="wind-patterns">
          <thead>
            <tr>
              <th scope="col">{t.power.cols.pattern}</th>
              <th scope="col" class="num">{t.power.cols.strength}</th>
              <th scope="col">{t.power.cols.duration}</th>
              <th scope="col" class="num">{t.power.cols.share}</th>
              <th scope="col">{t.power.cols.gusts}</th>
            </tr>
          </thead>
          <tbody>
            {#each w.patterns as p (p.code)}
              <tr>
                <td>{p.name}</td>
                <td class="num">{p.strengthVar > 0 ? `${fmt(p.strengthAvg)} ± ${fmt(p.strengthVar)}` : fmt(p.strengthAvg)}</td>
                <td>{durationText(p)}</td>
                <td class="num">{pct(p.share)}</td>
                <td>{p.gusts ? t.power.yes : t.power.no}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
      <div class="controls">
        <label class="slider wide">
          <span>{t.power.heightControl} <output data-testid="height">{heightText}</output></span>
          <input type="range" min="0" max="150" step="5" bind:value={height} />
        </label>
      </div>
      <p data-testid="wind-stats">{t.power.windStats(fmt(meanWind(dist)), pct(shareAtLeast(dist, 0.6)))}</p>
      <PowerWindHistogram {bins} title={t.power.histTitle(heightText)} desc={t.power.histDesc} />
      <details class="table">
        <summary>{t.power.table}</summary>
        <table>
          <thead><tr><th scope="col">{t.power.windAxis}</th><th scope="col" class="num">{t.power.shareAxis}</th></tr></thead>
          <tbody>
            {#each bins as b, i (i)}
              {#if b.share > 0}<tr><td>{fmt(b.from)}–{fmt(b.to)}</td><td class="num">{pct(b.share)}</td></tr>{/if}
            {/each}
          </tbody>
        </table>
      </details>
      {#if windmills.length > 0}
        <h3>{t.power.windAveragesTitle}</h3>
        <p class="muted note">{t.power.windAveragesNote}</p>
        <div class="scroll">
          <table data-testid="wind-averages">
            <thead>
              <tr>
                <th scope="col">{t.power.cols.name}</th>
                <th scope="col">{t.power.cols.mod}</th>
                <th scope="col" class="num">{t.power.cols.avgPeak}</th>
                <th scope="col" class="num">{t.power.cols.ofFull}</th>
                <th scope="col" class="num">{t.power.cols.avgFree}</th>
              </tr>
            </thead>
            <tbody>
              {#each windmills as p (p.id)}
                {@const a = windAverages(sailed(p) as WindModel, dist)}
                <tr data-producer={p.id}>
                  <td>{@render name(p.item, p.name, [])}{#if p.model.turbulencePenalty}<span class="muted turb" title={t.power.turbulence}>*</span>{/if}</td>
                  <td><ModLink id={p.mod} mods={meta.mods} /></td>
                  <td class="num" data-col="avg-peak">{fmt(a.peakPower)}</td>
                  <td class="num" data-col="of-full">{pct(a.ofFull)}</td>
                  <td class="num">{fmt(a.freeSpeed)}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
        {#if windmills.some((p) => p.model.turbulencePenalty)}<p class="muted note">* {t.power.turbulence}.</p>{/if}
      {/if}
      <PowerSources entries={[{ name: t.power.sections.wind, sources: w.sources ?? [] }]} />
    {/if}
  </section>

  <section aria-labelledby="power-explorer" data-testid="power-explorer">
    <h2 id="power-explorer">{t.power.sections.explorer}</h2>
    <p>{t.power.explorerIntro}</p>
    <div class="presets" role="group" aria-label={t.power.presets}>
      <span class="muted">{t.power.presets}</span>
      {#if reference}<button type="button" onclick={() => setPreset(reference?.id, 4)}>{t.power.presetHammers}</button>{/if}
      {#if quern}<button type="button" onclick={() => setPreset(quern?.id, 1)}>{t.power.presetQuern}</button>{/if}
      <button type="button" onclick={() => (picks = [])}>{t.power.clear}</button>
    </div>
    {#if picks.length > 0}
      <table class="picks" data-testid="picks">
        <thead><tr><th scope="col">{t.power.machine}</th><th scope="col">{t.power.count}</th><th><span class="visually-hidden">{t.power.remove}</span></th></tr></thead>
        <tbody>
          {#each picks as pick, i (i)}
            <tr>
              <td>
                <select bind:value={pick.id} aria-label={t.power.machine}>
                  {#each machinesByMod as [mod, list] (mod)}
                    <optgroup label={modName(mod)}>
                      {#each list as c (c.id)}<option value={c.id}>{c.name} ({loadText(c)})</option>{/each}
                    </optgroup>
                  {/each}
                </select>
              </td>
              <td><input type="number" min="0" max="999" step="1" bind:value={pick.count} aria-label={t.power.count} /></td>
              <td><button type="button" onclick={() => (picks = picks.filter((_, j) => j !== i))} aria-label={t.power.removeRow(byId.get(pick.id)?.name ?? pick.id)}>×</button></td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
    <p class="row">
      <button type="button" onclick={addRow} disabled={machines.length === 0}>{t.power.addMachine}</button>
      {#if hasOil}
        <label class="check" title={t.power.dryHint(dryMultiplier)}><input type="checkbox" bind:checked={dry} />{t.power.dry}</label>
      {/if}
    </p>
    {#if hasOil}<p class="muted note">{t.power.dryHint(dryMultiplier)}</p>{/if}
    {#if power.wind}
      <div class="controls">
        <label class="slider wide">
          <span>{t.power.heightControl} <output>{heightText}</output></span>
          <input type="range" min="0" max="150" step="5" bind:value={height} />
        </label>
      </div>
    {/if}
    <p class="total" data-testid="total-load" role="status">{t.power.totalLoad(fmt(load))}</p>
    {#if picks.length === 0}
      <p class="muted">{t.power.emptyLoad}</p>
    {:else}
      <PowerBars
        title={t.power.explorerTitle}
        desc={t.power.explorerDesc(heightText)}
        axis={t.power.speedAxis}
        groups={explorerBars}
        legend={families.map((f) => ({ label: t.power.families[f], color: COLOR[f] }))}
        testid="explorer-chart"
      />
      <div class="scroll">
        <table data-testid="explorer">
          <thead>
            <tr>
              <th scope="col">{t.power.cols.producer}</th>
              <th scope="col">{t.power.cols.mod}</th>
              <th scope="col" class="num">{t.power.cols.speed}</th>
              <th scope="col" class="num">{t.power.cols.avgSpeed}</th>
              <th scope="col" class="num">{t.power.cols.stalled}</th>
            </tr>
          </thead>
          <tbody>
            {#each results as r (r.p.id)}
              <tr data-producer={r.p.id}>
                <td>{@render name(r.p.item, r.p.name, [])}</td>
                <td><ModLink id={r.p.mod} mods={meta.mods} /></td>
                <td class="num" data-col="speed">{r.full.stalled ? t.power.stalls : fmt(r.full.speed)}</td>
                <td class="num">{r.avg ? fmt(r.avg.speed) : "–"}</td>
                <td class="num">{r.avg ? pct(r.avg.stalled) : r.full.stalled ? pct(1) : pct(0)}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  </section>

  <section aria-labelledby="power-consumers" data-testid="power-consumers">
    <h2 id="power-consumers">{t.power.sections.consumers}</h2>
    {#if machines.length > 0}
      <PowerBars title={t.power.consumersTitle} desc={t.power.consumersDesc} axis={t.power.cols.load} groups={consumerGroups} legend={consumerLegend} testid="consumers-chart" />
    {/if}
    {#snippet consumerTable(list: Consumer[], testid: string)}
      <div class="scroll">
        <table class="wide-table" data-testid={testid}>
          <thead>
            <tr>
              <th scope="col">{t.power.cols.name}</th>
              <th scope="col">{t.power.cols.mod}</th>
              <th scope="col">{t.power.cols.category}</th>
              <th scope="col" class="num">{t.power.cols.load}</th>
              <th scope="col" class="num">{t.power.cols.idle}</th>
              <th scope="col" class="num">{t.power.cols.dry}</th>
              <th scope="col">{t.power.cols.note}</th>
            </tr>
          </thead>
          <tbody>
            {#each list as c (c.id)}
              <tr data-consumer={c.id}>
                <td>{@render name(c.item, c.name, c.sources)}</td>
                <td><ModLink id={c.mod} mods={meta.mods} /></td>
                <td>{t.power.categories[c.category]}</td>
                <td class="num" data-col="load">{loadText(c)}</td>
                <td class="num">{c.idleLoad === undefined ? "–" : fmt(c.idleLoad)}</td>
                <td class="num">{c.oil ? dryText(c) : "–"}</td>
                <td class="text">{c.note ?? ""}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/snippet}
    {@render consumerTable(machines, "consumers")}
    {#if others.length > 0}
      <details class="table">
        <summary>{t.power.transmission(others.length)}</summary>
        {@render consumerTable(others, "transmission")}
      </details>
    {/if}
    <PowerSources entries={consumers.map((c) => ({ name: c.name, ...link(c.item), mod: modName(c.mod), sources: c.sources }))} />
  </section>
{/if}

<style>
  h2 {
    font-size: 1.3rem;
    margin: 2rem 0 0.5rem;
    padding-bottom: 0.2rem;
    border-bottom: 1px solid var(--border);
  }
  h3 {
    font-size: 1.05rem;
    margin: 1.5rem 0 0.3rem;
  }
  .toc {
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem 1rem;
  }
  .note {
    font-size: 0.85rem;
  }
  .scroll {
    overflow-x: auto;
    max-width: 100%;
  }
  table {
    font-size: 0.9rem;
  }
  /* Wide tables keep readable columns and scroll sideways in .scroll on a phone, rather
     than squeezing a long condition or note into a column a word wide. */
  .wide-table {
    min-width: 60rem;
  }
  .wide-table td:first-child {
    min-width: 12rem;
  }
  .wide-table td.text {
    min-width: 18rem;
  }
  thead th {
    border-bottom: 1px solid var(--border);
    vertical-align: bottom;
  }
  tbody tr:hover {
    background: var(--surface-2);
  }
  .num {
    text-align: right;
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
  .nowrap {
    white-space: nowrap;
  }
  .key {
    display: inline-block;
    width: 0.8rem;
    height: 0.6rem;
    border-radius: 2px;
    margin-right: 0.35rem;
  }
  .fallback {
    display: inline-block;
    margin-left: 0.3rem;
    padding: 0 0.3rem;
    border: 1px solid var(--critical);
    border-radius: var(--radius);
    font-size: 0.75rem;
  }
  .turb {
    margin-left: 0.15rem;
  }
  .controls {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-end;
    gap: 0.75rem 1.5rem;
    margin: 1rem 0 0.5rem;
  }
  fieldset {
    border: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-wrap: wrap;
    gap: 0.2rem 0.9rem;
  }
  legend {
    float: left;
    margin-right: 0.6rem;
    color: var(--muted);
  }
  fieldset label,
  .check {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    white-space: nowrap;
  }
  .slider,
  .select {
    display: flex;
    flex-direction: column;
    gap: 0.1rem;
    font-size: 0.9rem;
  }
  .slider input {
    width: 12rem;
    max-width: 100%;
    accent-color: var(--accent);
  }
  .slider.wide input {
    width: 18rem;
  }
  output {
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }
  select,
  input[type="number"] {
    padding: 0.25rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  input[type="number"] {
    width: 5rem;
  }
  /* Fixed columns, so a long machine name shrinks its select instead of widening the page. */
  .picks {
    width: 100%;
    max-width: 34rem;
    table-layout: fixed;
  }
  .picks th:nth-child(2) {
    width: 5rem;
  }
  .picks th:nth-child(3) {
    width: 2.5rem;
  }
  .picks select {
    width: 100%;
  }
  .picks input[type="number"] {
    width: 4.5rem;
  }
  .presets,
  .row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem 0.75rem;
  }
  .total {
    font-weight: 600;
    font-size: 1.1rem;
  }
  details.table {
    margin: 0.5rem 0;
  }
  details.table summary {
    cursor: pointer;
    color: var(--muted);
    font-size: 0.9rem;
  }
</style>
