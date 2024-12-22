## v3.9.3
### Changes
* Use the existing auto detection of penetrators to add them to exclusions instead of trying to do partial optimizations on them.

### Bug Fixes
* Fix meshes with different cast & receive shadow settings getting merged together.
* Fix ParticleSystems with skinned mesh renderer shape breaking when merging meshes & materials.
* Add missing MMD blendshape names to the list of known MMD blendshape names. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/137)
* Fix synced animator controller layers breaking when layers get merged or removed. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/138)

## v3.9.2
### Bug Fixes
* Fix shader analyzer not handling `_fragment` and other per pass suffixes for `shader_feature` pragmas.
* Fix shader analyzer parsing ShaderLab case sensitive even though its not supposed to be. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/pull/130)

## v3.9.1
### Bug Fixes
* Fix crash when the avatar has a skinned mesh with out of bounds bone indices. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/129)

## v3.9.0
### Features
* Generated shaders and materials are now stripped of all properties that got baked into the shader.
  * This reduces avatar download and uncompressed size slightly.
* Reduced the amount of shader variants generated when using `Write Properties as Static Values`.
  * Forward base passes don't generate variants without the `LIGHTPROBE_SH` keyword anymore.
  * Materials with render queue > 2500 don't generate any shadow related variants.

### Changes
* Generated shader names now have a hash suffix.
* `Keep MMD Blend Shapes` is now `MMD Compatibility` and also prevents the first 3 fx layers to get merged or deleted. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/122)
* Rewrote handling of material property animations with WD ON while using `Shader Toggles`.
  * It now tracks which meshes are animated and put those into a mask in the material instead of checking for NaN values.
* Automatically add the mesh of RealKissSystem to exclusions internally.

### Bug Fixes
* Copy the toggle `Normalized Blend Values` of direct blend trees. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/120)
* Fix `FindAllPhysBoneDependencies` not checking VRCConstraints or FinalIK components. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/123)
* Fix animation bindings of the base type `Renderer` not getting respected properly. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/121)
* Shader parser now handles `ColorMask 0` passes with no code block correctly and throws a parse error for other non code block passes.
* Fix optimizer not checking all the animator controllers in the avatar descriptor for some things.
* Fix special animation layers not getting updated animation clips.
* Layers with only a off animation no longer block default disabled meshes from using NaNimation. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/126)
* Fix `IsAnimatableBinding` not checking if the bindings type is a base type of the component types.

## v3.8.0
### Features
* Add support for VRChat constraints.
* Add support for FinalIK components with the `Delete Unused GameObjects` option. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/115)
* Add support for material variants. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/116)
* Add support for global `Packages` include paths for local packages.
* Add support for prefab variants. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/112)

### Changes
* Changed default settings to match the `Basic` preset.
* `Optimize on Upload` is now called `Apply on Upload`.
* Generated shader include files now have more readable names than just hashes.

## v3.7.4
### Bug Fixes
* Fix viseme blendshapes and eyelid blendshapes sometimes not getting reassigned correctly if they aren't on a mesh named `Body`.
* Fix crash with missing scripts on the avatar.

## v3.7.3
### Bug Fixes
* Fix the hardcoded `_VirtualLens_Root` exclusion not working for the `Delete Unused GameObjects` option.
* Fix shader optimizer not handling shaders correctly that declare `_LightColor0` or `_SpecColor` manually instead of using the unity include. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/111)

## v3.7.2
### Features
* Strip Meta pass from shaders that have it.

### Changes
* Moved the code to ensure texture samplers exist at the end of the fragment instead of the beginning.
  * This improves performance with poi fur.
* Shaders that don't end in `.shader` or `.orlshader` are now explicitly marked as not supported.

### Bug Fixes
* Fix shader analyzer not parsing default values for function parameters. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/109)
* Fix `Merge Same Dimension Textures` merging textures with different filter or wrap settings.
* Fix `Delete Unused GameObjects` not marking the phys bone root as moving when it has multiple children that are all but one ignored.

## v3.7.1
### Changes
* Removing unused bindings from clips only checks for specific property existence for Renderer bindings. All other types just check if the component exists for now.

