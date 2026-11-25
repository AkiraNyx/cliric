using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Lyricify.Backgrounds;
using Lyricify.Backgrounds.AppleMusicInspired.Ios;
using Lyricify.Backgrounds.AppleMusicInspired.Windows.Rendering;

namespace Cliric.App;

internal static class ReferenceAppleMusicBackground
{
    private const string RendererAssembly = "Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf";
    private static readonly string Payload = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../reference-release-4.4.2/payload"));
    private static readonly Dictionary<string, string> VerifiedComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        [RendererAssembly] = "BCCC098CCDEB8AEE64A0232F1DEC3A88666162899F7FDCA65AA06E963E5ECE1D",
        ["Lyricify.Backgrounds.AppleMusicInspired.Ios.Shared"] = "6D9D454E9C374ECDA62B564E74B3829A48E4C5B5CD73B8D7AB93874D2408A400",
        ["Lyricify.Backgrounds.Hosting.Win32"] = "038519C52A17393037B28C649163A45A4956105AC1DC7EF2E9D4A5F1B5122AF7",
        ["Lyricify.Backgrounds.Hosting.Wpf"] = "15A73531228986F3D370E251D1CF86F1E6656ADC8409E738041828833B5F65A9"
    };
    private static readonly AssemblyLoadContext RendererContext = new ReferenceContext();

    internal static bool Available => VerifiedComponents.Keys.All(name => File.Exists(AssemblyPath(name)));

    internal static IBackgroundSession Create(AppleMusicIosBackgroundSettings settings, bool lightTheme, bool independent, int presetSlot = -1)
    {
        if (!Available)
            throw new InvalidOperationException("缺少本地 4.4.2 背景组件。");
        var assembly = RendererContext.LoadFromAssemblyName(new AssemblyName(RendererAssembly));
        string typeName = independent ? "IndependentAppleMusicIosBackground" : "AppleMusicIosBackground";
        var type = assembly.GetType(RendererAssembly + "." + typeName, throwOnError: true)!;
        // The release renderer calls Start even with the pulse set to zero; the host audio helper honors opt-out.
        Func<string?>? audio = settings.BassPulseScale <= 0 ? () => AppleMusicSpectrumAnalysis.DisabledAudioEndpoint : null;
        return (IBackgroundSession)Activator.CreateInstance(type, settings, lightTheme, null, null, audio, presetSlot)!;
    }

    private static string AssemblyPath(string name) => Path.Combine(Payload, name + ".dll");

    private sealed class ReferenceContext() : AssemblyLoadContext("Cliric reference background (4.4.2)")
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Keep the shared session/settings contract in the host so the renderer can be cast directly.
            if (assemblyName.Name is not { } name || !VerifiedComponents.TryGetValue(name, out string? expected))
                return null;
            string path = AssemblyPath(name);
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("本地背景组件与 4.4.2 版本不符。");
            stream.Position = 0;
            return LoadFromStream(stream);
        }
    }
}
