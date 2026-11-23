namespace Cliric.Core;

public static class ArtworkVideos
{
    public static string? Resolve(string? albumId)
    {
        string? key = albumId switch
        {
            "0SrskI3mHcu5MzKeZNv2f6" or "4d0a5uamW9NWRc1KXFeErI" or "02ATSOBqF7ddYG1248wCgH" or "3ETKNL7BaU8N2gh1GH4HXQ" => "0SrskI3mHcu5MzKeZNv2f6",
            "4yHDaLGrPSwUfJNVUIvFgg" or "6MBVShS0FIBbZLBdfzXuZS" => "4yHDaLGrPSwUfJNVUIvFgg",
            "3rZyOsowQOtW3BMuO4vCmh" => "3rZyOsowQOtW3BMuO4vCmh",
            _ => null
        };
        return key is null ? null : "https://res.lyricify.app/videos/id/" + key + ".mp4";
    }
}
