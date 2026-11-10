using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LyricReplica.App.Reference;

namespace LyricReplica.App;

internal static class IsolationEffectChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("isolation-original.json")!;
        using var fixture=JsonDocument.Parse(stream);
        var original=fixture.RootElement.EnumerateArray().Single(t=>t.GetProperty("type").GetString()!.EndsWith("IsolationEffectWithDithering"));
        check(Marshal.SizeOf<IsolationShaderEffect.Constants>()==original.GetProperty("constantBytes").GetInt32(),"original Isolation constant buffer size");
        foreach(var field in original.GetProperty("fields").EnumerateArray())
            check(Marshal.OffsetOf<IsolationShaderEffect.Constants>(field.GetProperty("name").GetString()!).ToInt32()==field.GetProperty("offset").GetInt32(),"original Isolation field layout");
        using var effect=new IsolationShaderEffect();
        foreach(var property in original.GetProperty("properties").EnumerateArray())
        {
            string name=property.GetProperty("name").GetString()!;var member=typeof(IsolationShaderEffect).GetProperty(name)!;
            var attribute=member.CustomAttributes.Single(a=>a.AttributeType.Name=="CustomEffectPropertyAttribute");
            var arguments=property.GetProperty("constructor").EnumerateArray().ToArray();
            check(attribute.ConstructorArguments.Count==2&&Convert.ToInt32(attribute.ConstructorArguments[0].Value)==int.Parse(arguments[0].GetProperty("value").GetString()!)&&Convert.ToInt32(attribute.ConstructorArguments[1].Value)==int.Parse(arguments[1].GetProperty("value").GetString()!),"original Isolation property type and index "+name);
            object? value=member.GetValue(effect);var expected=property.GetProperty("default");
            bool equal=value switch
            {
                System.Numerics.Vector2 v=>v.X==expected[0].GetSingle()&&v.Y==expected[1].GetSingle(),
                System.Numerics.Vector3 v=>v.X==expected[0].GetSingle()&&v.Y==expected[1].GetSingle()&&v.Z==expected[2].GetSingle(),
                bool v=>v==expected.GetBoolean(),_=>Convert.ToSingle(value)==expected.GetSingle()
            };
            check(equal,"original Isolation default "+name);
        }
        check(effect.GetInputCount()==0,"Isolation is a generated background without image samplers");
    }
}
