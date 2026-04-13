# Tips and info for advanced users
Here I will note down more specific details on how the optimizer works and changes in workflow to use it to its fullest extend.

## Switching from poiyomi lock in workflow to optimizer shader toggles
Keep materials unlocked with shader toggles. The `Write Properties as Static Values` option is very similar to what poi lock in does and is forced when using shader toggles.

### UV tile discards vs shader toggles
When using poi a common workflow includes using uv tile discards to toggle individual parts of a mesh on and off. The optimizer has a similar system of shader based toggles that requires less manual work. Instead of using uv tile discards, you can split your parts up into meshes and make animations that simply toggle the meshes on and off. The shader toggles option will then merge those meshes again and change the animations to use the generated shader toggles instead.

### Property animated tag
In poi lock in workflow you have to manually mark properties that should not get locked in as animated.

The optimizer does ignore the animated tags and instead looks at all the animations in the animators on build time. This means much less work keeping the animated tags in sync with your animations as you can just skip that part entirely.

This does mean that if your setup contains one material in a mesh that has something marked as animated, but the other materials on the same mesh have that same property not marked as animated, then the optimizer will treat all of those materials as animated and not lock in that property for any of them!  
See [issue 176](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/176) for an example setup that wont work with the optimizer because of this.

### Property rename animated tag
Just like with the animated tag, the optimizer ignores all rename animated tags. Your current animations will not work unless you lock in these materials with poi lock in beforehand.

Instead of supporting RA the optimizer has its own system to keep material properties when they get animated from different meshes the same after merging. So when doing the shader toggles optimizer workflow you instead split materials that need different animations into their own mesh and animate them there. At build time everything should still get merged back together while adding code to the shader to ensure the properties from different source meshes stay intact. All of this happens automatically without need to manually mark properties anywhere.  
See [issue 182](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues/182) for extra context.