var source = new byte[4096];
var bound = Zlib.Zlib.CompressBound((nuint)source.Length);
var dest = new byte[(int)bound];
var written = Zlib.Zlib.Compress(dest, source);
if (written == 0 || written >= (nuint)source.Length) throw new InvalidOperationException($"compress: {written}");
if (!Zlib.Zlib.ZlibVersion().StartsWith('1')) throw new InvalidOperationException("version");
Console.WriteLine($"{source.Length} -> {written}");
