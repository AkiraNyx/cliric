#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$env:DOTNET_CLI_HOME = Join-Path $root 'tools/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root 'tools/nuget'
$dotnet = Join-Path $root 'tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = (Get-Command dotnet).Source }
& (Join-Path $root 'scripts/Setup-LiquidGlass.ps1')
$output = Join-Path $root 'artifacts/capsule-player-demo-fade'
$build = Join-Path $root 'artifacts/capsule-demo-build'
& $dotnet publish (Join-Path $PSScriptRoot 'CapsulePlayerDemo.csproj') -c Release -r win-x64 --self-contained true --artifacts-path $build -o $output -v:q
if ($LASTEXITCODE -ne 0) { throw 'Preview build failed.' }

$media = Join-Path $output 'DemoMedia'
New-Item -ItemType Directory -Path $media -Force | Out-Null
Add-Type -Namespace CapsuleDemo -Name SampleAudio -MemberDefinition @'
public static void Write(string path, int duration, double root, int tempo)
{
    const int rate = 16000;
    using var file = System.IO.File.Create(path);
    using var writer = new System.IO.BinaryWriter(file);
    int samples = rate * duration;
    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + samples * 2);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(rate);
    writer.Write(rate * 2);
    writer.Write((short)2);
    writer.Write((short)16);
    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
    writer.Write(samples * 2);
    double beatLength = 60.0 / tempo;
    int[] sequence = { 0, 7, 12, 14, 7, 12, 16, 12 };
    for (int i = 0; i < samples; i++)
    {
        double t = (double)i / rate;
        double pulses = t / beatLength * 2;
        int step = (int)pulses;
        double phase = pulses - step;
        double hz = root * System.Math.Pow(2, sequence[step % sequence.Length] / 12.0);
        double envelope = System.Math.Min(1, phase * 30) * System.Math.Exp(-3.6 * phase);
        double tone = System.Math.Sin(t * hz * System.Math.Tau) + .22 * System.Math.Sin(t * hz * 2 * System.Math.Tau);
        double bass = .32 * System.Math.Sin(t * root * .5 * System.Math.Tau) * System.Math.Exp(-2.5 * (t % (2 * beatLength)));
        double fade = System.Math.Min(1, System.Math.Min(t * 6, (duration - t) * 1.6));
        writer.Write((short)((tone * envelope + bass) * fade * 7200));
    }
}
'@
[CapsuleDemo.SampleAudio]::Write((Join-Path $media 'track-1.wav'), 36, 261.63, 104)
[CapsuleDemo.SampleAudio]::Write((Join-Path $media 'track-2.wav'), 32, 220, 88)
[CapsuleDemo.SampleAudio]::Write((Join-Path $media 'track-3.wav'), 44, 196, 72)
Write-Output (Join-Path $output 'CapsulePlayerDemo.exe')
