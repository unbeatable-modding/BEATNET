using FMOD;
using FMOD.Studio;
using FMODUnity;

namespace BEATNET;

internal sealed class BeatNetBus
{
    private Bus bus;
    private ChannelGroup group;
    private int users;
    private bool locked;

    internal RESULT Acquire(out ChannelGroup channelGroup)
    {
        channelGroup = group;
        if (users > 0)
        {
            users++;
            return RESULT.OK;
        }
        bus = RuntimeManager.GetBus("bus:/music");
        var result = bus.lockChannelGroup();
        if (result != RESULT.OK && result != RESULT.ERR_ALREADY_LOCKED)
        {
            return result;
        }
        locked = result == RESULT.OK;
        result = RuntimeManager.StudioSystem.flushCommands();
        if (result == RESULT.OK)
        {
            result = bus.getChannelGroup(out group);
        }
        if (result != RESULT.OK)
        {
            Release();
            return result;
        }
        channelGroup = group;
        users = 1;
        return RESULT.OK;
    }

    internal void Release()
    {
        if (users > 0 && --users > 0)
        {
            return;
        }
        if (locked)
        {
            bus.unlockChannelGroup();
            locked = false;
        }
        group.clearHandle();
    }
}
