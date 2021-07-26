# d4rkAvatarOptimizer
d4rkpl4y3rs VRChat avatar 3.0 optimizer that aims to reduce skinned mesh & material count.
## How To Use
Add the d4rkAvatarOptimizer component to your avatar root. It should go on the same object that your VRC Avatar Descriptor is on.  
You should then see something like this:

![Example Screenshot](./ExampleImages/example0.png)
### Write properties as static values
When enabled the optimizer will replace the uniform parameter definitions with a static value on all materials.  
For example `uniform float4 _Color;` will get changed to `static float4 _Color = float4(1, 0, 1, 1);`  
This enables the shader compiler to do more [constant folding](https://en.wikipedia.org/wiki/Constant_folding) and thus making the shader run faster.  
Unfortunately the shader compiler is allowed to ignore NaN's while doing that so if a shader is not made with that in mind this might cause some issues.
### Merge skinned meshes
The optimizer tries to merge different skinned meshes together.
If some of those skinned mesh game objects get toggled with animations in the fxlayer it will add logic to the shader to toggle those sub meshes in shader instead.
Skinned meshes that are on different layers (eg UIMenu) from each other will not get merged.  
Can't merge meshes that have any tessellation or surface shaders.  
**Will break material swap animations.**
### Keep material animations separate
This makes sure that animated properties from one mesh don't animate the property on materials from a different mesh if their meshes got merged.
Can break since it creats a lot of constant buffer variables.
### Merge different property materials
Merges materials with the same shader where properties can have different values.
If they do have different values the values will get written to a constant buffer.
Material IDs get written to uv.w and used to access the correct value from that cbuffer.

Can't merge materials if:
* Shader is surface shader or has tessellation
* A property that differs is used in shader lab code (eg `ZWrite [_ZWrite]`)
### Merge same dimension textures
Merges materials if they use different textures if their with, height & compression format match.
Creates a Texture2DArray from the original textures.

Can't merge materials if:
* Shader has *any* function that takes Texture2D or sampler2D as input  
  eg `float3 triplanar(float3 pos, float3 normal, sampler2D sampl, float4 st)`
* Texture property to merge gets used in custom macro. **This is not detected by the optimizer!**
### Merge cull back with cull off
Merges materials even if their culling properties differ. Forces culling to off.
### Profile time used
Outputs how much time the different sections in the code took to execute.
### Create optimized copy
Creates a copy of the avatar and performs the selected optimizations on the copy.
Disables the original avatar so only the copy is active.  
None of the original assets will be changed so even if the optimizer fails your avatar is still safe!

In addition to the selected optimizations there are some optimizations that are always performed:
* Remove unused shape keys. Unused here means not visemes nor referenced in any animation in the fx layer.
* Merge identical material slots on skinned meshes.
* Add dummy animation to animator states that have no animation specified.
* Remove illegal avatar components.
