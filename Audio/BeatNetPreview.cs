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
    internal bool IsActive => sound.hasHandle();
    internal bool IsLoading => starting;
    internal string Error { get; private set; } = string.Empty;

    internal void Play(Uri url)
    {
        Stop();
        Error = string.Empty;
        var result = RuntimeManager.CoreSystem.createStream(url.AbsoluteUri, MODE.NONBLOCKING | MODE._2D, out sound);
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
        else if (channel.isPlaying(out var playing) != RESULT.OK || !playing)
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
            if (starting)
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
        if (pausedMusic)
        {
            ArcadeBGMManager.Instance?.PauseSongPreview(wasPaused);
            pausedMusic = false;
        }
    }

    public void Dispose() => Stop();
}
