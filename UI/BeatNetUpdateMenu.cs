using Arcade.UI.MenuStates;

namespace BEATNET;

internal static class BeatNetUpdateMenu
{
    private static string song = string.Empty;
    private static bool returning;

    internal static void Open(string id)
    {
        song = id;
        var menu = ArcadeMenuStateMachine.Instance;
        if (returning || menu == null || menu.CurrentState?.StateName == EArcadeMenuStates.SongSelect) { return; }
        returning = true;
        if (!menu.SetState(EArcadeMenuStates.SongSelect, onComplete: () => returning = false)) { returning = false; }
    }

    internal static void Tick(BeatNetButton button)
    {
        if (!returning && song.Length > 0 && button.OpenUpdate(song)) { song = string.Empty; }
    }

    internal static void Clear()
    {
        song = string.Empty;
        returning = false;
    }
}
