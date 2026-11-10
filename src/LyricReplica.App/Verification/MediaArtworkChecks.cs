using System.IO;
using Windows.Storage.Streams;

namespace LyricReplica.App;

internal static class MediaArtworkChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var bytes = Artwork.Encode(Artwork.Create(0));
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        var read = await SystemMediaConnection.ReadArtworkAsync(RandomAccessStreamReference.CreateFromStream(stream));
        check(read is not null && read.SequenceEqual(bytes), "WinRT thumbnail stream is read completely without a live media session");
        var bitmap = Artwork.Decode(read!);
        check(bitmap.IsFrozen && bitmap.PixelWidth > 0 && bitmap.PixelWidth <= 1200, "media artwork survives stream disposal and is decoded at bounded width");
        check(await SystemMediaConnection.ReadArtworkAsync(null) is null, "absent media thumbnail preserves the imported cover");
        using var empty = new InMemoryRandomAccessStream();
        check(await SystemMediaConnection.ReadArtworkAsync(RandomAccessStreamReference.CreateFromStream(empty)) is null, "empty media thumbnail is treated as unavailable");
        using var oversized = new InMemoryRandomAccessStream();
        oversized.Size = Artwork.MaximumMediaBytes + 1;
        bool tooLarge = false;
        try
        {
            await SystemMediaConnection.ReadArtworkAsync(RandomAccessStreamReference.CreateFromStream(oversized));
        }
        catch (InvalidDataException) { tooLarge = true; }
        check(tooLarge, "oversized media thumbnail is rejected before allocating its byte buffer");
        bool invalid = false;
        try
        {
            Artwork.Decode([1, 2, 3, 4]);
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException) { invalid = true; }
        check(invalid, "invalid thumbnail bytes cannot replace a valid cover");
    }
}
