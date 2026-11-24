using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace UploaderCore;

/// <summary>Görüntüde okunan bir yazı ve piksel cinsinden sınır kutusu.</summary>
public record struct TextRegion(string Text, double X, double Y, double Width, double Height);

/// <summary>Florence-2 (base-ft) modelini ONNX Runtime ile çalıştırır: yazıları bölgeleriyle okur.</summary>
public sealed class FlorenceOcr : IDisposable
{
    public static readonly string[] ModelFiles = ["onnx/vision_encoder.onnx", "onnx/embed_tokens.onnx", "onnx/encoder_model.onnx", "onnx/decoder_model_merged.onnx", "tokenizer.json"];
    public const string Repository = "onnx-community/Florence-2-base-ft";
    public const int InputSize = 768;

    // "What is the text in the image, with regions?" isteminin belirteçleri (<OCR_WITH_REGION> görevi).
    private static readonly long[] PromptIds = [0, 2264, 16, 5, 2788, 11, 5, 2274, 6, 19, 3806, 116, 2];
    private const long Bos = 0, Eos = 2, DecoderStart = 2;
    private const int Layers = 6, Heads = 12, HeadDim = 64, NoRepeatNgram = 3;

    private readonly InferenceSession _vision, _embed, _encoder, _decoder;
    private readonly FlorenceTokenizer _tokenizer;
    private readonly RunOptions _runOptions = new();

