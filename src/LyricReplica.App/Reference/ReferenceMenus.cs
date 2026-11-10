using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LyricReplica.App.Reference;

public static class ReferenceMenus
{
    public static void Apply(ContextMenu menu)
    {
        menu.SetResourceReference(FrameworkElement.StyleProperty,"ReferenceContextMenu");
        RefreshTheme();menu.Opened+=(_,_)=>RefreshTheme();
        void RefreshTheme()
        {
            // Closed popups are detached from the window's resource invalidation tree.
            foreach(string key in new[]{"TextFillColorPrimaryBrush","TextFillColorDisabledBrush","SystemFillColorSolidNeutralBackgroundBrush","SubtleFillColorSecondaryBrush","ControlFillColorDefaultBrush","ControlElevationBorderBrush","FluentSystemIcons"})
                menu.Resources[key]=Application.Current.Resources[key];
        }
    }

    public static void UpdateTheme(bool light)
    {
        var resources=Application.Current.Resources;
        Set("TextFillColorPrimary",light?"#E4000000":"#FFFFFFFF");
        Set("TextFillColorDisabled",light?"#5C000000":"#5DFFFFFF");
        Set("SystemFillColorSolidNeutralBackground",light?"#FFF3F3F3":"#FF2E2E2E");
        Set("SubtleFillColorSecondary",light?"#09000000":"#0FFFFFFF");
        Set("ControlFillColorDefault",light?"#B3FFFFFF":"#0FFFFFFF");
        var stroke=new LinearGradientBrush{MappingMode=BrushMappingMode.Absolute,StartPoint=new Point(0,0),EndPoint=new Point(0,3)};
        stroke.GradientStops.Add(new GradientStop(ColorOf(light?"#29000000":"#18FFFFFF"),.33));
        stroke.GradientStops.Add(new GradientStop(ColorOf(light?"#0F000000":"#12FFFFFF"),1));
        if(light)stroke.RelativeTransform=new ScaleTransform(1,-1);
        resources["ControlElevationBorderBrush"]=stroke;
        resources["FluentSystemIcons"]=new FontFamily(new Uri("pack://application:,,,/"),"./Assets/Fonts/#FluentSystemIcons-Regular");
        void Set(string name,string value)=>resources[name+"Brush"]=new SolidColorBrush(ColorOf(value));
        static Color ColorOf(string value)=>(Color)ColorConverter.ConvertFromString(value);
    }
}
