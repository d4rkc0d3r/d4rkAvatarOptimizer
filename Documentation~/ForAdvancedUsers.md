# Tips and info for advanced users
Here I will note down more specific details on how the optimizer works and changes in workflow to use it to its fullest extent.

## Tradeoffs of the Basic preset
The Basic preset is designed to be conservative and not change behavior of your avatar.  
However there are a couple things it does not consider breakage as otherwise almost no optimizations would be possible.
1. SkinnedMeshRenderer root bone & probe anchor are ignored when merging.  
   When merging meshes it will try to pick the "best" root bone and probe anchor from the source meshes:
   - The most common root bone/probe anchor among the source meshes
   - On tie it picks in order: humanoid > some parent is a humanoid > any specified > first mesh transform
2. Worlds might animate anything on an avatar via stations. This is ignored as following this would require preserving the hierarchy as is. No deleting unused anything, no merging anything, no reordering anything. This would make the optimizer basically useless.  
   The one exception to this is MMD worlds which are supported by the `MMD Compatibility` setting.
3. `SV_VertexID` & `SV_PrimitiveID` in shaders is not guaranteed to stay the same after optimization. Not ignoring this would require never merging any meshes/materials which would defeat the point of the optimizer.  
   If one of your effects relies on stable vertex/primitive ids you have to put that mesh in the `Exclusions` list.
4. Mismatched vertex attributes between meshes. This isn't breakage per se but it can increase mesh size.  
   For example if one of your meshes has 3 different uv sets but the others only have 1, then after getting merged the resulting mesh will have 3 uv sets.  
   The optimizer will copy the last existing uv set for the meshes that don't have as many into the extra slots to match shader behavior when querying non existing uv sets.

### Automatic exclusions
If you are a prefab creator and one of your prefabs breaks due to any of the above reasons, contact me. I have an automatic exclusion system in place and we can work to either support the prefab outright or add detection for it.  
Currently these get automatically excluded:
- DPS/TPS/SPS Penetrator Mesh
- Real Kiss System Mesh
- `_VirtualLens_Root` from Virtual Lens

Additionally, the optimizer automatically excludes everything that gets animated by any animator thats not on the avatar root.  
It only supports the animator controllers specified in the vrc avatar descriptor for all the animation rewriting logic.

## Log file
The optimizer creates a log file named `_d4rkAvatarOptimizer.log` next to the other optimizer generated assets.  
You can quickly open it by pressing the gear wheel settings button in the top left of the optimizer inspector and then the button with the text file icon in the settings window.

The log is organized by indentation level so I highly recommend using a text editor that supports folding to view it.

## Switching from poiyomi lock in workflow to optimizer shader toggles
Keep materials unlocked with shader toggles. The `Write Properties as Static Values` option is very similar to what poi lock in does and is forced when using shader toggles.

### uv tile discards vs shader toggles
When using poi a common workflow includes using uv tile discards to toggle individual parts of a mesh on and off. The optimizer has a similar system of shader based toggles that requires less manual work. Instead of using uv tile discards, you can split your parts up into meshes and make animations that simply toggle the meshes on and off. The shader toggles option will then merge those meshes again and change the animations to use the generated shader toggles instead.

### Property animated tag
In poi lock in workflow you have to manually mark properties that should not get locked in as animated.

The optimizer does ignore the animated tags and instead looks at all the animations in the animators on build time. This means much less work keeping the animated tags in sync with your animations as you can just skip that part entirely.

This does mean that if your setup contains one material in a mesh that has something marked as animated, but the other materials on the same mesh have that same property not marked as animated, then the optimizer will treat all of those materials as animated and not lock in that property for any of them!  
See [issue 176](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/176) for an example setup that won't work with the optimizer because of this.

### Property rename animated tag
Just like with the animated tag, the optimizer ignores all rename animated tags. Your current animations will not work unless you lock in these materials with poi lock in beforehand.

Instead of supporting RA the optimizer has its own system to keep material properties when they get animated from different meshes the same after merging. So when doing the shader toggles optimizer workflow you instead split materials that need different animations into their own mesh and animate them there. At build time everything should still get merged back together while adding code to the shader to ensure the properties from different source meshes stay intact. All of this happens automatically without need to manually mark properties anywhere.  
See [issue 182](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/182) for extra context.