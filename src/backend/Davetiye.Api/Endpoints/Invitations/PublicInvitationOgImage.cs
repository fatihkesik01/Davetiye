using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Globalization;

namespace Davetiye.Api.Endpoints.Invitations;

/// <summary>A generic code-owned PNG; invitation text, addresses and contacts never enter it.</summary>
internal static class PublicInvitationOgImage
{
    public static byte[] Bytes => Create();
    public static byte[] Create(string? title = null, string? details = null)
    {
        const int width = 1200, height = 630;
        using var image = new MemoryStream();
        image.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 2;
        Chunk(image, "IHDR", header);
        var pixels = new byte[height * width * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var inset = x is >= 120 and < 1080 && y is >= 90 and < 540;
                var stripe = inset && (y is >= 140 and < 150 or >= 480 and < 490);
                var offset = (y * width + x) * 3;
                pixels[offset] = (byte)(stripe ? 129 : inset ? 255 : 242);
                pixels[offset + 1] = (byte)(stripe ? 92 : inset ? 255 : 236);
                pixels[offset + 2] = (byte)(stripe ? 160 : inset ? 255 : 246);
            }
        DrawText(pixels, width, title ?? "DAVETIYE", 160, 235, 4, 36, 2);
        DrawText(pixels, width, details ?? "DAVETLISINIZ", 160, 390, 2, 70, 1);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];
            for (var y = 0; y < height; y++)
            {
                pixels.AsSpan(y * width * 3, width * 3).CopyTo(row.AsSpan(1));
                zlib.Write(row);
            }
        }
        Chunk(image, "IDAT", compressed.ToArray());
        Chunk(image, "IEND", []);
        return image.ToArray();
    }
    private static void DrawText(byte[] pixels, int width, string value, int left, int top, int scale, int columns, int rows)
    {
        var text = new string(value.ToUpperInvariant().Replace('İ', 'I').Replace('ı', 'I')
            .Normalize(NormalizationForm.FormD).Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        for (var index = 0; index < Math.Min(text.Length, columns * rows); index++)
        {
            var character = text[index];
            var glyph = Font.GetValueOrDefault(character, Font['?']);
            for (var y = 0; y < 7; y++)
                for (var x = 0; x < 5; x++)
                    if ((glyph[y] & (1 << (4 - x))) != 0)
                        for (var dy = 0; dy < scale; dy++)
                            for (var dx = 0; dx < scale; dx++)
                            {
                                var px = left + (index % columns) * 6 * scale + x * scale + dx;
                                var py = top + (index / columns) * 10 * scale + y * scale + dy;
                                var offset = (py * width + px) * 3;
                                pixels[offset] = 74; pixels[offset + 1] = 53; pixels[offset + 2] = 90;
                            }
        }
    }
    // Small code-owned raster alphabet; arbitrary user content cannot execute drawing commands.
    private static readonly IReadOnlyDictionary<char, byte[]> Font = new Dictionary<char, string>
    {
        ['A']="0E11111F111111", ['B']="1E11111E11111E", ['C']="0E11101010110E", ['D']="1E11111111111E",
        ['E']="1F10101E10101F", ['F']="1F10101E101010", ['G']="0E11101711110F", ['H']="1111111F111111",
        ['I']="0E04040404040E", ['J']="0702020212120C", ['K']="11121418141211", ['L']="1010101010101F",
        ['M']="111B1515111111", ['N']="11191915131311", ['O']="0E11111111110E", ['P']="1E11111E101010",
        ['Q']="0E11111115120D", ['R']="1E11111E141211", ['S']="0F10100E01011E", ['T']="1F040404040404",
        ['U']="1111111111110E", ['V']="11111111110A04", ['W']="11111115151B11", ['X']="11110A040A1111",
        ['Y']="11110A04040404", ['Z']="1F01020408101F", ['0']="0E11131519110E", ['1']="040C040404040E",
        ['2']="0E11010204081F", ['3']="1E01010601011E", ['4']="02060A121F0202", ['5']="1F10101E01011E",
        ['6']="0E10101E11110E", ['7']="1F010204080808", ['8']="0E11110E11110E", ['9']="0E11110F01010E",
        [' ']="00000000000000", ['?']="0E110102040004", ['-']="0000001F000000", [':']="00040400040400",
        ['.']="00000000000C0C", ['/']="01010204081010", ['&']="0C12120C15120D", ['!']="04040404040004"
    }.ToDictionary(pair => pair.Key, pair => Convert.FromHexString(pair.Value));
    private static void Chunk(Stream stream, string name, byte[] payload)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, payload.Length); stream.Write(number);
        var type = Encoding.ASCII.GetBytes(name); stream.Write(type); stream.Write(payload);
        uint crc = 0xffffffff;
        foreach (var value in type.Concat(payload))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
}
