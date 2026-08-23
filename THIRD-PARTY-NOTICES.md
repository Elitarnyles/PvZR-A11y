# Third-party notices

This mod's own source is MIT licensed. See LICENSE.

Two libraries are shipped alongside it, in `lib/`, because without them the mod cannot speak
and a new user would get total silence with nothing to explain it. They are not part of this
project and they are not MIT licensed. Their terms are below, and the full texts are in `lib/`.

Both are loaded dynamically at runtime. The mod calls into them across a plain C function
boundary and contains no part of their code, which is what the LGPL allows.

## Tolk

Tolk is a screen-reader abstraction layer: one interface that speaks through whichever reader
is running — NVDA, JAWS, Window-Eyes, SuperNova, System Access, ZoomText — and falls back to
Microsoft SAPI when none is. It is the reason this mod does not have to know which reader you
use.

- Author: Davy Kager
- Home: https://github.com/dkager/tolk
- Licence: GNU Lesser General Public License, version 3
- Full text: `lib/LICENCE-Tolk-LGPL-3.0.txt`, together with `lib/LICENCE-Tolk-GPL-3.0.txt`,
  which the LGPLv3 supplements and requires
- Shipped file: `lib/Tolk.dll`

The LGPL gives you the right to replace this library with your own build. Nothing here
prevents that: drop your `Tolk.dll` into the game's `UserLibs` folder and the mod will load it
instead. No signature, hash or version is checked.

## NVDA Controller Client

The client library NVDA provides so that other programs can send it text to speak. Tolk loads
it when NVDA is running.

- Author: NV Access Limited and contributors
- Home: https://github.com/nvaccess/nvda
- Licence: GNU Lesser General Public License, version 2.1
- Full text: `lib/LICENCE-nvdaControllerClient-LGPL-2.1.txt`
- Shipped file: `lib/nvdaControllerClient64.dll`

The same applies: replace the file in `UserLibs` and the mod will use yours.

## What is deliberately not shipped

Tolk can also drive JAWS, Window-Eyes, SuperNova, System Access and ZoomText, but each needs
its own client library, and those are not redistributable here. If you use one of those
readers, put its client library in `UserLibs` next to `Tolk.dll` and Tolk will find it. The
list of file names is in the Tolk documentation.

SAPI needs nothing extra. It is part of Windows, and the mod can fall back to it — see the
`AllowSapi` setting, which is off by default so that a silent mod is a visible problem rather
than a quietly degraded one.

## Trademarks

Plants vs. Zombies is a trademark of Electronic Arts Inc. and PopCap Games. This mod is
unofficial, unaffiliated and unendorsed. It ships no game code and no game assets, and it
requires a legally obtained copy of the game to do anything at all.
