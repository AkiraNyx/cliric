using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LyricReplica.Core;

namespace LyricReplica.App.Reference;

public sealed class ReferencePlusLighterEffect : ShaderEffect
{
    private static readonly PixelShader StandardShader=Load("pluslightershader.ps"),MaxWhiteShader=Load("pluslightermaxwhiteshader.ps");
    public static readonly DependencyProperty InputProperty=RegisterPixelShaderSamplerProperty(nameof(Input),typeof(ReferencePlusLighterEffect),0);
    public static readonly DependencyProperty OverlayAlphaProperty=DependencyProperty.Register(nameof(OverlayAlpha),typeof(double),typeof(ReferencePlusLighterEffect),new UIPropertyMetadata(1d,PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty AlphaProperty=DependencyProperty.Register(nameof(Alpha),typeof(double),typeof(ReferencePlusLighterEffect),new UIPropertyMetadata(.95,PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty BackgroundBoostProperty=DependencyProperty.Register(nameof(BackgroundBoost),typeof(double),typeof(ReferencePlusLighterEffect),new UIPropertyMetadata(1d,PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty IntrusionProperty=DependencyProperty.Register(nameof(Intrusion),typeof(double),typeof(ReferencePlusLighterEffect),new UIPropertyMetadata(1.6,PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty OverflowCompressionProperty=DependencyProperty.Register(nameof(OverflowCompression),typeof(double),typeof(ReferencePlusLighterEffect),new UIPropertyMetadata(1d,PixelShaderConstantCallback(4)));
    public Brush Input{get=>(Brush)GetValue(InputProperty);set=>SetValue(InputProperty,value);}
    public double OverlayAlpha{get=>(double)GetValue(OverlayAlphaProperty);set=>SetValue(OverlayAlphaProperty,value);}
    public double Alpha{get=>(double)GetValue(AlphaProperty);set=>SetValue(AlphaProperty,value);}
    public double BackgroundBoost{get=>(double)GetValue(BackgroundBoostProperty);set=>SetValue(BackgroundBoostProperty,value);}
    public double Intrusion{get=>(double)GetValue(IntrusionProperty);set=>SetValue(IntrusionProperty,value);}
    public double OverflowCompression{get=>(double)GetValue(OverflowCompressionProperty);set=>SetValue(OverflowCompressionProperty,value);}
    public ReferencePlusLighterEffect():this(false){}
    public ReferencePlusLighterEffect(bool maxWhite)
    {
        PixelShader=maxWhite?MaxWhiteShader:StandardShader;
        foreach(var property in new[]{InputProperty,OverlayAlphaProperty,AlphaProperty,BackgroundBoostProperty,IntrusionProperty,OverflowCompressionProperty})UpdateShaderValue(property);
    }
    public static ReferencePlusLighterEffect ForPreset(PlusLighterEffectPresetTypes preset)=>new(preset==PlusLighterEffectPresetTypes.Android)
    {
        OverlayAlpha=1,Alpha=preset switch{PlusLighterEffectPresetTypes.iOS26=>.92,PlusLighterEffectPresetTypes.iOS18=>.76,_=>1},
        BackgroundBoost=preset switch{PlusLighterEffectPresetTypes.iOS26=>.55,PlusLighterEffectPresetTypes.iOS18=>.76,_=>1},
        Intrusion=1.6,OverflowCompression=preset==PlusLighterEffectPresetTypes.iOS18?1:0
    };
    private static PixelShader Load(string name)
    {var shader=new PixelShader{UriSource=new Uri("pack://application:,,,/LyricReplica;component/Assets/Shaders/"+name)};shader.Freeze();return shader;}
}
