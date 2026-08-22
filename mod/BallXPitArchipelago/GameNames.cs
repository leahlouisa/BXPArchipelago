using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Converts between game enums (CharType/BuildingType/LevelType) and the display names used
/// in Archipelago item/location strings, via the explicit tables in GameNames.Generated.cs
/// (generated from shared/game_data.json - regenerate via scratchpad's gen_gamenames.py-style
/// script if that file changes, don't hand-edit the generated file).
///
/// This used to be a mechanical camelCase-split/join formula instead of a table. That broke
/// silently: several real in-game names don't reverse-parse from their enum token at all
/// ("Laboratory" for kLab, "Sheriff's Office" for kSheriffOffice, "Smoldering Depths" for
/// kHell), and several BuildingType enum values turned out to be unused/cut content with no
/// real in-game building behind them - granting their "blueprint" via SaveMgr.GainBlueprint
/// succeeded silently but produced nothing visible in-game (confirmed live: kPetLab). Table
/// entries are wiki-cross-checked, not guessed.
/// </summary>
internal static partial class GameNames
{
    internal static string CharacterDisplay(CharType ct) =>
        CharacterNames.TryGetValue(ct, out var s) ? s : ct.ToString();

    internal static string BuildingDisplay(BuildingType bt) =>
        BuildingNames.TryGetValue(bt, out var s) ? s : bt.ToString();

    internal static string LevelDisplay(LevelType lt) =>
        LevelNames.TryGetValue(lt, out var s) ? s : lt.ToString();

    internal static bool TryParseCharacter(string display, out CharType ct)
    {
        foreach (var kv in CharacterNames)
        {
            if (kv.Value == display)
            {
                ct = kv.Key;
                return true;
            }
        }

        ct = default;
        return false;
    }

    internal static bool TryParseBuilding(string display, out BuildingType bt)
    {
        foreach (var kv in BuildingNames)
        {
            if (kv.Value == display)
            {
                bt = kv.Key;
                return true;
            }
        }

        bt = default;
        return false;
    }

    internal static bool TryParseLevel(string display, out LevelType lt)
    {
        foreach (var kv in LevelNames)
        {
            if (kv.Value == display)
            {
                lt = kv.Key;
                return true;
            }
        }

        lt = default;
        return false;
    }
}
