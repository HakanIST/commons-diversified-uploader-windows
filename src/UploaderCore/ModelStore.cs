namespace UploaderCore;

/// <summary>Model dosyalarını Hugging Face'ten ilk kullanımda indirir.</summary>
public static class ModelStore
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommonsDiversifiedUploader", "models", "Florence-2-base-ft");

    /// <param name="progress">0…1 arası toplam ilerleme ve o an inen dosyanın adı.</param>
    public static async Task Ensure(string directory, IProgress<(double Fraction, string File)>? progress = null, CancellationToken ct = default)
    {
        var files = FlorenceOcr.ModelFiles;
        for (var i = 0; i < files.Length; i++)
        {
            var target = Path.Combine(directory, files[i].Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var response = await Http.Shared.GetAsync($"https://huggingface.co/{FlorenceOcr.Repository}/resolve/main/{files[i]}", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new ApiException($"Model indirilemedi: {files[i]}");
            var total = response.Content.Headers.ContentLength ?? 0;
            var temp = target + ".part";
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[1 << 20];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(((i + (double)done / total) / files.Length, files[i]));
                }
            }
            File.Move(temp, target, overwrite: true);
        }
        progress?.Report((1, ""));
    }
}
