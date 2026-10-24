using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Kleiner prozeduraler Audio-Service (M7/P1): erzeugt alle Platzhalter-Sounds zur Laufzeit mit
    /// einfachen Sinus-/Rausch-Clips. Keine Audiodateien, keine externe Abhängigkeit. Wird nur vom GameClient
    /// erzeugt und bei Kampf-/Fortschrittsereignissen aufgerufen.</summary>
    public sealed class AudioService : MonoBehaviour
    {
        private const int SampleRate = 22050;

        private AudioSource _source;
        private AudioClip _swing;
        private AudioClip _hit;
        private AudioClip _kill;
        private AudioClip _hurt;
        private AudioClip _loot;
        private AudioClip _levelUp;
        private AudioClip _click;

        public void Initialize()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 0.45f;

            _swing = CreateNoise("Swing", 0.16f, 0.55f);
            _hit = CreateTone("Hit", 0.12f, 160f, 70f, 0.7f);
            _kill = CreateTone("Kill", 0.35f, 260f, 70f, 0.8f);
            _hurt = CreateTone("Hurt", 0.14f, 120f, 90f, 0.7f);
            _loot = CreateArpeggio("Loot", new[] { 660f, 990f }, 0.09f, 0.55f);
            _levelUp = CreateArpeggio("LevelUp", new[] { 523f, 659f, 784f, 1046f }, 0.1f, 0.65f);
            _click = CreateTone("Click", 0.04f, 880f, 880f, 0.35f);
        }

        public void PlaySwing() => Play(_swing, 0.7f);
        public void PlayHit() => Play(_hit, 0.8f);
        public void PlayKill() => Play(_kill, 0.9f);
        public void PlayHurt() => Play(_hurt, 0.8f);
        public void PlayLoot() => Play(_loot, 0.8f);
        public void PlayLevelUp() => Play(_levelUp, 0.9f);
        public void PlayClick() => Play(_click, 0.5f);

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || _source == null) return;
            _source.PlayOneShot(clip, volume);
        }

        private static AudioClip CreateTone(string name, float duration, float startFrequency, float endFrequency, float volume)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                float frequency = Mathf.Lerp(startFrequency, endFrequency, t);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float envelope = Mathf.Pow(1f - t, 2f);
                data[i] = Mathf.Sin(phase) * envelope * volume;
            }

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateNoise(string name, float duration, float volume)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[samples];
            float previous = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                float white = Random.value * 2f - 1f;
                previous = Mathf.Lerp(previous, white, 0.25f);
                data[i] = previous * Mathf.Pow(1f - t, 1.5f) * volume;
            }

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateArpeggio(string name, float[] frequencies, float noteDuration, float volume)
        {
            int noteSamples = Mathf.Max(1, Mathf.RoundToInt(noteDuration * SampleRate));
            int samples = noteSamples * frequencies.Length;
            var data = new float[samples];

            for (int note = 0; note < frequencies.Length; note++)
            {
                float phase = 0f;
                for (int i = 0; i < noteSamples; i++)
                {
                    float t = i / (float)noteSamples;
                    phase += 2f * Mathf.PI * frequencies[note] / SampleRate;
                    float envelope = Mathf.Sin(Mathf.PI * t) * (1f - 0.35f * t);
                    data[note * noteSamples + i] = Mathf.Sin(phase) * envelope * volume;
                }
            }

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
