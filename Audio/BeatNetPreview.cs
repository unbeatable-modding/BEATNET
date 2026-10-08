using System;
using System.Threading.Tasks;
using Arcade.UI;
using FMOD;
using FMODUnity;
using UnityEngine;

namespace BEATNET;

internal sealed class BeatNetPreview : IDisposable
{
    private Sound sound;
    private Channel channel;
    internal readonly BeatNetBus Bus = new();
    private bool busHeld;
    private bool starting;
    private bool pausedMusic;
    private bool wasPaused;
    private float deadline;
    private bool local;
    private float previewStart;
    private uint previewEnd;
    internal bool IsActive => sound.hasHandle();
    internal bool IsLoading => starting;
    internal string Error { get; private set; } = string.Empty;

    internal void Play(Uri url, float start = 0f)
    {
        Stop();
        Error = string.Empty;
        local = url.IsFile;
        previewStart = start;
        var result = RuntimeManager.CoreSystem.createStream(local ? url.LocalPath : url.AbsoluteUri, MODE.NONBLOCKING | MODE._2D, out sound);
        if (result != RESULT.OK)
        {
            Fail(result, "open");
            return;
        }
        starting = true;
        deadline = Time.unscaledTime + 30f;
    }

    internal void Tick()
    {
        if (!IsActive)
        {
            return;
        }
        if (starting)
        {
            var result = sound.getOpenState(out var state, out _, out _, out _);
            if (result != RESULT.OK || state == OPENSTATE.ERROR || Time.unscaledTime >= deadline)
            {
                Fail(result, "load");
                return;
            }
            if (state != OPENSTATE.READY)
            {
                return;
            }
            result = Bus.Acquire(out var group);
            if (result != RESULT.OK)
            {
                Fail(result, "music bus");
                return;
            }
            busHeld = true;
            result = RuntimeManager.CoreSystem.playSound(sound, group, true, out channel);
            if (result != RESULT.OK)
            {
                Fail(result, "play");
                return;
            }
            if (local)
            {
                result = sound.getLength(out var length, TIMEUNIT.MS);
                if (result != RESULT.OK)
                {
                    Fail(result, "duration");
                    return;
                }
                var offset = !float.IsNaN(previewStart) && !float.IsInfinity(previewStart)
                    && previewStart > 0f && previewStart < length / 1000f ? (uint)(previewStart * 1000f) : (uint)(length * 0.15f);
                previewEnd = offset + Math.Min(30000u, length - offset);
                result = channel.setPosition(offset, TIMEUNIT.MS);
                if (result != RESULT.OK)
                {
                    Fail(result, "seek");
                    return;
                }
            }
            wasPaused = ArcadeBGMManager.Paused;
            pausedMusic = ArcadeBGMManager.Instance != null;
            ArcadeBGMManager.Instance?.PauseSongPreview(true);
            result = channel.setPaused(false);
            if (result != RESULT.OK)
            {
                Fail(result, "resume");
                return;
            }
            starting = false;
        }
        else if (channel.isPlaying(out var playing) != RESULT.OK || !playing
            || local && channel.getPosition(out var position, TIMEUNIT.MS) == RESULT.OK && position >= previewEnd)
        {
            Stop();
        }
    }

    private void Fail(RESULT result, string stage)
    {
        Stop();
        CustomSongLoader.Logger?.LogWarning($"preview {stage} failed {result}");
        Error = "Cannot play preview / try again";
    }

    internal void Stop()
    {
        Error = string.Empty;
        if (channel.hasHandle())
        {
            channel.stop();
            channel.clearHandle();
        }
        if (sound.hasHandle())
        {
            var released = sound;
            sound.clearHandle();
            if (starting && !local)
            {
                _ = Task.Run(() => released.release());
            }
            else
            {
                released.release();
            }
        }
        if (busHeld)
        {
            Bus.Release();
            busHeld = false;
        }
        starting = false;
        local = false;
        previewEnd = 0;
        if (pausedMusic)
        {
            ArcadeBGMManager.Instance?.PauseSongPreview(wasPaused);
            pausedMusic = false;
        }
    }

    public void Dispose() => Stop();
}
