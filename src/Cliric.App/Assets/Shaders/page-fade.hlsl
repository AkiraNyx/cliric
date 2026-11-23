// fxc /T ps_2_0 /E main /Fo page-fade.ps page-fade.hlsl
sampler2D foreground : register(s0);
float3 kernel : register(c0); // paired offset, center weight, paired weight
float opacity : register(c1);
float4 texelStep : register(c2);

float4 blurRow(float2 uv, float2 offset)
{
    return kernel.y * tex2D(foreground, uv)
         + kernel.z * (tex2D(foreground, uv - offset) + tex2D(foreground, uv + offset));
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 x = kernel.x * texelStep.xy;
    float2 y = kernel.x * texelStep.zw;
    return opacity * (kernel.y * blurRow(uv, x) + kernel.z * (blurRow(uv - y, x) + blurRow(uv + y, x)));
}