### Bug Fixes
* Fix `RenderersHaveSameAnimationCurves` only checking if the keyframe values match and ignoring the keyframe times.
* Fix removing unused animation clip bindings not respecting the Exclusions list.

## v3.7.0
### Features
* Add global setting `MotionTimeApproximationSampleCount` to adjust the amount of samples used for the motion time approximation.

### Bug Fixes
* Fix texture merging default value parsing not working.
* Fix global includes with leading slash like `/Assets/...` not working. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/106)
* Fix texture tile and offset values being wrong when materials get merged with different values.
* Only apply the rescaling of bones during mesh combining to bones that have scale animations. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/105)

## v3.6.4
### Bug Fixes
* Fix crash when the root Animator component has no avatar assigned to it. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/102)
* Fix fx layer 0 getting deleted when it has states or will be the only layer. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/100)

## v3.6.3
### Bug Fixes
* Fix mesh combining bone scale fix not normalizing the scale of all the parents of the bones during bake.
* Fix scaled bones not getting treated as "moving" bone when combining meshes.

## v3.6.2
### Changes
* `Delete Unused GameObjects` prepends the name of the deleted GameObject to the name of its children.

### Bug Fixes
* Automatically add `_VirtualLens_Root` to exclusions so it doesn't break when running the optimizer.
* Fix shader optimizer breaking on shaders that have no `Properties` block.
* Change generated animations to use `EditorCurveBinding.FloatCurve` instead of `EditorCurveBinding.DiscreteCurve`. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/98)
  * This fixes issues with GoGoLoco & NaNimation keep default enabled state in unity 2022.3.22f1.
* Fix mesh combining breaking when bones are scaled down by default.
* Fix bounding box of mesh combining being off when the root transform is not at the origin.

## v3.6.1
### Bug Fixes
* Fix ShaderAnalyzer not inline including absolute paths that start with `Assets/`.
* Fix empty Properties block not getting parsed correctly.
* Fix `#pragma shader_feature` only getting replaced by defining the keywords instead of defining them as a true value.
* Fix always disabled ParticleSystemRenderers getting deleted when `Delete Unused Components` is enabled. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/91)
  * Somehow the ParticleSystemRenderer component is important for the particle system to work even if it is disabled. Maybe something about sub emitters? idk
* Fix `VRCAnimatorPlayAudio` not getting its source path adjusted when using `Delete Unused GameObjects`. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/93)

## v3.6.0
### Features
* Add support for `VRCHeadChop` component.
* Add support for `VRCAnimatorPlayAudio` state machine behavior.

### Changes
* Add timing of shader parse to single shader or material instead of just for whole folder parses in the Shader Analyzer Debugger.
* Improved shader parser performance massively by doing simple checks instead of using `.StartsWith()` & `.EndsWith()` everywhere and replacing some left over regex with string operations.

### Bug Fixes
* Fix wire frame showing up on materials on AMD gpus sometimes. (hopefully its gone for good)
* Fix first merged material blob on mesh generating all the animated property arrays instead of only the ones that are used by the meshes in the blob.
* Fix `#include_with_pragmas` not getting parsed correctly. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/86)
* Fix `HLSLPROGRAM` not working since `UnityLightingCommon.cginc` got always included and it has `fixed4` variable declarations.
* Fix first timing check for the UI not working correctly and upping the disable of the auto refresh to exceeding the time limit 3 times to give the JIT more time to optimize.
* Fix SPS penetrators getting jumbled up by `Mesh.Optimize()` [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/pull/89)
  * This still isn't full support for SPS yet so if it still doesn't work put the penetrator in the exclusion list.

## v3.5.3
### Bug Fixes
* Fix BoxCollider animation bindings getting thrown away for the BoxCollider that gets implicitly added by VRCStation components. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/83)

## v3.5.2
### Bug Fixes
* Fix Texture2D_ST material property animations not working when using the `Use Shader Toggles` option.

