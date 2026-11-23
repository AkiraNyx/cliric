// fxc /T ps_2_0 /E main /Fo page-fade.ps page-fade.hlsl
sampler2D foreground : register(s0);
float3 pose : register(c0);

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 x = float2(pose.x, 0);
    float2 y = float2(0, pose.y);
    float4 center = tex2D(foreground, uv);
    float4 corners = tex2D(foreground, uv - x - y) + tex2D(foreground, uv + x - y)
                   + tex2D(foreground, uv - x + y) + tex2D(foreground, uv + x + y);
    return pose.z * (center * .5 + corners * .125);
}
