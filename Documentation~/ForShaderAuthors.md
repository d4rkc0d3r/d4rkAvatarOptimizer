# Guidelines for Shader Authors

This document outlines a few things shader authors should consider if they want their shaders to be compatible with **d4rkAvatarOptimizer**.

## Generally unsupported features (detected)
These features are unsupported and are detected by the analyzer, which will mark the shader as incompatible:

* `UsePass` statements
* `UNITY_INSTANCING_BUFFER_START` macros
* Surface shaders
* Tessellation shaders
* Mismatched numbers of opening and closing curly braces
  * This can happen when using preprocessor macros that open or close curly braces.

## Things that can break the optimizer silently
Items in this section may not get caught by the shader analyzer. This can result in the optimizer attempting to optimize the shader, but failing to do so correctly.

In general, try to avoid using preprocessor macros as much as possible. They can hide critical code structure from the parser.

### Property declarations
In particular, avoid hiding property declarations behind macros—this *will* break when writing properties as static values.  
For example, do **not** do this:

```c
// VERY BAD, DO NOT DO THIS
#define MY_DECLARE_COLOR_PROPERTY(name) float4 name;
MY_DECLARE_COLOR_PROPERTY(_MyColor)
```

A common use case is declaring texture properties with macros. Instead of doing this:

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

Besides direct HLSL declarations, you can also use the Unity macros `UNITY_DECLARE_TEX2D` and `UNITY_DECLARE_TEX2D_NOSAMPLER`, as they have hardcoded support in the optimizer.

### Geometry shader streams
Do not pass geometry output streams to other functions. The optimizer needs to see the `.Append()` calls directly in the geometry shader function to inject its wrapper struct properly. This also means you should not hide the `.Append()` calls behind macros.

### Vertex data and function signatures
Other critical places include the structs for vertex data and the vertex/geometry/fragment function signatures. The optimizer needs to pass a combined mesh/material ID from `UV0.z` to later shader stages. This requires modifying those structs and function signatures, which can break if the optimizer cannot see the full structure.

As with property declarations, do not hide vertex data struct member declarations behind macros. Also do not hide vertex or fragment function parameters behind macros. Using `#ifdef` blocks inside the struct/parameter list is fine.

For example, do **not** do this:

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

Instead, do this:

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

## Marking shaders as incompatible
If your shader is not compatible with d4rkAvatarOptimizer, you can explicitly mark it as incompatible by adding the comment `//d4rkAO:incompatible_shader` anywhere in the shader code. This prevents d4rkAvatarOptimizer from attempting to optimize materials using this shader.

As with other incompatible shaders, this means **Write Properties as Static Values** will not run for those shaders, and therefore the features **Shader Toggles** and **Merge Different Property Materials** will not work either. All other optimizations will still apply.

## Requiring constant properties
If your shader has properties that must remain constant for it to function correctly, you can mark these properties as required constants by adding the comment `//d4rkAO:require_constant(_PropertyName)`.

The preprocessor symbol `OPTIMIZER_ENABLED` will be defined in the generated shader if any required constant properties are declared.

This is useful in cases where inline replacement of the value is necessary, such as a geometry shader instance count:

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

## Using Ifex conditions
You can wrap parts of your shader in `//ifex CONDITION` and `//endex` comments to have d4rkAvatarOptimizer include or exclude those parts based on the condition. This works even outside of the HLSL code, which makes it useful for excluding entire shader passes (for example, an optional outline pass).

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

In this example, the entire outline pass will be excluded from the optimized shader if the property `_EnableOutlines` is not set to `1` (true) on the material.

Unlike in Poiyomi ifex, **Ifex** here automatically also checks whether the value is animated or comes from **Merge Different Property Materials**. In those cases, all comparisons to values will return false, because the value could differ at runtime.

So for the example above, we need to tell the optimizer that the property must be constant.