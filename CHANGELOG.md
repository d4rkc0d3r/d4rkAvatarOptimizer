## Next Version
### Features
* Add info box if build target is Android to tell users that the optimizer doesn't support Quest avatars.
* Add validation check that checks if the optimizer got added to an optimized copy.
* Add validation error if the amount of base playable layers isn't 5.

### Bug fixes
* Fix extra out parameters in the fragment shader not getting added to the function declaration. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/10)
* Add ParserException if any parameter in vertex or fragment can't be parsed.
* Fix parsing of template types to permit spaces between the type and the template brackets.
* Fix new lines in string literals not being the environment's new line character. This caused shader warning spam on poi shader.

## v1.5
### Bug fixes
* Fix crash when an EditorOnly tagged game object was in the exclusions list.
* Fix non Texture2D textures that are the same reporting that they can't be combined.
* Fix non Texture2D textures trying to combine with `null`.
* Fix crash when vertex shader has no input struct.
* Fix geometry shader input type to also allow for point & line.
* Fix nested BlendTrees not getting iterated through to fix animation paths.
* Move the optimized copy to the same scene as the original.
* Replace VFACE semantic with SV_IsFrontFace.
* Force include TEXCOORD0 semantic in the vertex shader input struct. This fixes cases where the sematic only exists in some shader variants.
* Don't delete transforms that are moved by animations.
* Don't delete parent transforms of Constraints.
* Deep copy blend trees when fixing animation paths.

## v1.4
### Features
* Add info box educating about increased poly count after optimization if some renderer has more material slots than sub meshes.

### Bug fixes
* Shader optimizer skips parsed shaders that are null or parsed incorrectly.
* Fix floats getting written with system localization instead of invariant culture.
* Fix crash when an animation has a material swap that doesn't point to a transform on the avatar. Fix for: [Null pointer error on Create Optimized Copy](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/7)
* Fix bug where the merge mesh logic would ignore the exclusion list when merging into an excluded mesh.
* Fix merge perf rank preview pre optimization counting EditorOnly meshes & materials.
* Fix merge perf rank preview not taking into account particle system material slots.
* Fix merge perf rank preview counting the "hidden" extra material slots if they didn't get baked into a mesh.
* Fix bug with animated properties that ended in numbers.
* Fix that debug info for all game objects with toggles would list the always disabled game objects instead.
* Fix frag inject code not running if there were no array properties or mesh toggles. This caused the code to preserve texture samplers not to be injected. Fix for: [Shader issue?](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/8)
* Write active mesh toggle state to the `_IsActiveMesh` material properties. Also base them on the Renderer enabled state instead of just the game object active state.

### Debug Info
* Catch exception in the shader optimizer to show which shader is causing the exception.

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
