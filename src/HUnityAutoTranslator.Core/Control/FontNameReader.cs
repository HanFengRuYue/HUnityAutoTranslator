using System.Text;

namespace HUnityAutoTranslator.Core.Control;

public static class FontNameReader
{
    private const ushort NameIdFamily = 1;
    private const ushort NameIdTypographicFamily = 16;
    private static readonly UnicodeEncoding BigEndianUnicode = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    public static bool TryReadFamilyName(string filePath, out string familyName)
    {
        familyName = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            return TryReadFamilyName(File.ReadAllBytes(filePath), out familyName);
        }
        catch
        {
            familyName = string.Empty;
            return false;
        }
    }

    internal static bool TryReadFamilyName(byte[] data, out string familyName)
    {
        familyName = string.Empty;
        if (data.Length < 12)
        {
            return false;
        }

        var tableCount = ReadUInt16(data, 4);
        var tableDirectoryLength = 12 + tableCount * 16;
        if (tableDirectoryLength > data.Length)
        {
            return false;
        }

        for (var i = 0; i < tableCount; i++)
        {
            var tableOffset = 12 + i * 16;
            if (!TagEquals(data, tableOffset, "name"))
            {
                continue;
            }

            var nameOffset = ReadUInt32(data, tableOffset + 8);
            var nameLength = ReadUInt32(data, tableOffset + 12);
            if (!IsValidRange(data.Length, nameOffset, nameLength))
            {
                return false;
            }

            return TryReadNameTable(data, (int)nameOffset, (int)nameLength, out familyName);
        }

        return false;
    }

    private static bool TryReadNameTable(byte[] data, int nameOffset, int nameLength, out string familyName)
    {
        familyName = string.Empty;
        if (nameLength < 6)
        {
            return false;
        }

        var count = ReadUInt16(data, nameOffset + 2);
        var stringOffset = ReadUInt16(data, nameOffset + 4);
        var recordStart = nameOffset + 6;
        var storageStart = nameOffset + stringOffset;
        if (recordStart + count * 12 > nameOffset + nameLength || storageStart > nameOffset + nameLength)
        {
            return false;
        }

        var candidates = new List<NameCandidate>();
        for (var i = 0; i < count; i++)
        {
            var recordOffset = recordStart + i * 12;
            var platformId = ReadUInt16(data, recordOffset);
            var encodingId = ReadUInt16(data, recordOffset + 2);
            var languageId = ReadUInt16(data, recordOffset + 4);
            var nameId = ReadUInt16(data, recordOffset + 6);
            if (nameId != NameIdFamily && nameId != NameIdTypographicFamily)
            {
                continue;
            }

            var length = ReadUInt16(data, recordOffset + 8);
            var offset = ReadUInt16(data, recordOffset + 10);
            var absoluteOffset = storageStart + offset;
            if (length == 0 || !IsValidRange(data.Length, (uint)absoluteOffset, length) || absoluteOffset + length > nameOffset + nameLength)
            {
                continue;
            }

            var text = DecodeName(data, absoluteOffset, length, platformId, encodingId);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            candidates.Add(new NameCandidate(text.Trim(), ScoreName(nameId, platformId, languageId)));
        }

        var best = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Value.Length)
            .FirstOrDefault();
        if (best == null)
        {
            return false;
        }

        familyName = best.Value;
        return true;
    }

    private static string? DecodeName(byte[] data, int offset, int length, ushort platformId, ushort encodingId)
    {
        try
        {
            if (platformId == 0 || platformId == 3)
            {
                return length % 2 == 0 ? BigEndianUnicode.GetString(data, offset, length).TrimEnd(new[] { '\0' }) : null;
            }

            if (platformId == 1)
            {
                return Encoding.ASCII.GetString(data, offset, length).TrimEnd(new[] { '\0' });
            }

            return encodingId == 0
                ? Encoding.ASCII.GetString(data, offset, length).TrimEnd(new[] { '\0' })
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreName(ushort nameId, ushort platformId, ushort languageId)
    {
        var score = nameId == NameIdFamily ? 100 : 80;
        if (IsEnglishLanguage(platformId, languageId))
        {
            score += 30;
        }

        if (platformId == 3)
        {
            score += 10;
        }
        else if (platformId == 0)
        {
            score += 8;
        }

        return score;
    }

    private static bool IsEnglishLanguage(ushort platformId, ushort languageId)
    {
        return platformId switch
        {
            1 => languageId == 0,
            3 => (languageId & 0x03ff) == 0x0009,
            _ => false
        };
    }

    private static bool TagEquals(byte[] data, int offset, string tag)
    {
        return offset + 4 <= data.Length &&
            data[offset] == tag[0] &&
            data[offset + 1] == tag[1] &&
            data[offset + 2] == tag[2] &&
            data[offset + 3] == tag[3];
    }

    private static bool IsValidRange(int dataLength, uint offset, uint length)
    {
        return offset <= dataLength && length <= dataLength - offset;
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
            ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) |
            data[offset + 3];
    }

    private sealed record NameCandidate(string Value, int Score);
}
