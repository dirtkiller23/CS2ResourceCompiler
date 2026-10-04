using System.IO;
using System.Linq;
using ValveResourceFormat.IO;

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

// A game's name as shown, its executables, in the order the game folder is searched for them, and whether it's from before
// 2021. Older games bake lighting only on the CPU through -vrad3 and lack the newer options. Folder is where the executables
// and tools are within the Steam install, which only differs for games that come inside another app
internal sealed record GameInfo(Game Game, string Name, string[] Executables, bool Legacy, string Folder = @"game\bin\win64");

// A game found in its folder, where its tools are
internal sealed record InstalledGame(GameInfo Info, string Folder);

internal static class Games
{
    // in the order they're preferred when several are installed
    public static readonly GameInfo[] All =
    [
        new(Game.Cs2, "Counter-Strike 2", ["cs2.exe"], false),
        new(Game.HalfLifeAlyx, "Half-Life: Alyx", ["hlvr.exe"], true),
        new(Game.Deadlock, "Deadlock", ["project8.exe", "deadlock.exe"], false),
        new(Game.Primelock, "Primelock", ["primelock.exe"], false),
        new(Game.Hlx, "HLX", ["hlx.exe"], false),
        new(Game.Hl3, "Half-Life 3", ["hl3.exe"], false),
        new(Game.SteamVrHome, "SteamVR Home", ["steamtours.exe"], true, @"tools\steamvr_environments\game\bin\win64"),
        new(Game.Dota2, "Dota 2", ["dota2.exe"], false),
        new(Game.DeskJob, "Aperture Desk Job", ["deskjob.exe"], true),
        new(Game.SteamVR, "SteamVR", ["vr.exe", "steamtours.exe"], true),
    ];

    public static IEnumerable<string> Executables => All.SelectMany(g => g.Executables);

    public static bool IsLegacy(this Game game)
    {
        return All.First(g => g.Game == game).Legacy;
    }

    // The game whose executable is in a folder, or null when there's none
    public static GameInfo? Find(string folder)
    {
        return All.FirstOrDefault(g => g.Executables.Any(exe => File.Exists(Path.Combine(folder, exe))));
    }

    // The games installed through Steam, found by their executables rather than their app ids, so ones without an id here
    // are found too. Steam doesn't know the executables of games inside other apps, like SteamVR Home, so each app is
    // looked in where any game keeps them. Some apps share a game's folder, so each folder is listed once
    public static List<InstalledGame> Installed()
    {
        return
        [
            .. GameFolderLocator.FindAllSteamGames()
                .SelectMany(app => All.Select(g => g.Folder).Distinct().Select(folder => Path.Combine(app.GamePath, folder)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(folder => Find(folder) is { } info ? new InstalledGame(info, folder) : null)
                .OfType<InstalledGame>()
                .OrderBy(installed => Array.IndexOf(All, installed.Info)),
        ];
    }
}
