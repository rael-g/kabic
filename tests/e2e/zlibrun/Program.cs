var source = new byte[4096];
var bound = Zlib.Zlib.CompressBound((nuint)source.Length);
var dest = new byte[(int)bound];
var code = Zlib.Zlib.Compress(dest, out var written, source);
if (code != 0 || written == 0 || written >= (nuint)source.Length) throw new InvalidOperationException($"compress: {code}, {written}");
if (!Zlib.Zlib.ZlibVersion().StartsWith('1')) throw new InvalidOperationException("version");
Console.WriteLine($"{source.Length} -> {written}");