## v3.5.1
### Bug Fixes
* Fix root animator parameter animations getting removed as non animatable binding. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/82)
* Mark all bindings that reference a transform as animatable as long as the transform exists on the avatar.  
  This is because there are more animatable bindings than the AnimationUtility tells us about. Thanks Haï~ for linking me to [this](https://forum.unity.com/threads/new-animationclip-property-names.367288/#post-2384172).
* Mark all material property animations as animatable when the target path is affected by any material swap animation.

## v3.5.0
### Features
* Add auto settings for Shader and NaNimation toggles that disable them if the avatar has more than 150k tris.
  * Also shows a warning if Shader or NaNimation toggles are enabled and the avatar has more than 150k tris.
* Always remove animation curves which reference a binding that doesn't exist on the avatar.

### Changes
* Swap `OnPreprocessTexture` to `OnPreprocessAsset` to stop the optimizer from triggering a full project all texture reimport in unity 2022. [(more)](https://forum.unity.com/threads/unity-made-full-project-reimport-on-every-small-change.1066844/)
  * This will trigger one last full texture reimport if you already used the optimizer in unity 2022 :(
* Remove option `Merge Cull Back with Cull Off`, just set the culling of your materials to off if you want to merge them.
* Reduced generated shader complexity handling material property animations:
  * When Shader or NaNimation toggles are enabled, it will still perform a basic merge of meshes that get animated in the same way.
  * When all meshes in a merged mesh blob have material properties animated in the same way, it will no longer split the properties into separated properties.  
    This helps immensely when using global material adjustments like for example color, hue or brightness radials.

### Bug Fixes
* Fix materials getting merged even if they have different `VRCFallback` shaders set.
* Fix copy of old material property animation when it is a color property.
* Fix optimizer always injecting the animated property disambiguation code on merged material blobs that have at least one material that comes from the combined mesh path.
* Don't generate default properties which have names that are invalid ShaderLab property names. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/81)
* Fix always disabled renderer components not getting deleted before mesh merging happens.

## v3.4.1
### Changes
* Added BC6H & DXT1 compression to normal maps in the texture compression analyzer.

### Bug Fixes
* Fix mesh disappearing locally when NaNimation is enabled and the mesh has the Head or one of its children as the root bone.
* Fix shaderlab `Int` parameters actually getting written as an int. "Obviously" they should be written as a float.

## v3.4.0
### Features
* Improved support for poiyomi shaders to better match the lock in logic.
  * Add support for `//ifex` parsing which is important for poi 9.0 as it is used to skip entire passes.
  * Add support for `#if defined(PROP_Texture) || !defined(OPTIMIZER_ENABLED)` macros to skip unused texture properties.
* Massive performance improvements for the perf rank change preview.
  * My personal avatar went from 450ms to 35ms. (`AnimatorController.layers` is slow and you should cache it)

### Changes
* Always merge other meshes into the mesh `Body` if it exists. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/pull/74)
* Add property filter for the shader analyzer debugger.
* `Auto Refresh Preview Timeout` now ignores the first time it is over the threshold.
  * This is because the first time the preview is calculated it takes longer than usual.

### Bug Fixes
* Fix material property animation of the original property not getting the correct `material.` prefix.
* Don't duplicate shader functions with a `Texture2DArray` parameter.
* Fix detection of cull ShaderLab property and disable merge with off when there is more than one cull property.
* Fix crash when a MeshRenderer has no MeshFilter on the same GameObject. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/79)

## v3.3.4
### Bug Fixes
* Fix `Delete Unused GameObjects` not marking the global phys bone collider transforms as used.
* Fix sort order of which mesh should get priority for getting merged into.
* Fix WD ON workflow toggles not stopping NaNimation when `Optimize FX Layer` is disabled.
* Fix NaNimation getting blocked by WD ON workflow even for meshes that are on by default.

## v3.3.3
### Bug Fixes
* Don't normalize normals & tangents when merging meshes. This fixes outlines that are dependent on the normal length to determine mesh scale.
* Clamp blendshape value to 0-100 when baking the blendshape to match in-game blendshape clamping.

## v3.3.2
### Bug Fixes
* Fix crash when no fxlayer is present.
* Fix non shader or NaNimation toggles getting broken.

## v3.3.1
### Bug Fixes
* Fix it not working in unity 2019

## v3.3.0
### Features
* Add new way to toggle merged meshes `NaNimation`.
  * This allows merging meshes that have shaders that don't support shader toggles.
  * Add sub option `Keep Default Enabled State` which only merges meshes that have the same default enabled state.
  * Add sub option `Allow 3 Bone Skinning` which allows NaNimation on all meshes by reducing the skinning quality on meshes that use 4 bone weights.
* Add sub option `Merge MainTex` that decides whether the `_MainTex` property is allowed to be merged into a texture array. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/17)

