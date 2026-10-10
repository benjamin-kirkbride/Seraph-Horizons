// One bundle of the tabbed standalone page (scripts/standalone-tabs.ts): a checkout's model page
// (its src/components/ModelPage.svelte, rig maths and three.js scene) and the model tabs it draws.
// It registers a mount function in globalThis.__standaloneTabs under the bundle's name; the page's
// shell (tabs-shell.ts) mounts one tab at a time and calls the returned function to take it down.
import { mount, unmount } from "svelte";
import ModelPage from "virtual:standalone-tabs-page";
import { group, tabs } from "virtual:standalone-tabs-data";
import { selectTab } from "./tabs-model-data.ts";

export interface TabBundle {
  mount(target: HTMLElement, slug: string): () => void;
}

const bundle: TabBundle = {
  mount(target, slug) {
    const tab = tabs[slug];
    if (!tab) throw new Error(`no model tab "${slug}" in bundle ${group}`);
    selectTab(slug);
    const page = mount(ModelPage, { target, props: { model: tab.model } });
    return () => void unmount(page);
  },
};

const registry = ((globalThis as { __standaloneTabs?: Record<string, TabBundle> }).__standaloneTabs ??= {});
registry[group] = bundle;
