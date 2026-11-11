using System.Text;
using Domain.Common;

namespace Application.UseCases.Gtm;

/// <summary>
/// Gerador puro em C# de QR Code (ISO/IEC 18004, modo Byte, ECC Nível L, versões 1 a 8) em formato SVG
/// estilizado exclusivamente com <c>currentColor</c> para conformidade com <c>tokens.css</c>.
/// </summary>
public static class QrCodeSvgGenerator
{
    private readonly record struct VersionSpec(
        int Version,
        int Size,
        int TotalCodewords,
        int DataCodewords,
        int EccCodewordsPerBlock,
        int BlockCount,
        int RemainderBits,
        int[] AlignmentCenters);

    private static readonly VersionSpec[] Versions =
    [
        new(1, 21, 26, 19, 7, 1, 0, []),
        new(2, 25, 44, 34, 10, 1, 7, [6, 18]),
        new(3, 29, 70, 55, 15, 1, 7, [6, 22]),
        new(4, 33, 100, 80, 20, 1, 7, [6, 26]),
        new(5, 37, 134, 108, 26, 1, 7, [6, 30]),
        new(6, 41, 172, 136, 18, 2, 7, [6, 34]),
        new(7, 45, 196, 156, 20, 2, 0, [6, 22, 38]),
        new(8, 49, 242, 194, 24, 2, 0, [6, 24, 42])
    ];

    private const int QuietZone = 4;

    public static Result<string> GenerateSvg(string payload, string ariaLabel = "QR Code")
    {
        var matrixResult = GenerateMatrix(payload);
        if (matrixResult.IsFailure)
        {
            return Result.Failure<string>(matrixResult.Error);
        }

        var matrix = matrixResult.Value;
        var size = matrix.GetLength(0);
        var totalSize = size + (QuietZone * 2);
        var pathBuilder = new StringBuilder(size * size * 4);

        for (var row = 0; row < size; row++)
        {
            var col = 0;
            while (col < size)
            {
                if (!matrix[row, col])
                {
                    col++;
                    continue;
                }

                var runStart = col;
                while (col < size && matrix[row, col])
                {
                    col++;
                }

                var runLength = col - runStart;
                pathBuilder
                    .Append('M')
                    .Append(runStart + QuietZone)
                    .Append(' ')
                    .Append(row + QuietZone)
                    .Append('h')
                    .Append(runLength)
                    .Append("v1h-")
                    .Append(runLength)
                    .Append('z');
            }
        }

        var escapedLabel = System.Security.SecurityElement.Escape(
            string.IsNullOrWhiteSpace(ariaLabel) ? "QR Code" : ariaLabel.Trim());

        var svg = new StringBuilder(pathBuilder.Length + 256)
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
            .Append(totalSize)
            .Append(' ')
            .Append(totalSize)
            .Append("\" role=\"img\" aria-label=\"")
            .Append(escapedLabel)
            .Append("\" shape-rendering=\"crispEdges\"><path fill=\"currentColor\" d=\"")
            .Append(pathBuilder)
            .Append("\"/></svg>")
            .ToString();

        return Result.Success(svg);
    }

    public static Result<bool[,]> GenerateMatrix(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<bool[,]>(
                Error.Validation("QrCode.EmptyPayload", "O conteúdo para geração do QR Code é obrigatório."));
        }

        var bytes = Encoding.UTF8.GetBytes(payload.Trim());
        var selectedSpec = SelectVersion(bytes.Length);
        if (selectedSpec is null)
        {
            return Result.Failure<bool[,]>(
                Error.Validation(
                    "QrCode.PayloadTooLarge",
                    "O conteúdo excede a capacidade máxima suportada para o QR Code."));
        }

        var spec = selectedSpec.Value;
        var dataCodewords = EncodeByteDataCodewords(bytes, spec.DataCodewords);
        var finalBits = InterleaveAndAppendEcc(dataCodewords, spec);

        var size = spec.Size;
        var isFunction = new bool[size, size];
        var baseModules = new bool[size, size];

        DrawFunctionPatterns(spec, baseModules, isFunction);
        PlaceDataBits(finalBits, baseModules, isFunction, size);

        var bestMask = 0;
        var bestPenalty = int.MaxValue;
        bool[,]? bestMatrix = null;