### Changes
* No longer put the dummy animation clip in empty states. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/63)
* Add debug list `Mesh Bone Weight Stats`.
* Add debug list and validation info for when some meshes can't use NaNimation because of missing on or off animations.
* Collapsed some info boxes regarding materials that can't merge into one info box.
* The UI is now disabled when in play mode. This saves performance when testing with av3 emulator without running the optimizer.
* Requires ORL shader generator 6.2 or newer for faster shader parsing of .orlshader files.
* Mesh only uses 1 or 2 bone weight skinning if none of the vertices need more than that. Was always 4 bone weights before.
* Slightly increase the size of the performance rank change icons.
* Minor performance improvements:
  * Calculating if materials can merge is faster which speeds up the preview.
  * Merging skinned mesh data is faster by pre allocating list capacity and skipping uv sets that don't exist.

### Bug Fixes
* Fix crash when a material name has slashes or other characters that are not allowed in file names.
* Fix crash in 2022 when an extra animator is not humanoid or doesn't have an avatar definition. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/65)
* Fix macro to declare `tex##_ST` not being detected as a custom texture property declaration macro.
* Fix log spam in 2022 from vector array properties also setting as a float value.
* Fix material property animations on MeshRenderer components that get combined into a SkinnedMeshRenderer still being bound to the MeshRenderer type.
* Fix basic merged mesh blobs also trying to use `Use Shader Toggles` when it is enabled but the shaders don't support it. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/66)
* Fix non triangle topology meshes not working when merged.
* Fix `Merge Same Ratio Blend Shapes` not working correctly if some ratios had 0 values in them.
* Fix crash with Skinned Mesh Renderers that have no mesh assigned to them. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/72)
* Fix VRCFury check only checking the root game object and not all children. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/69)

## v3.2.2
### Bug Fixes
* Fix phys bone dependency check only checking the moved transforms and not all children. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/61)
* Fix ignored transform in phys bone not being marked as moving despite it being possible thanks to stretch & squish.
* Fix delete unused game objects not registering phys bone ignore transforms as used. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/61)

## v3.2.1
### Bug Fixes
* Fix crash when a static mesh has a missing material and `Write Properties as Static Values` is enabled. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/60)

## v3.2.0
### Changes
* Remove option `Merge Regardless of Blend Shapes`. It is now always enabled because VRChat now uses unity 2022.
* Add debug info for blendshapes that get merged together.

### Bug Fixes
* Fix warning spam in console if a material has Hidden/InternalErrorShader assigned to it.

## v3.1.4
### Bug Fixes
* Fix jaw flap bone not marked as moving transform. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/pull/58)

## v3.1.3
### Changes
* When the UI takes longer than 500ms (default value, can be changed in settings) it will disable the auto refresh of the preview and instead shows a manual button to refresh the preview. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/51)
* Toggling foldouts no longer refreshes the preview. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/51)

### Bug Fixes
* Fix crash when the FX controller has a layer with two states and only one transition with zero conditions. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/56)

## v3.1.2
### Bug Fixes
* Fix crash when something from the exclusion list gets deleted before the optimizer runs or is marked as editor only. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/50#issuecomment-1717940085)
* Fix UI crash when the avatar has a missing script component on the avatar root.
* Fix crash when the avatar uses empty clips with WD ON toggles. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/50)
* Fix FX Layer detection for non humanoid rigs. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/49)

## v3.1.1
### Bug Fixes
* Hard code that lilToon shaders are not supported as some of them don't get caught by the automated parsing.

## v3.1.0
### Features
* Blend trees can now also get merged into the direct blend tree when `Optimize FX Layer` is enabled.
* Toggle layers can now be merged even when their states are motion time or blend tree based.
* Toggle layers now recognize int params with greater x & less x + 1 as a bool condition. Can only convert int to float if its never used with a not equal condition.
* Toggle layers can now have an arbitrary amount of binary and conditions instead of just 1 or 2.
* Add support for basic multi toggle layers where each state animates the same bindings and all states are only chosen by any state transitions with a single int equals condition.

