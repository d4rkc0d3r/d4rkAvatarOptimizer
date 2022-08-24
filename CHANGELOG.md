## v1.3
### Features
* Added perf rank change indicators to the Merge Preview foldout.

### Bug fixes
* Fixed that Color properties without hdr tag wrote the raw sRGB value as a linear value into the shader.
* Some meshes can have `null` bones. Use the root bone instead. Fix for: [NullReferenceException: Object reference not set to an instance of an object.](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/6)

## v1.2
### Features
* Show warning if there are textures that are crunch compressed.
* Add list of all crunch compressed textures to the debug info section.
* Always skip variants `DYNAMICLIGHTMAP_ON`, `LIGHTMAP_ON`, `LIGHTMAP_SHADOW_MIXING`, `DIRLIGHTMAP_COMBINED`, and `SHADOWS_SHADOWMASK` since they are unused on avatars.

### Bug fixes
* Fixed the delete unused gameobjects function not respecting the exclusions properly.
* Don't merge textures that are crunch compressed. Fix for: [Material merging results in corrupted material/textures](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/5)
* Inline replace the `UNITY_POSITION` macro. Fix for: [Material merging results in corrupted material/textures](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/5)
* Change default "bump" texture value to the correct value of `float4(0.5, 0.5, 1, 1)` instead of the incorrect 0.5 alpha that the unity doc says. [I noticed that if I merge materials where one of them has a normal map and the others do not, I can run into broken normals](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/5#issuecomment-1220827519)
* Added `__Baked` property check for locked in shaders. This is used by silent crosstone for shader locking.
* Fix uv0 end swizzle not being applied to the original vertex shader copy.

### Debug info
* Add original shader name to the generated shader as a comment.

## v1.1
### Features
* Added exclusion Transform list. Anything that is under the hierarchy of the Transforms in the exclusion list will not be touched by the optimizer.  
  Implements suggestion: [if we could get a way to exclude certain things that would fix it](https://twitter.com/JettsdVRC/status/1559692330965372930)
* Show warning if any materials are locked in.
* Add a list of all locked in materials in the debug info foldout.
### Bug fixes
* Fixed a bug when "Write Properties as Static Values" was disabled with "Merge Same Dimension Textures" enabled and a material had to sample from only one arrayIndex.
* Count parsed shaders that have 0 lines as unparsable.  
  Fix for: [Out of range error cancelling optimisation](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/4)
