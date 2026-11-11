using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BD2PreviewInteractions;

internal sealed class LocalAudioPlayer : MonoBehaviour
{
    private static readonly ConcurrentDictionary<string, (long Size, long Stamp)> verified = new();
    private readonly Dictionary<string, (AudioClip Clip, int Used)> clips = new();
    private readonly List<UnityWebRequest> requests = new();
    private readonly HashSet<string> warned = new();
    private AudioSource[] sources = Array.Empty<AudioSource>();
    private int generation;
    private int usage;

    internal void Initialize()
    {
        sources = new[] { gameObject.AddComponent<AudioSource>(), gameObject.AddComponent<AudioSource>() };
        foreach (var source in sources) { source.playOnAwake = false; source.spatialBlend = 0; }
    }

    internal void Play((string Hash, bool Loop)[] events)
    {
        Stop();
        StartCoroutine(LoadAndPlay(events.Take(2).ToArray(), generation));
    }

    internal void Stop()
    {
        generation++;
        foreach (var source in sources) if (source != null) { source.Stop(); source.clip = null; source.loop = false; }
        foreach (var request in requests.ToArray()) request.Abort();
    }

    private IEnumerator LoadAndPlay((string Hash, bool Loop)[] events, int requestId)
    {
        var loaded = new AudioClip[events.Length];
        for (var i = 0; i < events.Length; i++)
        {
            if (requestId != generation) yield break;
            var hash = events[i].Hash;
            if (clips.TryGetValue(hash, out var cached))
            {
                loaded[i] = cached.Clip;
                clips[hash] = (cached.Clip, ++usage);
                continue;
            }
            var path = Path.Combine(Plugin.AudioRoot, hash + ".ogg");
            var verification = Task.Run(() => Verify(path, hash));
            while (!verification.IsCompleted)
            {
                if (requestId != generation) yield break;
                yield return null;
            }
            if (requestId != generation) yield break;
            if (verification.IsFaulted)
            {
                if (warned.Add(hash)) Plugin.Instance?.Notify($"音频读取或校验失败：{verification.Exception?.GetBaseException().Message}");
                yield break;
            }
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS))
            {
                requests.Add(request);
                var operation = request.SendWebRequest();
                while (!operation.isDone) yield return null;
                requests.Remove(request);
                if (requestId != generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (warned.Add(hash)) Plugin.Instance?.Notify($"音频解码失败：{hash}");
                    yield break;
                }
                loaded[i] = DownloadHandlerAudioClip.GetContent(request);
                clips[hash] = (loaded[i], ++usage);
            }
        }
        if (requestId != generation) yield break;
        var start = AudioSettings.dspTime + 0.015;
        for (var i = 0; i < loaded.Length; i++)
        {
            sources[i].clip = loaded[i]; sources[i].loop = events[i].Loop;
            sources[i].PlayScheduled(start);
        }
        while (clips.Count > 24)
        {
            var oldest = clips.Where(p => !events.Any(e => e.Hash == p.Key)).OrderBy(p => p.Value.Used).First();
            Destroy(oldest.Value.Clip); clips.Remove(oldest.Key);
        }
    }

    private static void Verify(string path, string hash)
    {
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("无效音频哈希");
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("缺少音频文件", path);
        var stamp = (file.Length, file.LastWriteTimeUtc.Ticks);
        if (verified.TryGetValue(path, out var previous) && previous == stamp) return;
        using var stream = File.OpenRead(path);
        var header = new byte[4];
        if (stream.Read(header, 0, 4) != 4 || System.Text.Encoding.ASCII.GetString(header) != "OggS") throw new InvalidDataException("音频格式无效");
        stream.Position = 0;
        using var sha = SHA256.Create();
        if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != hash) throw new InvalidDataException("音频哈希不匹配");
        verified[path] = stamp;
    }

    private void OnDestroy()
    {
        Stop();
        foreach (var request in requests.ToArray()) request.Dispose();
        requests.Clear();
        foreach (var clip in clips.Values) if (clip.Clip != null) Destroy(clip.Clip);
        clips.Clear();
    }
}
