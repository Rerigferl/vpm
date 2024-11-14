using System.Text.Json;
using ConsoleAppFramework;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Collections.Concurrent;

await ConsoleApp.RunAsync(args, Command.Root);

static class Command
{
    static AuthenticationHeaderValue? Bearer = null;
    static ProductInfoHeaderValue UserAgent = new("numeira.vpm-repository-builder", null);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="listPath"></param>
    /// <param name="repositoryToken"></param>
    /// <param name="repositorySettings">-s</param>
    /// <returns></returns>
    internal static async Task Root([Argument] string listPath, [Argument] string? repositoryToken, [Argument] string repositorySettings, bool debug = false)
    {
        var targetRepos = File.ReadAllLines(listPath);
        if (repositoryToken is not null)
            Bearer = new("Bearer", repositoryToken);

        ConcurrentBag<PackageInfo> packageList = new();

        RepositorySetting? setting = null;
        if (repositorySettings is not null)
        {
            using var fs = File.OpenRead(repositorySettings);
            setting = await JsonSerializer.DeserializeAsync(fs, SerializeContexts.Default.RepositorySetting);
        }

        if (setting is null)
        {
            return;
        }

        var outputDir = new DirectoryInfo("website");
        if (!outputDir.Exists)
        {
            outputDir.Create();
        }

        var baseUrl = new Uri(new Uri(setting.Url), ".");

        await Parallel.ForEachAsync(targetRepos, async (repo, cancellationToken) =>
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
            client.DefaultRequestHeaders.Authorization = Bearer;

            if (debug)
                Console.WriteLine($"[GET] https://api.github.com/repos/{repo}/releases");
            var releases = await client.GetFromJsonAsync($"https://api.github.com/repos/{repo}/releases", SerializeContexts.Default.ReleaseArray, cancellationToken);
            if (releases is null)
                return;

            string MakeAssetDownloadUrl(Asset asset) => $"https://api.github.com/repos/{repo}/releases/assets/{asset.Id}";

            await Parallel.ForEachAsync(releases, async (release, cancellationToken) =>
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
                client.DefaultRequestHeaders.Authorization = Bearer;
                client.DefaultRequestHeaders.Accept.Add(new("application/octet-stream"));
                PackageInfo? packageInfo = null;
                Asset? zip = null;
                foreach (var asset in release?.Assets ?? [])
                {
                    var downloadUrl = MakeAssetDownloadUrl(asset);
                    

                    if (asset.Name is "package.json")
                    {
                        if (debug)
                            Console.WriteLine($"[GET] {downloadUrl}");

                        packageInfo = await client.GetFromJsonAsync(downloadUrl, SerializeContexts.Default.PackageInfo, cancellationToken);
                    }
                    else if (asset.ContentType is "application/zip")
                    {
                        zip = asset;
                    }

                    if (packageInfo is not null && zip is not null)
                    {
                        break;
                    }
                }

                if (packageInfo is null || zip is null)
                    return;

                if (debug)
                    Console.WriteLine($"[GET] {MakeAssetDownloadUrl(zip)}");

                using var response = await client.GetAsync(MakeAssetDownloadUrl(zip), cancellationToken);
                var size = (int)(response.Content.Headers.ContentLength ?? 0);
                var data = ArrayPool<byte>.Shared.Rent(size);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                stream.ReadExactly(data, 0, size);
                var zipData = data.AsMemory(0, size);

                using var handle = File.OpenHandle(Path.Join(outputDir.FullName, zip.Name), FileMode.Create, FileAccess.Write, FileShare.None);
                await RandomAccess.WriteAsync(handle, zipData, 0, cancellationToken);

                if (string.IsNullOrEmpty(packageInfo.ZipSHA256))
                {
                    packageInfo.ZipSHA256 = ComputeSHA256(zipData.Span);
                }

                ArrayPool<byte>.Shared.Return(data);

                packageInfo.Url = new Uri(baseUrl, zip.Name).AbsoluteUri;

                packageList.Add(packageInfo);
            });
        });

        var packages = packageList.ToArray();

        var bufferWriter = new ArrayBufferWriter<byte>(ushort.MaxValue);

        using Utf8JsonWriter writer = new(bufferWriter);
        writer.WriteStartObject();
        writer.WriteString("name"u8, setting.Name);
        writer.WriteString("author"u8, setting.Author);
        writer.WriteString("url"u8, setting.Url);
        writer.WriteString("id"u8, setting.Id);
        writer.WritePropertyName("packages"u8);
        writer.WriteStartObject();
        {
            foreach (var package in packages.GroupBy(x => x.Name))
            {
                writer.WritePropertyName(package.Key!);
                writer.WriteStartObject();
                writer.WritePropertyName("versions"u8);
                writer.WriteStartObject();
                foreach (var packageInfo in package.OrderByDescending(x => x.Version!, SemVerComparer.Instance))
                {
                    writer.WritePropertyName(packageInfo.Version!);
                    packageInfo.Author = null;
                    JsonSerializer.Serialize(writer, packageInfo, SerializeContexts.Default.PackageInfo);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync();
        using var handle = File.OpenHandle(Path.Join(outputDir.FullName, Path.GetFileName(setting.Url)), FileMode.Create, FileAccess.Write, FileShare.None);
        await RandomAccess.WriteAsync(handle, bufferWriter.WrittenMemory, 0);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string ComputeSHA256(ReadOnlySpan<byte> source)
    {
        var hash = (stackalloc byte[32]);
        var result = (stackalloc char[64]);

        SHA256.TryHashData(source, hash, out _);

        if (Avx2.IsSupported)
        {
            ToString_Vector256(hash, result);
        }
        else if (Ssse3.IsSupported)
        {
            ToString_Vector128(hash, result);
        }
        else
        {
            ToString(hash, result);
        }

        return result.ToString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ToString_Vector256(ReadOnlySpan<byte> source, Span<char> destination)
        {
            ref var srcRef = ref MemoryMarshal.GetReference(source);
            ref var dstRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(destination));
            var hexMap = Vector256.Create("0123456789abcdef0123456789abcdef"u8);

            for (int i = 0; i < 2; i++)
            {
                var src = Vector256.LoadUnsafe(ref srcRef);
                var shiftedSrc = Vector256.ShiftRightLogical(src.AsUInt64(), 4).AsByte();
                var lowNibbles = Avx2.UnpackLow(shiftedSrc, src);
                var highNibbles = Avx2.UnpackHigh(shiftedSrc, src);

                var l = Avx2.Shuffle(hexMap, lowNibbles & Vector256.Create((byte)0xF));
                var h = Avx2.Shuffle(hexMap, highNibbles & Vector256.Create((byte)0xF));

                var lh = l.WithUpper(h.GetLower());

                var (v0, v1) = Vector256.Widen(lh);

                v0.StoreUnsafe(ref dstRef);
                v1.StoreUnsafe(ref Unsafe.AddByteOffset(ref dstRef, 32));

                srcRef = ref Unsafe.AddByteOffset(ref srcRef, 16);
                dstRef = ref Unsafe.AddByteOffset(ref dstRef, 64);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ToString_Vector128(ReadOnlySpan<byte> source, Span<char> destination)
        {
            ref var srcRef = ref MemoryMarshal.GetReference(source);
            ref var dstRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(destination));
            var hexMap = Vector128.Create("0123456789abcdef0123456789abcdef"u8);

            for (int i = 0; i < 8; i++)
            {
                var src = Vector128.LoadUnsafe(ref srcRef);
                var shiftedSrc = Vector128.ShiftRightLogical(src.AsUInt64(), 4).AsByte();
                var lowNibbles = Sse2.UnpackLow(shiftedSrc, src);

                var l = Ssse3.Shuffle(hexMap, lowNibbles & Vector128.Create((byte)0xF));

                var (v0, _) = Vector128.Widen(l);

                v0.StoreUnsafe(ref dstRef);

                srcRef = ref Unsafe.AddByteOffset(ref srcRef, 4);
                dstRef = ref Unsafe.AddByteOffset(ref dstRef, 16);
            }
        }

        static void ToString(ReadOnlySpan<byte> source, Span<char> destination)
        {
            for (int i = 0, i2 = 0; i < source.Length && i2 < destination.Length; i++, i2 += 2)
            {
                var value = source[i];
                uint difference = ((value & 0xF0U) << 4) + ((uint)value & 0x0FU) - 0x8989U;
                uint packedResult = ((((uint)(-(int)difference) & 0x7070U) >> 4) + difference + 0xB9B9U) | (uint)0x2020;

                destination[i2 + 1] = (char)(packedResult & 0xFF);
                destination[i2] = (char)(packedResult >> 8);
            }
        }
    }
}