    public FlorenceOcr(string modelDirectory)
    {
        string P(string f) => Path.Combine(modelDirectory, f.Replace('/', Path.DirectorySeparatorChar));
        // Resmî derleme Microsoft'a kullanım telemetrisi gönderebilir; uygulama hiçbir veri göndermez.
        DisableTelemetry();
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _vision = new InferenceSession(P("onnx/vision_encoder.onnx"), options);
        _embed = new InferenceSession(P("onnx/embed_tokens.onnx"), options);
        _encoder = new InferenceSession(P("onnx/encoder_model.onnx"), options);
        _decoder = new InferenceSession(P("onnx/decoder_model_merged.onnx"), options);
        _tokenizer = new FlorenceTokenizer(P("tokenizer.json"));
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "setenv")]
    private static extern int SetEnv(string name, string value, int overwrite);

    /// <summary>Kitaplık ortam oluşturulurken telemetriyi başlatır; bu yüzden önce ortam değişkeniyle kapatılır.</summary>
    private static void DisableTelemetry()
    {
        // .NET, Windows dışında yerel ortam değişkenlerini değiştirmez; orada setenv doğrudan çağrılır.
        if (OperatingSystem.IsWindows()) Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1");
        else SetEnv("ORT_DISABLE_TELEMETRY", "1", 1);
        OrtEnv.Instance().DisableTelemetryEvents();
    }

    public static bool IsInstalled(string directory) =>
        ModelFiles.All(f => File.Exists(Path.Combine(directory, f.Replace('/', Path.DirectorySeparatorChar))));

    private OrtValue RunSingle(InferenceSession session, string inputName, OrtValue input, string outputName)
    {
        using var result = session.Run(_runOptions, [inputName], [input], [outputName]);
        // Sonuç koleksiyonu atılınca değer de atılır; veriyi kopyalayıp kendi tensörümüzü oluştururuz.
        var shape = result[0].GetTensorTypeAndShape().Shape;
        return OrtValue.CreateTensorValueFromMemory(result[0].GetTensorDataAsSpan<float>().ToArray(), shape);
    }

    /// <param name="rgb768">768x768, satır sıralı RGB baytları.</param>
    public long[] Generate(byte[] rgb768, int maxNewTokens = 192)
    {
        const int n = InputSize * InputSize;
        if (rgb768.Length != n * 3) throw new ArgumentException("768x768 RGB bekleniyor", nameof(rgb768));
        float[] mean = [0.485f, 0.456f, 0.406f], std = [0.229f, 0.224f, 0.225f];
        var pixels = new float[n * 3];
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < n; i++)
                pixels[c * n + i] = (rgb768[i * 3 + c] / 255f - mean[c]) / std[c];

        using var pixelValue = OrtValue.CreateTensorValueFromMemory(pixels, [1, 3, InputSize, InputSize]);
        using var image = RunSingle(_vision, "pixel_values", pixelValue, "image_features");
        using var promptIds = OrtValue.CreateTensorValueFromMemory((long[])PromptIds.Clone(), [1, PromptIds.Length]);
        using var prompt = RunSingle(_embed, "input_ids", promptIds, "inputs_embeds");

        var imageShape = image.GetTensorTypeAndShape().Shape;
        long dim = imageShape[2], seq = imageShape[1] + PromptIds.Length;
        var merged = new float[seq * dim];
        image.GetTensorDataAsSpan<float>().CopyTo(merged);
        prompt.GetTensorDataAsSpan<float>().CopyTo(merged.AsSpan((int)(imageShape[1] * dim)));
        var maskData = new long[seq];
        Array.Fill(maskData, 1L);
        using var mask = OrtValue.CreateTensorValueFromMemory(maskData, [1, seq]);
        using var mergedValue = OrtValue.CreateTensorValueFromMemory(merged, [1, seq, dim]);
        using var hiddenResult = _encoder.Run(_runOptions, ["attention_mask", "inputs_embeds"], [mask, mergedValue], ["last_hidden_state"]);
        var hidden = hiddenResult[0];

        var outputNames = new List<string> { "logits" };
        for (var i = 0; i < Layers; i++)
            foreach (var part in new[] { "decoder.key", "decoder.value", "encoder.key", "encoder.value" })
                outputNames.Add($"present.{i}.{part}");
        var past = new Dictionary<string, OrtValue>();
        foreach (var name in outputNames.Skip(1))
            past[name.Replace("present", "past_key_values")] = OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float, [1, Heads, 0, HeadDim]);

        var tokens = new List<long> { DecoderStart };
        using var yes = OrtValue.CreateTensorValueFromMemory(new[] { true }, [1]);
        using var no = OrtValue.CreateTensorValueFromMemory(new[] { false }, [1]);
        var keep = new List<IDisposable>();
        try
        {
            for (var step = 0; step < maxNewTokens; step++)
            {
                using var lastId = OrtValue.CreateTensorValueFromMemory(new[] { tokens[^1] }, [1, 1]);
                using var last = RunSingle(_embed, "input_ids", lastId, "inputs_embeds");
                var names = new List<string> { "encoder_attention_mask", "encoder_hidden_states", "inputs_embeds", "use_cache_branch" };
                var values = new List<OrtValue> { mask, hidden, last, step == 0 ? no : yes };
                foreach (var (k, v) in past) { names.Add(k); values.Add(v); }
                var output = _decoder.Run(_runOptions, names, values, outputNames);
                keep.Add(output);
                for (var i = 1; i < outputNames.Count; i++)
                {
                    // Kodlayıcı dikkat önbelleği yalnızca ilk adımda üretilir; sonraki adımlarda aynısı kullanılır.
                    if (step > 0 && outputNames[i].Contains(".encoder.")) continue;
                    past[outputNames[i].Replace("present", "past_key_values")] = output[i];
                }
                long next;
                if (step == 0) next = Bos; // forced_bos_token_id
                else if (step == maxNewTokens - 1) next = Eos;
                else
                {
                    var banned = BannedTokens(tokens);
                    var logits = output[0].GetTensorDataAsSpan<float>();
                    var best = 0;
                    var bestValue = float.NegativeInfinity;
                    for (var i = 0; i < logits.Length; i++)
                        if (logits[i] > bestValue && !banned.Contains(i)) { best = i; bestValue = logits[i]; }
                    next = best;
                }
                tokens.Add(next);
                if (next == Eos) break;
                // Yalnızca son iki adımın çıktısı gerekli (kodlayıcı önbelleği ilk adımda kalır).
                if (keep.Count > 2) { keep[1].Dispose(); keep.RemoveAt(1); }
            }
        }
        finally
        {
            foreach (var d in keep) d.Dispose();
        }
        return tokens.ToArray();
    }

    /// <summary>no_repeat_ngram_size: daha önce görülen bir üçlüyü tamamlayacak belirteçler yasaklanır.</summary>
    internal static HashSet<long> BannedTokens(IReadOnlyList<long> tokens)
    {
        var banned = new HashSet<long>();
        const int n = NoRepeatNgram;
        if (tokens.Count < n) return banned;
        for (var i = 0; i <= tokens.Count - n; i++)
        {
            var same = true;
            for (var j = 0; j < n - 1 && same; j++) same = tokens[i + j] == tokens[tokens.Count - (n - 1) + j];
            if (same) banned.Add(tokens[i + n - 1]);
        }
        return banned;
    }

    public List<TextRegion> Recognize(byte[] rgb768, double width, double height) => _tokenizer.ParseRegions(Generate(rgb768), width, height);

    public string Decode(long[] tokens) => _tokenizer.Decode(tokens);

    public void Dispose()
    {
        _vision.Dispose(); _embed.Dispose(); _encoder.Dispose(); _decoder.Dispose(); _runOptions.Dispose();
    }
}

