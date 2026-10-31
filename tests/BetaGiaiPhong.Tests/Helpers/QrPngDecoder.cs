using System.IO.Compression;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace BetaGiaiPhong.Tests.Helpers;

public static class QrPngDecoder
{
    public static string Decode(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length < 33)
        {
            throw new ArgumentException("Invalid PNG payload size.", nameof(pngBytes));
        }

        // Verify PNG magic signature: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
        if (pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47)
        {
            throw new InvalidOperationException("Payload does not start with PNG magic header.");
        }

        int width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
        int height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];

        using var idatStream = new MemoryStream();
        int offset = 8;
        while (offset < pngBytes.Length)
        {
            if (offset + 8 > pngBytes.Length) break;
            int len = (pngBytes[offset] << 24) | (pngBytes[offset + 1] << 16) | (pngBytes[offset + 2] << 8) | pngBytes[offset + 3];
            string type = System.Text.Encoding.ASCII.GetString(pngBytes, offset + 4, 4);
            if (type == "IDAT")
            {
                idatStream.Write(pngBytes, offset + 8, len);
            }
            offset += 12 + len;
        }

        idatStream.Position = 0;
        using var zlib = new ZLibStream(idatStream, CompressionMode.Decompress);
        using var rawStream = new MemoryStream();
        zlib.CopyTo(rawStream);
        var raw = rawStream.ToArray();

        int bytesPerLine = (width + 7) / 8;
        int stride = 1 + bytesPerLine;
        var luminances = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            int lineOffset = y * stride;
            for (int x = 0; x < width; x++)
            {
                byte b = raw[lineOffset + 1 + (x / 8)];
                int bit = (b >> (7 - (x % 8))) & 1;
                luminances[y * width + x] = (byte)(bit == 0 ? 0 : 255);
            }
        }

        var source = new PlanarYUVLuminanceSource(luminances, width, height, 0, 0, width, height, false);
        var binarizer = new HybridBinarizer(source);
        var binBitmap = new BinaryBitmap(binarizer);
        var reader = new QRCodeReader();
        var result = reader.decode(binBitmap);

        return result?.Text ?? throw new InvalidOperationException("Failed to decode QR code from PNG image.");
    }
}
