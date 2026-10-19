// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

// Re- Sharper disable RedundantNameQualifier

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PdfSharp.Fonts
{
    /// <summary>
    /// Used in Core build only if no custom FontResolver and no FallbackFontResolver set
    /// and UseWindowsFontsUnderWindows or UseWindowsFontsUnderWsl2 is set.
    /// </summary>
    class WindowsPlatformFontResolver : IFontResolver
    {
        // This path is carved in stone. Even Microsoft hard-coded it in the source code of WPF.
        const string WindowsFontsPath = @"C:\Windows\Fonts";
        const string WindowsFontsPathUnderWsl2 = "/mnt/c/Windows/Fonts";

        class TypefaceInfo
        {
            public string FontFaceName { get; init; }

            public FontSimulation Simulation { get; init; }

            public string WindowsFileName { get; init; }

            public int CollectionFaceIndex { get; init; }

            internal TypefaceInfo(
                string fontFaceName,
                FontSimulation fontSimulation,
                string windowsFileName,
                int collectionFaceIndex = 0)
            {
                FontFaceName = fontFaceName;
                Simulation = fontSimulation;
                WindowsFileName = windowsFileName;
                CollectionFaceIndex = collectionFaceIndex;
            }

            [Flags]
            public enum FontSimulation
            {
                None = 0,
                Bold = 1,
                Italic = 2,
                Both = Bold | Italic
            }
        }

        static readonly List<TypefaceInfo> TypefaceInfos =
        [
            // ReSharper disable StringLiteralTypo

            new("Arial", TypefaceInfo.FontSimulation.None, "arial"),
            new("Arial Black", TypefaceInfo.FontSimulation.None, "ariblk"),
            new("Arial Bold", TypefaceInfo.FontSimulation.None, "arialbd"),
            new("Arial Italic", TypefaceInfo.FontSimulation.None, "ariali"),
            new("Arial Bold Italic", TypefaceInfo.FontSimulation.None, "arialbi"),

            new("Times New Roman", TypefaceInfo.FontSimulation.None, "times"),
            new("Times New Roman Bold", TypefaceInfo.FontSimulation.None, "timesbd"),
            new("Times New Roman Italic", TypefaceInfo.FontSimulation.None, "timesi"),
            new("Times New Roman Bold Italic", TypefaceInfo.FontSimulation.None, "timesbi"),

            new("Courier New", TypefaceInfo.FontSimulation.None, "cour"),
            new("Courier New Bold", TypefaceInfo.FontSimulation.None, "courbd"),
            new("Courier New Italic", TypefaceInfo.FontSimulation.None, "couri"),
            new("Courier New Bold Italic", TypefaceInfo.FontSimulation.None, "courbi"),

            new("Verdana", TypefaceInfo.FontSimulation.None, "verdana"),
            new("Verdana Bold", TypefaceInfo.FontSimulation.None, "verdanab"),
            new("Verdana Italic", TypefaceInfo.FontSimulation.None, "verdanai"),
            new("Verdana Bold Italic", TypefaceInfo.FontSimulation.None, "verdanaz"),

            new("DejaVu Sans", TypefaceInfo.FontSimulation.None, "DejaVuSans"),
            new("DejaVu Sans Bold", TypefaceInfo.FontSimulation.None, "DejaVuSans-Bold"),
            new("DejaVu Sans Italic", TypefaceInfo.FontSimulation.None, "DejaVuSans-Oblique"),
            new("DejaVu Sans Bold Italic", TypefaceInfo.FontSimulation.None, "DejaVuSans-BoldOblique"),

            new("Segoe UI", TypefaceInfo.FontSimulation.None, "segoeui"),
            new("Segoe UI Bold", TypefaceInfo.FontSimulation.None, "segoeuib"),
            new("Segoe UI Italic", TypefaceInfo.FontSimulation.None, "segoeuii"),
            new("Segoe UI Bold Italic", TypefaceInfo.FontSimulation.None, "segoeuiz"),

            new("Lucida Console", TypefaceInfo.FontSimulation.None, "lucon"),
            new("Lucida Console Bold", TypefaceInfo.FontSimulation.Bold, "lucon"),
            new("Lucida Console Italic", TypefaceInfo.FontSimulation.Italic, "lucon"),
            new("Lucida Console Bold Italic", TypefaceInfo.FontSimulation.Both, "lucon"),

            new("Symbol", TypefaceInfo.FontSimulation.None, "symbol"),
            new("Symbol Bold", TypefaceInfo.FontSimulation.Bold, "symbol"),
            new("Symbol Italic", TypefaceInfo.FontSimulation.Italic, "symbol"),
            new("Symbol Bold Italic", TypefaceInfo.FontSimulation.Both, "symbol"),

            new("SimSun", TypefaceInfo.FontSimulation.None, "simsun"),
            new("SimSun Bold", TypefaceInfo.FontSimulation.Bold, "simsun"),
            new("SimSun Italic", TypefaceInfo.FontSimulation.Italic, "simsun"),
            new("SimSun Bold Italic", TypefaceInfo.FontSimulation.Both, "simsun"),

            new("MS Mincho", TypefaceInfo.FontSimulation.None, "msmincho"),
            new("MS Mincho Bold", TypefaceInfo.FontSimulation.Bold, "msmincho"),
            new("MS Mincho Italic", TypefaceInfo.FontSimulation.Italic, "msmincho"),
            new("MS Mincho Bold Italic", TypefaceInfo.FontSimulation.Both, "msmincho"),

            new("MingLiU-ExtB", TypefaceInfo.FontSimulation.None, "mingliub"),
            new("MingLiU-ExtB Bold", TypefaceInfo.FontSimulation.Bold, "mingliub"),
            new("MingLiU-ExtB Italic", TypefaceInfo.FontSimulation.Italic, "mingliub"),
            new("MingLiU-ExtB Bold Italic", TypefaceInfo.FontSimulation.Both, "mingliub"),

            new("Cambria", TypefaceInfo.FontSimulation.None, "cambria"),
            new("Cambria Math", TypefaceInfo.FontSimulation.None, "cambria", 1),

            // ReSharper restore StringLiteralTypo
        ];

        // Returns a PlatformFontResolverInfo.
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var typefaces = TypefaceInfos.Where(f => f.FontFaceName.StartsWith(familyName, StringComparison.OrdinalIgnoreCase));

            if (isBold)
                typefaces = typefaces.Where(f => f.FontFaceName.Contains("bold", StringComparison.OrdinalIgnoreCase));

            if (isItalic)
                typefaces = typefaces.Where(f => f.FontFaceName.Contains("italic", StringComparison.OrdinalIgnoreCase));

            var family = typefaces.FirstOrDefault();

            if (family is not null)
                return new PlatformFontResolverInfo(family.CollectionFaceIndex == 0
                        ? family.WindowsFileName : $"{family.WindowsFileName}#{family.CollectionFaceIndex}",
                    (family.Simulation & TypefaceInfo.FontSimulation.Bold) != 0,
                    (family.Simulation & TypefaceInfo.FontSimulation.Italic) != 0);

            return null;
        }

        public byte[]? GetFont(string faceName)
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract because it can yet be null.
            _fontsPath ??= Capabilities.OperatingSystem.IsWindows
                ? WindowsFontsPath
                : WindowsFontsPathUnderWsl2;

            var face = TypefaceInfos.FirstOrDefault(f =>
                string.Equals(f.CollectionFaceIndex == 0 ? f.WindowsFileName : $"{f.WindowsFileName}#{f.CollectionFaceIndex}",
                    faceName, StringComparison.OrdinalIgnoreCase));
            if (face is null)
                return null;
            var filepath = Path.Combine(_fontsPath, face.WindowsFileName + ".ttf");
            if (face.CollectionFaceIndex == 0 && File.Exists(filepath))
                return File.ReadAllBytes(filepath);
            filepath = Path.Combine(_fontsPath, face.WindowsFileName + ".ttc");
            if (File.Exists(filepath))
                return ExtractCollectionFace(File.ReadAllBytes(filepath), face.CollectionFaceIndex);
            return null;
        }

        internal static byte[] ExtractCollectionFace(byte[] collection, int faceIndex)
        {
            var source = collection.AsSpan();
            if (source.Length < 16 || !source[..4].SequenceEqual("ttcf"u8))
                throw new InvalidDataException("Expected a TrueType collection.");
            var faceCount = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(8, 4));
            if (faceCount == 0 || faceCount > (source.Length - 12) / 4)
                throw new InvalidDataException("Invalid TrueType collection directory.");
            if (faceIndex < 0 || faceIndex >= faceCount)
                throw new ArgumentOutOfRangeException(nameof(faceIndex));
            var faceOffsetValue = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(12 + faceIndex * 4, 4));
            if (faceOffsetValue > source.Length - 12)
                throw new InvalidDataException("Invalid TrueType face offset.");
            var faceOffset = (int)faceOffsetValue;
            var count = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(faceOffset + 4, 2));
            if (count == 0 || faceOffset + 12 + count * 16 > source.Length)
                throw new InvalidDataException("Invalid TrueType table directory.");
            var tables = new List<(byte[] Tag, byte[] Data)>();
            for (var index = 0; index < count; index++)
            {
                var record = source.Slice(faceOffset + 12 + index * 16, 16);
                var offset = BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8, 4));
                var length = BinaryPrimitives.ReadUInt32BigEndian(record.Slice(12, 4));
                if (offset > source.Length || length > source.Length - offset)
                    throw new InvalidDataException("Invalid TrueType table offset.");
                var data = source.Slice((int)offset, (int)length).ToArray();
                if (record[..4].SequenceEqual("head"u8))
                {
                    if (data.Length < 12)
                        throw new InvalidDataException("Invalid TrueType head table.");
                    data.AsSpan(8, 4).Clear();
                }
                tables.Add((record[..4].ToArray(), data));
            }
            long lengthBytes = 12 + count * 16;
            foreach (var table in tables)
                lengthBytes += ((long)table.Data.Length + 3) & ~3L;
            if (lengthBytes > int.MaxValue)
                throw new InvalidDataException("TrueType face is too large.");
            var face = new byte[(int)lengthBytes];
            source.Slice(faceOffset, 4).CopyTo(face);
            BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(4, 2), count);
            var power = 1;
            var selector = 0;
            while (power * 2 <= count) { power *= 2; selector++; }
            BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(6, 2), (ushort)(power * 16));
            BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(8, 2), (ushort)selector);
            BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(10, 2), (ushort)(count * 16 - power * 16));
            var destination = 12 + count * 16;
            var headOffset = -1;
            for (var index = 0; index < tables.Count; index++)
            {
                var (tag, data) = tables[index];
                var record = face.AsSpan(12 + index * 16, 16);
                tag.CopyTo(record);
                BinaryPrimitives.WriteUInt32BigEndian(record.Slice(4, 4), Checksum(data));
                BinaryPrimitives.WriteUInt32BigEndian(record.Slice(8, 4), (uint)destination);
                BinaryPrimitives.WriteUInt32BigEndian(record.Slice(12, 4), (uint)data.Length);
                data.CopyTo(face.AsSpan(destination));
                if (tag.AsSpan().SequenceEqual("head"u8)) headOffset = destination;
                destination += (data.Length + 3) & ~3;
            }
            if (headOffset < 0) throw new InvalidDataException("TrueType face has no head table.");
            BinaryPrimitives.WriteUInt32BigEndian(face.AsSpan(headOffset + 8, 4),
                unchecked(0xB1B0AFBAu - Checksum(face)));
            return face;
        }

        static uint Checksum(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            for (var index = 0; index < data.Length; index += 4)
            {
                uint word = 0;
                for (var part = 0; part < 4 && index + part < data.Length; part++)
                    word |= (uint)data[index + part] << (24 - part * 8);
                sum = unchecked(sum + word);
            }
            return sum;
        }
        static string _fontsPath = null!;
    }
}
