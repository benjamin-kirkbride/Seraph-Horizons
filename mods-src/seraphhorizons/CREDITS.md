# Credits

## Tidy Variants grouping rules

Some grouping rules in `assets/seraphhorizons/config/tidyvariants-overrides.json` were ported from Handbook Declutterer
by actioninja (https://github.com/actioninja/handbookdeclutterer), which uses files from DanaCraluminum's
Fix Handbook Clutter (https://github.com/Craluminum-Mods/FixHandbookClutter). They are restated as
Tidy Variants override rules, not copied; the reviewed revision is
`5b407a474936f1d8ab406fd4bda5537693b5f6f4` (Handbook Declutterer 2.0.1).

Handbook Declutterer's `ATTRIBUTE.md`:

```
This mod utilizes some files from DanaCraluminum's Fix Handbook Clutter which can be found at 
https://github.com/Craluminum-Mods/FixHandbookClutter
```

Handbook Declutterer's `LICENSE` (Fix Handbook Clutter's `LICENSE` has the same text and copyright line):

```
MIT License

Copyright (c) 2022 Craluminum2413

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Parts from Immersive Woodworking's sawmill model

Most of the bucking sawmill's model (`assets/seraphhorizons/shapes/block/buckingmill*.json`) was made for this mod. Some of its
parts are taken from the sawmill model of Immersive Woodworking by Bobrik00
(https://mods.vintagestory.at/immersivewoodworking), turned and resized but otherwise as that model has
them: the gears (both pinions, the drum pinion, the crown disc and the small crown gear), the saw blades,
the saw heads and the cranks. In the shape files those elements keep Immersive Woodworking's names
(`Rotor_default_*`, `saw_*`, `sash_*`).

Those parts belong to Bobrik00 and are used and modified here with the author's permission. They are not
covered by this repository's Apache License; ask Bobrik00 before reusing or redistributing them. The rest
of the model is covered by the repository's license.

<!-- OWNER, before release: Bobrik00's permission was given for the bucking sawmill. Confirm with
Bobrik00 that it extends to the rosser; then say so in the rosser's paragraph below (as the bucking
sawmill's says "used and modified here with the author's permission"), and check LICENSE's scope
note, the rosser's credit line in site/models.json and the _comment its generator writes into
rosser.json and rosser_frame.json. Until then the paragraph below makes no claim of permission. -->

Most of the rosser's model (`assets/seraphhorizons/shapes/block/rosser*.json`) was made for this mod too.
Some of its parts are taken from the same sawmill model of Immersive Woodworking by Bobrik00: the crown
disc on its entry shaft and the two pinions that mesh with it, turned and placed as the bucking sawmill's
are. In the shape files those elements keep Immersive Woodworking's names behind a prefix
(`entry_Rotor_default_3_*`, `gear_pinion_w_Rotor_default_1_*`, `gear_pinion_e_Rotor_default_2_*`).
Every other toothed wheel of the rosser (the ring's rim, the ring pinion, the worm wheels and the worms'
threads, the change gears and the banjo gears) is built from copies of one tooth of that model, the peg
of its main rotor's flange (`MainRotor_twoway_013.001`), squared up and placed round each wheel; those
elements are named `*_iwtooth*` and `*_iwthread*`.

Those parts belong to Bobrik00. They are not covered by this repository's Apache License; ask Bobrik00
before reusing or redistributing them. The rest of the model is covered by the repository's license.

## The eidolon's body, from Vintage Story

The eidolon's model (`assets/seraphhorizons/shapes/entity/eidolon/eidolon.json`) is Vintage Story's
own mobile eidolon by Anego Studios, the game's `shapes/entity/lore/eidolon/normal.json` (game
1.22.7), copied into this mod as a snapshot by `Eidolon/tools/make_shape.py`. Its elements and their
geometry, and the vanilla animations the file keeps (`stand-*`, `weapon-*`, `toppleover`), are that
model's, unchanged. Its textures are not copied: the shape points at the game's own files, one of
them swapped for another of the game's (rusty iron for tarnished brass). Added for this mod: the two
anchor elements (`carry-anchor`, `trunk-anchor`), the `RightHand`, `LeftHand`, `Carry` and `Trunk`
attachment points and the laborer's animations (`fell`, `carry-*`, `lift`, `setdown`, `trunk-*`,
`guard-idle`, `hung`, `activate`, `slump`, `standup`).

The model belongs to Anego Studios and is not covered by this repository's Apache License; it is
used here as part of a mod for their game. The additions are covered by the repository's license.

The eidolon gantry's model (`assets/seraphhorizons/shapes/block/eidolongantry.json`) was made for this
mod, and is covered by the repository's license, except for the body hung in it: the elements named
`b_<stage>_<name>` are a copy of the eidolon's elements above (Anego Studios' model), posed in the
`hung` animation by `EidolonGantry/tools/make_shape.py`, and are that model's as above.