### Changes
* `Combine Motion Time Approximation` now only generates the 25%, 50% & 75% time points if they improve the approximation.

## v3.0.2
### Bug Fixes
* Fix phys bones getting deleted with `Delete Unused Components` when they only got used for parameters in the animator.
* Fix `Merge Different Property Materials` not working when `Use Shader Toggles` is disabled but meshes get merged with the basic merge.
* Animators sometimes don't write exact values so change the mesh shader discard to `0.5 > _IsActiveMesh` instead of `!_IsActiveMesh`.

## v3.0.1
### Changes
* Use a binary serialized object as root asset for meshes, animations & texture2darrays to speed up the asset creation process.

### Bug Fixes
* Fix crash from EditorOnly mesh which is affected by `Disable Phys Bones When Unused`. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/45)
* Instead of crashing, only log a warning if the original material slot is not found. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/47)

## v3.0.0
### Features
* New mode to merge meshes without introducing shader toggles.
  * When none of "Use Shader Toggles", "Write Properties as Static Values" or "Merge Different Property Materials" are enabled, the optimizer will no longer generate new shaders at all.
* Support for Android build target by using the above new mode. Also hides those options when the build target is Android.
* Add global setting "Always Optimize on Upload" that applies the default optimization settings to avatars uploaded without the optimizer component.
* Add preset buttons "Basic", "Shader Toggles" & "Full" to the optimizer.
* Remove useless layers from the FXLayer when "Optimize FX Layer" is enabled.
* Add option to combine motion time layers as a piecewise linear 1D tree into the direct blend tree as well.
* Show layer merge errors as tooltips in the "Show FX Layer Merge Results" section.
* Add option to disable phys bones together with the skinned mesh they affect.
* "Optimize FX Layer" can now also merge toggle layers that use two bools.
* Add support for `.orlshader` files from [orels-Unity-Shaders](https://github.com/orels1/orels-Unity-Shaders/tree/main)
* Add basic support for [Modular Avatar](https://github.com/bdunderscore/modular-avatar) by changing the callback order to -15 when Modular Avatar is detected.
* "Delete Unused Components" now deletes phys bones whose dependencies all get deleted as well.

### Changes
* "Merge Skinned Meshes" now only merges skinned meshes that get animated together.
  * Added "Use Shader Toggles" option to enable the old behavior.
* Deleted "Keep Material Animations Separate" option. It is now always enabled when "Use Shader Toggles" is enabled.
* Renamed "Merge Simple Toggles as BlendTree" to "Optimize FX Layer".
* "Write Properties as Static Values" is now forced on when "Use Shader Toggles" or "Merge Different Property Materials" is enabled.
* "Delete Unused GameObjects" is now turned off by default. Only the "Full" preset has it enabled.
* Remove "Merge Different Render Queue" option. It was mostly unused and you can simply make the render queues match manually.
* Small optimizations to the generated shaders:
  * Changed type of Texture2DArray index from int to float eliminating a type conversion. 
  * Use ternaries for merged material properties that have only 2 unique values.
  * No longer include unity cg include files, instead directly include only UnityLightingCommon.cginc to get the _SpecColor declaration.
  * Skip `#if` blocks in the output if the condition is known due to `shader_feature` pragmas.
  * Disable warnings 3557 & 4008 for the generated shaders as they happen quite a lot when constant folding happens.
  * Properties only write the actual property name and not the full display name.

### Bug Fixes
* Fix error message when trying to delete an unused AudioSource before its connected VRCSpatialAudioSource.
* Fix boneless skinned meshes ignoring the root bone property.
* Fix inline if return statements in the vertex & geometry shader not getting parsed correctly.
* Fix first mesh of a merged mesh block not checking if one of its parents has game object toggles.
* Fix `Texture2D<float4>` properties not getting parsed correctly.
* Fix material merge function only checking for material swaps in the candidate slot and not also the first slot of the existing list.
* Fix merged blendshapes not getting their initial weight set.
* Fix Marshmallow when "Delete Unused GameObjects" is enabled.

## v2.3.2
### Bug Fixes
* Fix animator copy when the animator is a sub asset. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/pull/43)
* Disable Create Optimized Copy button if the avatar has a VRCFury component. Also shows a warning explaining why. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/42)
* Fix warning spam in console from "unused" properties in the setting editor.

