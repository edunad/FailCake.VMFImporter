<h2>
	<p align="center">
		FailCake.VMFImporter
	</p>
</h2>
<h4>
	<p align="center">
		<a href="/LICENSE"><img alt="logo" src="https://img.shields.io/github/license/edunad/FailCake.VMFImporter"/>&nbsp;</a>
		<a href="https://github.com/edunad/FailCake.VMFImporter/issues?q=is%3Aopen+is%3Aissue+label%3ABUG"><img alt="logo" src="https://img.shields.io/github/issues/edunad/FailCake.VMFImporter/BUG.svg"/>&nbsp;</a>
		<a href="https://github.com/edunad/FailCake.VMFImporter/commits/master/"><img alt="logo" src="https://img.shields.io/github/last-commit/edunad/FailCake.VMFImporter.svg"/>&nbsp;</a><br/>
		<br/>
		<a href="#installation">Installation</a> -
		<a href="#features">Features</a> -
		<a href="#issues">Issues</a> -
		<a href="#setup">Setup</a>
	</p>
</h4>

> [!IMPORTANT]
> THIS ONLY WORKS WITH [HAMMER++](https://ficool2.github.io/HammerPlusPlus-Website/). Regular Hammer isn't supported.

> [!IMPORTANT]
> Originally built for a game made by a friend [Delivery & Beyond](https://store.steampowered.com/app/3376480/Delivery__Beyond/), not really intended for public use. But after thinking about it, figured I'd share it anyway. Some stuff is still hardcoded for the game, but eventually I want to turn this into a proper plugin.

> [!IMPORTANT]
> Requires the [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier) package (to simplify the meshes)

<img width="512" height="512" alt="{F9280638-B3EB-4238-BFB4-7B0228BD40B1}" src="https://i.rawr.dev/O7lmnfXlCI.gif" />
<img width="712" height="157" alt="{6D9959EA-294A-4093-ADE8-F6AD30858708}" src="https://github.com/user-attachments/assets/c2522b95-f474-4d91-a805-2cbad3c0f993" />

## Installation
<img width="256" height="256" src="https://github.com/user-attachments/assets/5d3fcc53-d3b5-4938-94b6-9974f772c646" />

Open Unity's Package Manager and add a new Git URL:
```
https://github.com/Whinarn/UnityMeshSimplifier.git
```
```
https://github.com/edunad/FailCake.VMFImporter.git?path=/com.failcake.vmf.importer
```

## Features
- Displacement support
- UV / OFFSET support (per plane)
- Custom texture loading with material overrides
- Texture arrays for model packing
- Entity replacement through templates (no map I/O yet though)
- Tool texture hiding/removal
- Basic collision generation with box colliders (for complex stuff, use func_ entities and add mesh colliders manually)
- Material data for footstep sounds

## Issues
**Texture arrays get duplicated for each VMF model:**
- Tried making a central VTF database but ran into too many problems. Might revisit this later.

**Displacement heights don't match Hammer exactly:**
- The math gets wonky if you create a displacement and then resize it in Hammer. Need to fix the calculation at some point :P

**Texture sizes can break:**
- Your textures need to be power of 2 (64x64, 128x128, etc) or things get weird.

**Textures are not transparent:**
- Transparent textures at the moment use alpha clipping, not actual transparency. This will change in the future

## Setup
1. Open `VMFImporterSettings` and add your VPK paths. Example: `D:\Program Files (x86)\Steam\steamapps\common\Source SDK Base 2013 Singleplayer\hl2\hl2_textures_dir.vpk`

2. To replace a material with your own shader, add it to the `Material Override` dictionary. Remember the key is CASE SENSITIVE.

3. To replace entities, add them to the `Entity Overrides` dictionary. Also CASE SENSITIVE.

4. Layer Materials are special textures meant for runtime replacement. This was a Delivery & Beyond specific thing.

<img width="961" height="942" alt="{B3469AF6-D7B7-4C71-A4C1-8FA2D0C9FCA3}" src="https://github.com/user-attachments/assets/aca2ebb4-073f-4da5-be9d-a7c650f98c5f" />