/// <summary>tokenizer.json içinden yalnızca kod çözme için gerekenleri okur (bayt düzeyinde BPE).</summary>
internal sealed class FlorenceTokenizer
{
    private readonly Dictionary<long, string> _idToToken = [];
    private readonly Dictionary<long, int> _locValue = [];
    private readonly HashSet<long> _special = [];
    private static readonly Dictionary<int, byte> ByteDecoder = BuildByteDecoder();

    private static Dictionary<int, byte> BuildByteDecoder()
    {
        // GPT-2 bayt ↔ karakter eşlemesi
        var bs = Enumerable.Range(33, 94).Concat(Enumerable.Range(161, 12)).Concat(Enumerable.Range(174, 82)).ToList();
        var cs = new List<int>(bs);
        var n = 0;
        for (var b = 0; b < 256; b++)
            if (!bs.Contains(b)) { bs.Add(b); cs.Add(256 + n++); }
        return bs.Zip(cs).ToDictionary(p => p.Second, p => (byte)p.First);
    }

    public FlorenceTokenizer(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var p in doc.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject()) _idToToken[p.Value.GetInt64()] = p.Name;
        foreach (var added in doc.RootElement.GetProperty("added_tokens").EnumerateArray())
        {
            var id = added.GetProperty("id").GetInt64();
            var content = added.GetProperty("content").GetString() ?? "";
            _idToToken[id] = content;
            _special.Add(id);
            if (content.StartsWith("<loc_") && int.TryParse(content.AsSpan(5, content.Length - 6), out var v)) _locValue[id] = v;
        }
    }

    private string Text(IEnumerable<long> ids)
    {
        var bytes = new List<byte>();
        foreach (var id in ids)
        {
            if (!_idToToken.TryGetValue(id, out var token)) continue;
            if (_special.Contains(id)) { bytes.AddRange(Encoding.UTF8.GetBytes(token)); continue; }
            foreach (var rune in token.EnumerateRunes())
            {
                if (ByteDecoder.TryGetValue(rune.Value, out var b)) bytes.Add(b);
                else bytes.AddRange(Encoding.UTF8.GetBytes(rune.ToString()));
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public string Decode(IEnumerable<long> ids) => Text(ids);

    /// <summary>"yazı&lt;loc×8&gt;yazı&lt;loc×8&gt;…" dizisini bölgelere ayırır; konumlar 0–999 aralığında nicemlenmiştir.</summary>
    public List<TextRegion> ParseRegions(IEnumerable<long> ids, double width, double height)
    {
        var regions = new List<TextRegion>();
        var textIds = new List<long>();
        var locs = new List<int>();
        foreach (var id in ids)
        {
            if (_locValue.TryGetValue(id, out var v))
            {
                locs.Add(v);
                if (locs.Count < 8) continue;
                var label = Text(textIds).Trim();
                if (label.Length > 0)
                {
                    var xs = new[] { 0, 2, 4, 6 }.Select(i => (locs[i] + 0.5) / 1000 * width).ToArray();
                    var ys = new[] { 1, 3, 5, 7 }.Select(i => (locs[i] + 0.5) / 1000 * height).ToArray();
                    regions.Add(new TextRegion(label, xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min()));
                }
                textIds.Clear(); locs.Clear();
            }
            else if (_special.Contains(id)) { }
            else
            {
                locs.Clear(); // eksik kutu: at
                textIds.Add(id);
            }
        }
        return regions;
    }
}