## v2.3.1
### Bug Fixes
* Fix tangents being 0 if a mesh with no tangents gets merged with one that has tangents.

## v2.3.0
### Features
* Added tooltips to all toggles and foldouts. They are identical to the descriptions in the readme.
* Added settings window to configure the default settings when adding the optimizer component to an avatar.

### Changes
* The title of the inspector now shows a shorter name when the window is too small to show the full name.

## v2.2.5
### Bug Fixes
* Fix crash when trying to create a file with invalid characters in the name. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/40)

## v2.2.4
### Bug Fixes
* Detect if a shader has instancing support and throw a parser exception if it has. This prevents the optimizer from trying to optimize shaders that it can't handle.

## v2.2.3
### Bug Fixes
* Fix crash with outdated VRC SDK. Disables the Optimize on Upload option and shows an error message instead. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/39)

## v2.2.2
### Bug Fixes
* Fix crash when the avatar has some viseme blendshapes set to `-none-`. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/38)

## v2.2.1
### Bug Fixes
* Fix error when trying to make Texture2DArray with mipmaps disabled.

## v2.2.0
### Features
* Add option to apply optimization on upload instead of creating a copy of the avatar.

### Changes
* Improved the warning text for locked in materials / unlocked materials depending on the optimization mode.
* Changed warnings for materials that can't merge to info boxes instead of warnings.
* Add TPS support for penetrator detection.
* No longer remove illegal components from the avatar.
* Move Profile Time Used to the top of the debug info section.

### Bug Fixes
* Fix crash when the avatar has multiple non skinned meshes that try to get merged together without any material that can get merged or no skinned mesh in the merge target. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/34)
* Fix parser not recognizing `#include` directives with `<>` instead of `""`. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/35)
* Fix optimizer ignoring `[gamma]` tags for properties. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/33)
* Add safeguards around bindpose & bone count mismatch. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/34)

## v2.1.1
### Bug Fixes
* Fix VRChat beta sdk incompatibility because of `GameObject.GetOrAddComponent()` moving namespaces.

## v2.1.0
### Features
* Add option to keep MMD blendshapes intact. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/32)

### Bug Fixes
* Apparently `sampler` is also a way to declare a `SamplerState`. So now the optimizer also replaces those.
* Parse and ignore const keyword in shader function parameters.

## v2.0.0
### Features
* Add option to merge simple toggles in the fx layer into one big direct blend tree.
* Optimizer is now a VPM & UPM compatible package.
* A crash in the optimize function will now show an error message that when clicked opens a link to the github issue tracker.
* Add option to merge blendshapes that get animated in a fixed ratio to each other.
* Remove blendshapes that only have animations animating them to 0 if the initial weight on the mesh is also 0.
* Show change in total blendshape count under the perf rank change section.
* Add warning if the avatar has extra animators. The optimizer only supports the custom playable layers.
* Add warning if the avatar has layer masks in the animators and the delete unused gameobjects option is enabled.

### Changes
* Texture Compression Analyzer now only shows SSIM by default to reduce clutter.
* Detect DPS penetrators and stop them from getting merged into other meshes.
* Merge static meshes as skinned meshes now only merges if it results in a decrease in material count.
* Foldouts can now be toggled by clicking on the label text too.
* Delete unused GameObjects is now disabled by default if the avatar has any layer masks in the animators.
* Remove `#pragma skip_optimizations` from the generated shaders.
* Progress bar updates continuously while shaders get parsed.

### Bug Fixes
* Fix ring finger to foot collider having half the size from VRChat update 2023.1.2.
* Fix crash when a material swap gets merged into a blob of meshes and its mesh index is non-zero. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/28)
* Fix always converting vertex colors to full float32 instead of using unorm8 if it was already unorm8.
* Fix crash when eye look blendshape IDs were greater or equal than the actual number of blendshapes.
* Fix jaw flap blendshape getting deleted.

## v1.10
### Features
* Add debug view to show all animated material property paths.
* Several small improvements to the generated shaders and mesh layouts reducing the amount of VRAM used by the optimized avatar.

