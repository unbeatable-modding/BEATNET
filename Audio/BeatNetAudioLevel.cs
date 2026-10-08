using System;
using FMOD;
using FMODUnity;

namespace BEATNET;

internal sealed class BeatNetAudioLevel : IDisposable
{
    private DSP meter;
    private bool inputEnabled;
    private bool outputEnabled;

    internal bool Read()
    {
        if (!meter.hasHandle())
        {
            if (RuntimeManager.CoreSystem.getMasterChannelGroup(out var group) != RESULT.OK
                || group.getDSP((int)CHANNELCONTROL_DSP_INDEX.TAIL, out var output) != RESULT.OK
                || output.getMeteringEnabled(out inputEnabled, out outputEnabled) != RESULT.OK
                || output.setMeteringEnabled(inputEnabled, true) != RESULT.OK) { return false; }
            meter = output;
        }
        return IsAudible(meter);
    }

    internal static bool IsAudible(DSP meter)
    {
        if (meter.getIdle(out var idle) != RESULT.OK || idle
            || meter.getMeteringInfo(IntPtr.Zero, out var output) != RESULT.OK) { return false; }
        for (var i = 0; i < output.numchannels; i++)
        {
            if (output.peaklevel[i] > 0.0001f) { return true; }
        }
        return false;
    }

    public void Dispose()
    {
        if (!meter.hasHandle()) { return; }
        meter.setMeteringEnabled(inputEnabled, outputEnabled);
        meter.clearHandle();
    }
}
