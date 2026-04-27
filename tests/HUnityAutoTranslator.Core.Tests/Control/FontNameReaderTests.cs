using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class FontNameReaderTests
{
    [Fact]
    public void TryReadFamilyName_prefers_english_windows_family_name()
    {
        var path = WriteTempFont(
            NameRecord.Create(platformId: 3, encodingId: 1, languageId: 0x0804, nameId: 1, value: "Simplified Chinese Family", utf16: true),
            NameRecord.Create(platformId: 3, encodingId: 1, languageId: 0x0409, nameId: 1, value: "English Family", utf16: true));

        FontNameReader.TryReadFamilyName(path, out var familyName).Should().BeTrue();
        familyName.Should().Be("English Family");
    }

    [Fact]
    public void TryReadFamilyName_reads_mac_roman_family_name()
    {
        var path = WriteTempFont(
            NameRecord.Create(platformId: 1, encodingId: 0, languageId: 0, nameId: 1, value: "Mac Family", utf16: false));

        FontNameReader.TryReadFamilyName(path, out var familyName).Should().BeTrue();
        familyName.Should().Be("Mac Family");
    }

    [Fact]
    public void TryReadFamilyName_returns_false_when_name_table_is_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ttf");
        File.WriteAllBytes(path, BuildFontWithoutNameTable());

        FontNameReader.TryReadFamilyName(path, out var familyName).Should().BeFalse();
        familyName.Should().BeEmpty();
    }

    private static string WriteTempFont(params NameRecord[] records)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ttf");
        File.WriteAllBytes(path, BuildFont(records));
        return path;
    }

    private static byte[] BuildFontWithoutNameTable()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, 0x00010000);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        return stream.ToArray();
    }

    private static byte[] BuildFont(IReadOnlyList<NameRecord> records)
    {
        using var name = new MemoryStream();
        var stringOffset = 6 + records.Count * 12;
        WriteUInt16(name, 0);
        WriteUInt16(name, (ushort)records.Count);
        WriteUInt16(name, (ushort)stringOffset);

        var stringBytes = new List<byte[]>();
        var currentStringOffset = 0;
        foreach (var record in records)
        {
            var bytes = record.GetBytes();
            WriteUInt16(name, record.PlatformId);
            WriteUInt16(name, record.EncodingId);
            WriteUInt16(name, record.LanguageId);
            WriteUInt16(name, record.NameId);
            WriteUInt16(name, (ushort)bytes.Length);
            WriteUInt16(name, (ushort)currentStringOffset);
            stringBytes.Add(bytes);
            currentStringOffset += bytes.Length;
        }

        foreach (var bytes in stringBytes)
        {
            name.Write(bytes, 0, bytes.Length);
        }

        var nameBytes = name.ToArray();
        using var font = new MemoryStream();
        WriteUInt32(font, 0x00010000);
        WriteUInt16(font, 1);
        WriteUInt16(font, 0);
        WriteUInt16(font, 0);
        WriteUInt16(font, 0);
        font.Write(Encoding.ASCII.GetBytes("name"), 0, 4);
        WriteUInt32(font, 0);
        WriteUInt32(font, 28);
        WriteUInt32(font, (uint)nameBytes.Length);
        font.Write(nameBytes, 0, nameBytes.Length);
        return font.ToArray();
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private sealed record NameRecord(ushort PlatformId, ushort EncodingId, ushort LanguageId, ushort NameId, string Value, bool Utf16)
    {
        public static NameRecord Create(ushort platformId, ushort encodingId, ushort languageId, ushort nameId, string value, bool utf16)
        {
            return new NameRecord(platformId, encodingId, languageId, nameId, value, utf16);
        }

        public byte[] GetBytes()
        {
            return Utf16 ? Encoding.BigEndianUnicode.GetBytes(Value) : Encoding.ASCII.GetBytes(Value);
        }
    }
}
