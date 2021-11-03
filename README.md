<p align="center">
  <img src="https://i.imgur.com/SoiQCeG.gif" alt="sfx">
</p>

<h3 align="center">
  <b>
      Unity Particle System to Flyff SFX
  </b>
</h3>

## About
This is an export script made for the unity engine to convert unity particle systems to flyff-compatible SFX files. This script only works for converting Billboard-type SFX parts, since Flyff's particle's do not provide enough agency to warrant a conversion from unity's. If you have any ideas for particles, do let me know!

<p align="center">
  <img src="https://i.imgur.com/NWkYG5R.gif" alt="sfx1" width="550px">
  <br>turns into<br>
  <img src="https://i.imgur.com/aOjZfQu.gif" alt="sfx2" width="550px">
</p>

### Module Support
The following are the only modules supported in the unity particle system, since they are the only ones that can be translated to work with Flyff keyframe SFX.
- [x] Emission
- [x] Color over lifetime
- [x] Size over lifetime
- [x] Velocity over lifetime
- [x] Rotation over lifetime
- [x] Noise
- [x] Renderer

## Installation
Copy the SFXExport.cs script into the Assets/Editor folder. That's it.

## Usage
- You must place any textures you use for the particle system in Assets/Resources/SFXTextures.
- Create your particle system gameobject.
  - You may add child particle systems do the parent particle system as well.
  - Modify the particle material to play with the blend settings, like glow and blend.
- Save the gameobject as a prefab (drag it from the hierarchy window into the project window, in any folder).
- Select the prefab in the project window.
- Click on Tools -> Export Selected Particle Prefab to .SFX.
  - The exporter will save the .SFX file in Assets/Exports, along with the textures that are used.
- Enjoy the hour you just saved by using curves instead of keyframes!

#### General Don'ts
- Don't use the rate properties in the emission module, use one burst instead, and set the burst count to 1.
- Don't use any of the modules that are not supported (duh), as they won't allow you to export.
- Don't use modes that are not either constant or curve, for the modules that support those decisions.
- Don't forget to check the console, it's very helpful.

## Troubleshooting
If something isn't working as expected, the first thing you should do is **check the logs**. Check your console and enable warnings too! If you do find a bug, feel free to create an issue or message me on discord (Frostiae#2809).
