// The standalone model viewer's entry: the site's own model page (src/components/ModelPage.svelte) for
// one model, its shape and rig built into the page (scripts/standalone-viewer.ts). Nothing is fetched.
import { mount } from "svelte";
import ModelPage from "../../src/components/ModelPage.svelte";
import "../../src/app.css";
import { model } from "virtual:standalone-model";

document.title = model.title;
const target = document.getElementById("app");
if (!target) throw new Error("#app is missing from the page");
const note = document.createElement("p");
note.className = "muted standalone-note";
note.textContent = "A standalone copy of the site's model viewer, the model's files built in.";
target.append(note);
mount(ModelPage, { target, props: { model } });
