using Reactor.Utilities.Attributes;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

[RegisterInIl2Cpp]
public sealed class AudioTestHelper : MonoBehaviour
{
    private static AudioTestHelper? _instance;
    private AudioSource? _source;
    private AudioClip? _tone;

    public AudioTestHelper(IntPtr ptr) : base(ptr) { }

    public static void PlayTestTone()
    {
        Ensure();
        _instance!.PlayTone();
    }

    private static void Ensure()
    {
        if (_instance != null)
            return;

        var go = new GameObject("BCL_AudioTest");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<AudioTestHelper>();
    }

    private void PlayTone()
    {
        _source ??= gameObject.AddComponent<AudioSource>();
        _source.loop = false;
        _source.playOnAwake = false;
        _tone ??= GenerateTone(440f, 0.25f);
        _source.clip = _tone;
        _source.volume = 0.8f;
        _source.Play();
    }

    private static AudioClip GenerateTone(float freq, float durationSeconds)
    {
        var sampleRate = 48000;
        var sampleCount = Mathf.CeilToInt(sampleRate * durationSeconds);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * i / sampleRate) * 0.25f;
        }

        var clip = AudioClip.Create("BCL_TestTone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