        for (var mask = 0; mask < 8; mask++)
        {
            var candidate = ApplyMaskAndMetadata(baseModules, isFunction, spec, mask);
            var penalty = EvaluateMaskPenalty(candidate, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
                bestMatrix = candidate;
            }
        }

        _ = bestMask;
        return Result.Success(bestMatrix!);
    }

    private static VersionSpec? SelectVersion(int byteLength)
    {
        foreach (var spec in Versions)
        {
            // Mode (4 bits) + Length (8 bits) = 12 bits -> 2 bytes overhead
            if (byteLength + 2 <= spec.DataCodewords)
            {
                return spec;
            }
        }

        return null;
    }

    private static byte[] EncodeByteDataCodewords(byte[] payloadBytes, int targetDataCodewords)
    {
        var bits = new List<bool>(targetDataCodewords * 8);
        AppendBits(bits, 0b0100, 4); // Byte mode
        AppendBits(bits, payloadBytes.Length, 8); // Count for versions 1..9

        foreach (var b in payloadBytes)
        {
            AppendBits(bits, b, 8);
        }

        var capacityBits = targetDataCodewords * 8;
        var terminatorBits = Math.Min(4, capacityBits - bits.Count);
        AppendBits(bits, 0, terminatorBits);

        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        var codewords = new byte[targetDataCodewords];
        var codewordCount = bits.Count / 8;
        for (var i = 0; i < codewordCount; i++)
        {
            byte val = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                if (bits[(i * 8) + bit])
                {
                    val |= (byte)(1 << (7 - bit));
                }
            }

            codewords[i] = val;
        }

        byte[] padBytes = [0xEC, 0x11];
        for (var i = codewordCount; i < targetDataCodewords; i++)
        {
            codewords[i] = padBytes[(i - codewordCount) % 2];
        }

        return codewords;
    }

    private static bool[] InterleaveAndAppendEcc(byte[] dataCodewords, VersionSpec spec)
    {
        var blockDataLength = spec.DataCodewords / spec.BlockCount;
        var generator = BuildReedSolomonGenerator(spec.EccCodewordsPerBlock);

        var dataBlocks = new byte[spec.BlockCount][];
        var eccBlocks = new byte[spec.BlockCount][];

        for (var b = 0; b < spec.BlockCount; b++)
        {
            var block = new byte[blockDataLength];
            Array.Copy(dataCodewords, b * blockDataLength, block, 0, blockDataLength);
            dataBlocks[b] = block;
            eccBlocks[b] = ComputeReedSolomonRemainder(block, generator);
        }

        var interleaved = new List<byte>(spec.TotalCodewords);
        for (var i = 0; i < blockDataLength; i++)
        {
            for (var b = 0; b < spec.BlockCount; b++)
            {
                interleaved.Add(dataBlocks[b][i]);
            }
        }

        for (var i = 0; i < spec.EccCodewordsPerBlock; i++)
        {
            for (var b = 0; b < spec.BlockCount; b++)
            {
                interleaved.Add(eccBlocks[b][i]);
            }
        }

        var bits = new bool[(interleaved.Count * 8) + spec.RemainderBits];
        for (var i = 0; i < interleaved.Count; i++)
        {
            var cw = interleaved[i];
            for (var bit = 0; bit < 8; bit++)
            {
                bits[(i * 8) + bit] = ((cw >> (7 - bit)) & 1) == 1;
            }
        }

        return bits;
    }

    private static byte[] BuildReedSolomonGenerator(int degree)
    {
        var poly = new byte[degree];
        poly[degree - 1] = 1;
        byte root = 1;

        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                poly[j] = MultiplyGf(poly[j], root);
                if (j + 1 < degree)
                {
                    poly[j] ^= poly[j + 1];
                }
            }

            root = MultiplyGf(root, 0x02);
        }

        return poly;
    }

    private static byte[] ComputeReedSolomonRemainder(byte[] data, byte[] generator)
    {
        var result = new byte[generator.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;

            for (var i = 0; i < generator.Length; i++)
            {
                result[i] ^= MultiplyGf(generator[i], factor);
            }
        }

        return result;
    }

    private static byte MultiplyGf(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    private static void DrawFunctionPatterns(VersionSpec spec, bool[,] modules, bool[,] isFunction)
    {
        var size = spec.Size;

        // Finder patterns + separators
        DrawFinderPattern(0, 0, modules, isFunction, size);
        DrawFinderPattern(size - 7, 0, modules, isFunction, size);
        DrawFinderPattern(0, size - 7, modules, isFunction, size);

        // Timing patterns
        for (var i = 8; i < size - 8; i++)
        {
            var dark = i % 2 == 0;
            SetFunctionModule(6, i, dark, modules, isFunction);
            SetFunctionModule(i, 6, dark, modules, isFunction);
        }

        // Alignment patterns
        var centers = spec.AlignmentCenters;
        for (var i = 0; i < centers.Length; i++)
        {
            for (var j = 0; j < centers.Length; j++)
            {
                var r = centers[i];
                var c = centers[j];
                if ((i == 0 && j == 0) ||
                    (i == 0 && j == centers.Length - 1) ||
                    (i == centers.Length - 1 && j == 0))
                {
                    continue;
                }

                DrawAlignmentPattern(r, c, modules, isFunction);
            }
        }

        // Dark module
        SetFunctionModule(size - 8, 8, true, modules, isFunction);

        // Reserve format info areas
        for (var i = 0; i < 9; i++)
        {
            if (i != 6)
            {
                SetFunctionModule(8, i, false, modules, isFunction);
                SetFunctionModule(i, 8, false, modules, isFunction);
            }
        }

        for (var i = 0; i < 8; i++)
        {
            SetFunctionModule(8, size - 1 - i, false, modules, isFunction);
            if (i < 7)
            {
                SetFunctionModule(size - 1 - i, 8, false, modules, isFunction);
            }
        }

        // Version info (versions >= 7)
        if (spec.Version >= 7)
        {
            var versionBits = EncodeVersionInfo(spec.Version);
            for (var i = 0; i < 18; i++)
            {
                var bit = ((versionBits >> i) & 1) == 1;
                var a = size - 11 + (i % 3);
                var b = i / 3;
                SetFunctionModule(a, b, bit, modules, isFunction);
                SetFunctionModule(b, a, bit, modules, isFunction);
            }
        }
    }

    private static void DrawFinderPattern(int topRow, int leftCol, bool[,] modules, bool[,] isFunction, int size)
    {
        for (var dr = -1; dr <= 7; dr++)
        {
            for (var dc = -1; dc <= 7; dc++)
            {
                var r = topRow + dr;
                var c = leftCol + dc;
                if (r < 0 || r >= size || c < 0 || c >= size)
                {
                    continue;
                }

                var isDark = dr is >= 0 and <= 6 && dc is >= 0 and <= 6 &&
                    (dr is 0 or 6 || dc is 0 or 6 || (dr is >= 2 and <= 4 && dc is >= 2 and <= 4));
                SetFunctionModule(r, c, isDark, modules, isFunction);
            }
        }
    }

    private static void DrawAlignmentPattern(int centerRow, int centerCol, bool[,] modules, bool[,] isFunction)
    {
        for (var dr = -2; dr <= 2; dr++)
        {
            for (var dc = -2; dc <= 2; dc++)
            {
                var dist = Math.Max(Math.Abs(dr), Math.Abs(dc));
                SetFunctionModule(centerRow + dr, centerCol + dc, dist != 1, modules, isFunction);
            }
        }
    }

    private static void SetFunctionModule(int row, int col, bool isDark, bool[,] modules, bool[,] isFunction)
    {
        modules[row, col] = isDark;
        isFunction[row, col] = true;
    }

    private static void PlaceDataBits(bool[] bits, bool[,] modules, bool[,] isFunction, int size)
    {
        var bitIndex = 0;
        var upward = true;

        for (var rightCol = size - 1; rightCol >= 1; rightCol -= 2)
        {
            if (rightCol == 6)
            {
                rightCol = 5;
            }

            for (var step = 0; step < size; step++)
            {
                var row = upward ? size - 1 - step : step;
                for (var offset = 0; offset < 2; offset++)
                {
                    var col = rightCol - offset;
                    if (!isFunction[row, col] && bitIndex < bits.Length)
                    {
                        modules[row, col] = bits[bitIndex++];
                    }
                }
            }

            upward = !upward;
        }
    }

    private static bool[,] ApplyMaskAndMetadata(bool[,] baseModules, bool[,] isFunction, VersionSpec spec, int mask)
    {
        var size = spec.Size;
        var result = new bool[size, size];

        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                var invert = !isFunction[r, c] && ShouldInvertMask(mask, r, c);
                result[r, c] = baseModules[r, c] ^ invert;
            }
        }

        // Format info: ECC Level L = 0b01
        var formatBits = EncodeFormatInfo(0b01, mask);
        for (var i = 0; i <= 5; i++)
        {
            result[8, i] = ((formatBits >> i) & 1) == 1;
        }

        result[8, 7] = ((formatBits >> 6) & 1) == 1;
        result[8, 8] = ((formatBits >> 7) & 1) == 1;
        result[7, 8] = ((formatBits >> 8) & 1) == 1;

        for (var i = 9; i < 15; i++)
        {
            result[14 - i, 8] = ((formatBits >> i) & 1) == 1;
        }

        for (var i = 0; i < 8; i++)
        {
            result[8, size - 1 - i] = ((formatBits >> i) & 1) == 1;
        }

        for (var i = 8; i < 15; i++)
        {
            result[size - 15 + i, 8] = ((formatBits >> i) & 1) == 1;
        }

        result[size - 8, 8] = true;
        return result;
    }

    private static bool ShouldInvertMask(int mask, int r, int c) => mask switch
    {
        0 => (r + c) % 2 == 0,
        1 => r % 2 == 0,
        2 => c % 3 == 0,
        3 => (r + c) % 3 == 0,
        4 => ((r / 2) + (c / 3)) % 2 == 0,
        5 => ((r * c) % 2) + ((r * c) % 3) == 0,
        6 => (((r * c) % 2) + ((r * c) % 3)) % 2 == 0,
        7 => (((r + c) % 2) + ((r * c) % 3)) % 2 == 0,
        _ => false
    };

    private static int EncodeFormatInfo(int eccLevelBits, int mask)
    {
        var data = (eccLevelBits << 3) | mask;
        var rem = data << 10;
        for (var i = 4; i >= 0; i--)
        {
            if (((rem >> (i + 10)) & 1) != 0)
            {
                rem ^= 0x537 << i;
            }
        }

        return ((data << 10) | rem) ^ 0x5412;
    }

    private static int EncodeVersionInfo(int version)
    {
        var rem = version << 12;
        for (var i = 5; i >= 0; i--)
        {
            if (((rem >> (i + 12)) & 1) != 0)
            {
                rem ^= 0x1F25 << i;
            }
        }

        return (version << 12) | rem;
    }

    private static int EvaluateMaskPenalty(bool[,] modules, int size)
    {
        var penalty = 0;

        // Rule 1: 5+ same color in row/col
        for (var r = 0; r < size; r++)
        {
            var runColor = modules[r, 0];
            var runLen = 1;
            for (var c = 1; c < size; c++)
            {
                if (modules[r, c] == runColor)
                {
                    runLen++;
                }
                else
                {
                    if (runLen >= 5)
                    {
                        penalty += 3 + (runLen - 5);
                    }

                    runColor = modules[r, c];
                    runLen = 1;
                }
            }

            if (runLen >= 5)
            {
                penalty += 3 + (runLen - 5);
            }
        }

        for (var c = 0; c < size; c++)
        {
            var runColor = modules[0, c];
            var runLen = 1;
            for (var r = 1; r < size; r++)
            {
                if (modules[r, c] == runColor)
                {
                    runLen++;
                }
                else
                {
                    if (runLen >= 5)
                    {
                        penalty += 3 + (runLen - 5);
                    }

                    runColor = modules[r, c];
                    runLen = 1;
                }
            }

            if (runLen >= 5)
            {
                penalty += 3 + (runLen - 5);
            }
        }

        // Rule 2: 2x2 blocks
        for (var r = 0; r < size - 1; r++)
        {
            for (var c = 0; c < size - 1; c++)
            {
                var color = modules[r, c];
                if (modules[r, c + 1] == color && modules[r + 1, c] == color && modules[r + 1, c + 1] == color)
                {
                    penalty += 3;
                }
            }
        }

        // Rule 4: dark ratio deviation from 50%
        var darkCount = 0;
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                if (modules[r, c])
                {
                    darkCount++;
                }
            }
        }

        var total = size * size;
        var fivePercentSteps = Math.Abs((darkCount * 20) - (total * 10)) / total;
        penalty += fivePercentSteps * 10;

        return penalty;
    }

    private static void AppendBits(List<bool> target, int value, int bitCount)
    {
        for (var i = bitCount - 1; i >= 0; i--)
        {
            target.Add(((value >> i) & 1) == 1);
        }
    }
}

