# Guidelines for Shader Authors
Here I will outline a couple things shader authors should consider if they want their shaders to be compatible with d4rkAvatarOptimizer.

## General unsupported features that get detected
These features are unsupported but are detected by the analyzer marking the shader incompatible:
* `UsePass` statements
* `UNITY_INSTANCING_BUFFER_START` macros
* Surface shaders
* Tessellation shaders
* Miss matched amount of opening and closing curly braces
  * This can happen when using preprocessor macros that open or close curly braces.

## Things that can break the optimizer silently
Things in here might not get caught by the shader analyzer resulting in the optimizer trying to optimize the shader but failing to do so correctly.

Generally try to avoid using preprocessor macros as much as possible. They can hide critical code structure from the parser.
### Property Declarations  
Especially avoid hiding property declarations behind macros as that *will* break when writing properties as static values.
For example, do NOT do this:
```c
// VERY BAD, DO NOT DO THIS
#define MY_DECLARE_COLOR_PROPERTY(name) float4 name;
MY_DECLARE_COLOR_PROPERTY(_MyColor)
```
A common use case for this is declaring texture properties with macros. Instead of doing this:
```c
// DO NOT DO THIS
#ifdef SOME_PLATFORM_DEFINE
    #define MY_DECLARE_TEXTURE_PROPERTY(name) sampler2D name; float4 name##_ST;
#else
    #define MY_DECLARE_TEXTURE_PROPERTY(name) Texture2D name; SamplerState sampler##name; float4 name##_ST;
#endif
MY_DECLARE_TEXTURE_PROPERTY(_MyTexture)
```
Do this:
```c
// GOOD
#ifdef SOME_PLATFORM_DEFINE
    sampler2D _MyTexture;
    float4 _MyTexture_ST;
#else
    Texture2D _MyTexture;
    SamplerState sampler_MyTexture;
    float4 _MyTexture_ST;
#endif
```
Besides the direct hlsl declarations you can also use the unity macros `UNITY_DECLARE_TEX2D` and `UNITY_DECLARE_TEX2D_NOSAMPLER` as they have hardcoded support in the optimizer.

### Geometry shader streams
Don't pass the geometry output streams to other functions. The optimizer needs to see the `.Append()` calls directly in the geometry shader function to be able to inject its wrapper struct properly. This means you should also not hide the `.Append()` calls behind macros.

### Vertex Data and Function Signatures
Other critical places are the structs for vertex data and the vertex, geometry & fragment function signatures. I need to pass along a combine mesh/material id from uv0.z to the later shader stages. This requires me to modify those structs and function signatures which can easily break when the optimizer isn't able to see the full structure.

As with the property declarations, don't hide vertex data struct member declarations behind macros. Also don't hide vertex or fragment function parameters behind macros. Using `#ifdef` blocks inside the struct/parameter list is fine.  
For example, do NOT do this:
```c
// VERY BAD, DO NOT DO THIS
#ifdef SOME_PLATFORM_DEFINE
    #define FACING_PARAM(facing) , uint facing : SV_IsFrontFace
    #define UV_PARAM(uvIndex) float2 uv##uvIndex : TEXCOORD##uvIndex;
#else
    #define FACING_PARAM(facing)
    #define UV_PARAM(uv)
#endif
struct VS_OUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    UV_PARAM(uv1)
    UV_PARAM(uv2)
    UV_PARAM(uv3)
};
float4 frag(VS_OUT input FACING_PARAM(isFrontFace)) : SV_Target
{ ... }
```
Instead do this:
```c
// GOOD
struct VS_OUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    #ifdef SOME_PLATFORM_DEFINE
        float2 uv1 : TEXCOORD1;
        float2 uv2 : TEXCOORD2;
        float2 uv3 : TEXCOORD3;
    #endif
};
float4 frag(VS_OUT input
    #ifdef SOME_PLATFORM_DEFINE
        , uint isFrontFace : SV_IsFrontFace
    #endif
) : SV_Target
{ ... }
```

## Marking Shaders as Incompatible
If your shader is not compatible with d4rkAvatarOptimizer, you can explicitly mark it as incompatible by adding the comment `//d4rkAO:incompatible_shader` anywhere in the shader code. This will prevent d4rkAvatarOptimizer from attempting to optimize materials using this shader.  
As with other incompatible shaders, this means that there is no `Write Properties as Static Values` happening for those shaders and as such the features `Shader Toggles` and `Merge Different Property Materials` will not work either. All other optimizations will still properly apply.

## Requiring Constant Properties
If your shader has properties that must remain constant for the shader to function correctly, you can mark these properties as required constant by adding the comment `//d4rkAO:require_constant(_PropertyName)`.  
The preprocessor value of `OPTIMIZER_ENABLED` will be defined in the generated shader if any required constant properties are declared.
This is useful in cases where inline replacement of the value is necessary like for geometry shader instance count:
```c
//d4rkAO:require_constant(_GSInstanceCount)
#if defined(OPTIMIZER_ENABLED)
    [instance(_GSInstanceCount)]
#else
    [instance(32)]
#endif
void geom(triangle VS_OUT input[3], inout TriangleStream<PS_IN> triStream, uint instanceID : SV_GSInstanceID)
{
    #if !defined(OPTIMIZER_ENABLED)
        if (instanceID >= _GSInstanceCount) return;
    #endif
    // [...]
}
```

## Using Ifex Conditions
You can wrap parts of you shader in `//ifex CONDITION` and `//endex` comments to have d4rkAvatarOptimizer include or exclude those parts based on the condition. This works even outside of the hlsl code which makes it useful for excluding entire shader passes like an optional outline pass.
```c
//d4rkAO:require_constant(_EnableOutlines)
//ifex _EnableOutlines!=1
Pass
{
    Name "Outline"
    Tags { "LightMode" = "ForwardBase" }
    // [...]
}
//endex
```
In this example the entire outline pass will be excluded from the optimized shader if the property `_EnableOutlines` is not set to `1` (true) on the material. Unlike in poi ifex automatically also checks if the value is animated or a multi property from `Merge Different Property Materials`. In those cases all comparisons to values will return false as it could be different values at runtime.
So for this above case we need to tell the optimizer that the property needs to be constant.