### Changes
* Show original mesh paths in the `_IsActiveMesh` properties of merged materials.
* Don't inject animated property arrays for merged material properties where none of the original meshes had that property animated.
* Calculate min and max mesh id for each merged material and only add animated property arrays for that range.
* Only add the animated property based on the used mesh indices for the merged material.
* Interleave animated property arrays in cbuffer to reduce the required cbuffer size.
* Pack original meshID & materialID into uv0.z instead of having them separate in uv0.z & uv0.w. Miniscule VRAM savings. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/27)
* Call `Mesh.Optimize()` on the merged mesh to reduce the amount of memory it takes up. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/27)

### Bug Fixes
* Delete shader properties that start with _ShaderOptimizer from the optimized shader. This is to prevent Kaj shader optimizer from trying to optimize the shader again.
* Fix `RemoveIllegalComponents()` being broken when the sdk was imported via VCC.
* Change signaling value to NaN. This fixes animated properties interpolating the signal value weirdly.
* Create optimized swap materials after merging materials instead of before so that Texture2DArray and animated properties are correctly set.
* Deduplicate optimized swap materials with the optimized material that is already in the mesh renderer.
* Fix vertex shader not passing along mesh id to the geometry shader if the mesh id is disabled.

## v1.9
### Features
* Multiple performance improvements that reduce the time it takes to optimize an avatar by a factor of around 2x.

### Changes
* Instead of doing a `AssetDatabase.Refresh()` to import the optimized shaders it now uses `ImportAsset()` inside a `StartAssetEditing()` and `StopAssetEditing()` block. This saves a tiny bit of time.
* Replaced two heavily used regex with string and char operations. This saves a bit of time.
* Moved most of the generated shader code into include files. This saves a lot of time when importing the shaders.
* Strip `[Drawer]` attributes from the generated shader code. This saves a significant amount of time when assigning the shader to a material.

### Bug Fixes
* Fix that the optimizer would try to merge textures with different mip count.
* Fix crash when the avatar has a component with a missing script on it.

## v1.8
### Features
* Add info text if any normal maps are found that are not using BC5 compression.
* Add button to automatically convert all normal maps to BC5 compression.
* Add Texture Compression Analyzer under the `Tools/d4rkpl4y3r/Texture Compression Analyzer` menu.

### Changes
* The optimized animator controllers now have `(OptimizedCopy)` appended to their name.
* Add Limitations section to the readme to clarify that avatars might look broken if shaders are disabled. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/17)
* Slight blend shape download size reduction by setting delta values to 0 if they are below 1e-8.

### Bug Fixes
* Fix parents of animated transforms getting deleted. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/14)
* Always enable SkinnedMeshRenderer component that gets other meshes merged into it. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/15)
* Ignore Renderers that have no materials. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/19)
* Only add `#define KEYWORD` to passes that have a corresponding `#pragma shader_feature` in the pass. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/18)
* Fixed that multiple vert/geometry/fragment shader declarations would cause the optimizer to only use the last one.
* Add `Texture2D.GetDimensions(uint mipLevel, out float width, out float height, out float numberOfLevels)` and the `uint` variant overloads to the merged texture wrapper class. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/20)
* Fix assumed default vertex color to be white instead of black.
* Ignore Renderers that have no mesh. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/22)
* Vector4 properties now use G formatting instead of the default unity F1 formatting. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/21)

## v1.7
### Bug Fixes
* Fix optimizer not working with VRCSDK3-AVATAR-2022.10.12.21.07 SDK onwards. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/12)

## v1.6
### Features
* Optimizer now shows a progress bar dialog when optimizing an avatar.
* Add info box if build target is Android to tell users that the optimizer doesn't support Quest avatars.
* Add validation check that checks if the optimizer got added to an optimized copy.
* Add validation error if the amount of base playable layers isn't 5.

### Bug fixes
* Fix extra out parameters in the fragment shader not getting added to the function declaration. [(more)](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/10)
* Add ParserException if any parameter in vertex or fragment can't be parsed.
* Fix parsing of template types to permit spaces between the type and the template brackets.
* Fix new lines in string literals not being the environment's new line character. This caused shader warning spam on poi shader.
* Fix default texel size to 4 instead of 8. This matches the sizes of the default textures as checked with nvidia nsight.
* Fix non float params not working with "Keep Material Animations Separate" option.

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
