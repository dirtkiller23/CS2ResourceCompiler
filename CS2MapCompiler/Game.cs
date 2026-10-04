using System.Linq;

namespace CS2MapCompiler;

// The Source 2 games whose tools the app can compile with. Which one it is decides which options there are, some defaults,
// and a few flags
internal enum Game
{
    Cs2,
    Deadlock,
    Dota2,
    HalfLifeAlyx,
    SteamVrHome,
    DeskJob,
    SteamVR,

    Hl3,
    Hlx,
    Primelock,
}

internal static class Games
{
    // Each game's executables, in the order the game folder is searched for them, and whether it's from before 2021. Older
    // games bake lighting only on the CPU through -vrad3 and lack the newer options
    public static readonly (Game Game, string[] Executables, bool Legacy)[] All =
    [
        (Game.Cs2, ["cs2.exe"], false),
        (Game.HalfLifeAlyx, ["hlvr.exe"], true),
        (Game.Deadlock, ["project8.exe", "deadlock.exe"], false),
        (Game.Primelock, ["primelock.exe"], false),
        (Game.Hlx, ["hlx.exe"], false),
        (Game.Hl3, ["hl3.exe"], false),
        (Game.SteamVrHome, ["steamtours.exe"], true),
        (Game.Dota2, ["dota2.exe"], false),
        (Game.DeskJob, ["deskjob.exe"], true),
        (Game.SteamVR, ["vr.exe", "steamtours.exe"], true),
    ];

    public static IEnumerable<string> Executables => All.SelectMany(g => g.Executables);

    public static bool IsLegacy(this Game game)
    {
        return All.First(g => g.Game == game).Legacy;
    }
}
