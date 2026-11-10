using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using Vortice;
using Vortice.Direct2D1;

namespace LyricReplica.App.Reference;

[Guid("E393A868-D3BD-4983-A9E8-860D7D6A8926")]
public sealed class IsolationShaderEffect : CustomEffectBase,ID2D1DrawTransform
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Constants
    {
        public Vector2 Resolution;
        public float RandomValue1;
        public int UseHSVBlending;
        public Vector3 Color1;
        public float RandomValue2;
        public Vector3 Color2;
        public float RandomValue3;
        public Vector3 Color3;
        public float iTime;
        public Vector3 Color4;
        public int EnableLightWave;
        public float Dimming;
        public float DitherStrength;
        public Vector2 Padding;
    }
    private Constants _constants=new(){Resolution=new(1280,720),DitherStrength=1};
    private ID2D1DrawInfo? _draw;
    [CustomEffectProperty(PropertyType.Float,0)] public float RandomValue1{get=>_constants.RandomValue1;set{_constants.RandomValue1=value;Push();}}
    [CustomEffectProperty(PropertyType.Float,1)] public float RandomValue2{get=>_constants.RandomValue2;set{_constants.RandomValue2=value;Push();}}
    [CustomEffectProperty(PropertyType.Float,2)] public float RandomValue3{get=>_constants.RandomValue3;set{_constants.RandomValue3=value;Push();}}
    [CustomEffectProperty(PropertyType.Float,3)] public float iTime{get=>_constants.iTime;set{_constants.iTime=value;Push();}}
    [CustomEffectProperty(PropertyType.Bool,4)] public bool EnableLightWave{get=>_constants.EnableLightWave!=0;set{_constants.EnableLightWave=value?1:0;Push();}}
    [CustomEffectProperty(PropertyType.Vector2,5)] public Vector2 Resolution{get=>_constants.Resolution;set{_constants.Resolution=value;Push();}}
    [CustomEffectProperty(PropertyType.Vector3,6)] public Vector3 Color1{get=>_constants.Color1;set{_constants.Color1=value;Push();}}
    [CustomEffectProperty(PropertyType.Vector3,7)] public Vector3 Color2{get=>_constants.Color2;set{_constants.Color2=value;Push();}}
    [CustomEffectProperty(PropertyType.Vector3,8)] public Vector3 Color3{get=>_constants.Color3;set{_constants.Color3=value;Push();}}
    [CustomEffectProperty(PropertyType.Vector3,9)] public Vector3 Color4{get=>_constants.Color4;set{_constants.Color4=value;Push();}}
    [CustomEffectProperty(PropertyType.Float,10)] public float Dimming{get=>_constants.Dimming;set{_constants.Dimming=value;Push();}}
    [CustomEffectProperty(PropertyType.Float,11)] public float DitherStrength{get=>_constants.DitherStrength;set{_constants.DitherStrength=value;Push();}}
    public override void Initialize(ID2D1EffectContext context,ID2D1TransformGraph graph)
    {
        using var source=Application.GetResourceStream(new Uri("pack://application:,,,/LyricReplica;component/Assets/Shaders/effectwithdithering.ps")).Stream;
        using var memory=new MemoryStream();source.CopyTo(memory);byte[] code=memory.ToArray();
        context.LoadPixelShader(typeof(IsolationShaderEffect).GUID,code,code.Length);graph.SetSingleTransformNode(this);base.Initialize(context,graph);
    }
    public int GetInputCount()=>0;
    public void SetDrawInfo(ID2D1DrawInfo drawInfo){_draw=drawInfo;drawInfo.SetPixelShader(typeof(IsolationShaderEffect).GUID,(PixelOptions)0);Push();}
    public void MapOutputRectToInputRects(RawRect outputRect,RawRect[] inputRects){}
    public void MapInputRectsToOutputRect(RawRect[] inputRects,RawRect[] inputOpaqueSubRects,out RawRect outputRect,out RawRect outputOpaqueSubRect)
    {outputRect=new(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue);outputOpaqueSubRect=default;}
    public RawRect MapInvalidRect(int inputIndex,RawRect invalidInputRect)=>new(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue);
    private void Push(){_draw?.SetPixelShaderConstantBuffer(in _constants);}
    internal void ReleaseDrawInfo()=>_draw=null;
}
