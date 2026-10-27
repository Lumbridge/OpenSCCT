using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SMatchInfo: the match settings carried by the map URL. Written from spec/game/match-options.md.
public partial class USMatchInfo
{
    /// <summary>
    /// Reads every setting from a URL options string (all fields but Handicap). Lives of 0 or absent mean unlimited
    /// (999999); booleans are true only for the text "true".
    /// </summary>
    [Uc("SBase.SMatchInfo.SetOptionString", Sha1 = "cae85aad7ec74d1618c0a880ea888a0cdef0186d")]
    [PortOf("uc:SBase.SMatchInfo:SetOptionString", Spec = "spec/game/match-options.md#data")]
    public void SetOptionString(string Options)
    {
        LevelName = ParseOption(Options, "MapName");
        MaxPlayers = SvmMath.Clamp(GetIntOption(Options, "MaxPlayers", MaxPlayers), 0, K.MaxPlayersCap);
        TimeLimit = GetIntOption(Options, "TimeLimit", 0);
        ScoreLimit = GetIntOption(Options, "ScoreLimit", 0);
        GameMode = (EGameMode)(byte)GetIntOption(Options, "GameMode", K.DefaultGameMode);
        NumLivesATT = GetIntOption(Options, "LivesATT", 0);
        if (NumLivesATT == 0)
        {
            NumLivesATT = K.UnlimitedLives;
        }

        TimesRespawn = GetIntOption(Options, "TimesRespawnsATT", K.DefaultTimesRespawn);
        NumLivesDEF = GetIntOption(Options, "LivesDEF", 0);
        if (NumLivesDEF == 0)
        {
            NumLivesDEF = K.UnlimitedLives;
        }

        BalanceTeams = UcStrings.EqualsIgnoreCase(ParseOption(Options, "BalanceTeams"), "true");
        Revenge = UcStrings.EqualsIgnoreCase(ParseOption(Options, "Revenge"), "true");
        GamesInARow = GetIntOption(Options, "GamesInARow", 0);
        TimesReinitFlag = GetIntOption(Options, "TimesReinitFlag", K.DefaultFlagAndBombOption);
        TimeToExplode = GetIntOption(Options, "TimeToExplode", K.DefaultFlagAndBombOption);
        GameWithRanking = UcStrings.EqualsIgnoreCase(ParseOption(Options, "GameWithRanking"), "true");
        LevelGap = GetIntOption(Options, "LevelGap", 0);
    }

    /// <summary>The settings as URL options, in the original order; booleans print as True/False, the mode as its number.</summary>
    [Uc("SBase.SMatchInfo.GetOptionsString", Sha1 = "6ff4f540bd872182d1cf553ca2cdb81fee4ffcd9")]
    [PortOf("uc:SBase.SMatchInfo:GetOptionsString", Spec = "spec/game/match-options.md#rules")]
    public string GetOptionsString() =>
        string.Concat(
            "?MapName=", LevelName,
            "?MaxPlayers=", Int(MaxPlayers),
            "?TimeLimit=", Int(TimeLimit),
            "?ScoreLimit=", Int(ScoreLimit),
            "?GameMode=", Int((byte)GameMode),
            "?LivesATT=", Int(NumLivesATT),
            "?TimesRespawnsATT=", Int(TimesRespawn),
            "?LivesDEF=", Int(NumLivesDEF),
            "?BalanceTeams=", UcStrings.BoolText(BalanceTeams),
            "?Revenge=", UcStrings.BoolText(Revenge),
            "?GamesInARow=", Int(GamesInARow),
            "?TimesReinitFlag=", Int(TimesReinitFlag),
            "?TimeToExplode=", Int(TimeToExplode),
            "?GameWithRanking=", UcStrings.BoolText(GameWithRanking),
            "?LevelGap=", Int(LevelGap));

    [Uc("SBase.SMatchInfo.GrabOption", Sha1 = "515285bbf5fab5abf4b0c5a15ca9c1625f1a8d98")]
    [PortOf("uc:SBase.SMatchInfo:GrabOption", Spec = "spec/game/match-options.md#rules")]
    public bool GrabOption(ref string Options, ref string Result) => UcOptions.GrabOption(ref Options, ref Result);

    [Uc("SBase.SMatchInfo.GetKeyValue", Sha1 = "1206ed65b058a14d1d9b01cc814b8fa47c8d9200")]
    [PortOf("uc:SBase.SMatchInfo:GetKeyValue", Spec = "spec/game/match-options.md#rules")]
    public void GetKeyValue(string Pair, ref string Key, ref string Value) => UcOptions.GetKeyValue(Pair, ref Key, ref Value);

    [Uc("SBase.SMatchInfo.ParseOption", Sha1 = "88c94fb30771bb84d1ced63ba2ec38f29ff01485")]
    [PortOf("uc:SBase.SMatchInfo:ParseOption", Spec = "spec/game/match-options.md#rules")]
    public string ParseOption(string Options, string InKey) => UcOptions.ParseOption(Options, InKey);

    [Uc("SBase.SMatchInfo.GetIntOption", Sha1 = "b9a97a20385e0af115ec3adb618094af69eae291")]
    [PortOf("uc:SBase.SMatchInfo:GetIntOption", Spec = "spec/game/match-options.md#rules")]
    public int GetIntOption(string Options, string ParseString, int CurrentValue) =>
        UcOptions.GetIntOption(Options, ParseString, CurrentValue);

    [Uc("SBase.SMatchInfo.MaxKillLimit", Sha1 = "5a1a00eacc7e4429222aba657147a4c3b40915c2")]
    [PortOf("uc:SBase.SMatchInfo:MaxKillLimit", Spec = "spec/game/match-options.md#rules")]
    public static int MaxKillLimit() => K.MaxKillLimit;

    [Uc("SBase.SMatchInfo.DefaultKillLimit", Sha1 = "88dccf159f7623b0125778f75232cd7e78df1570")]
    [PortOf("uc:SBase.SMatchInfo:DefaultKillLimit", Spec = "spec/game/match-options.md#rules")]
    public static int DefaultKillLimit() => K.DefaultKillLimit;

    /// <summary>The menu's lowest respawn delay: 1 s in Merc Deathmatch, 5 s otherwise (never applied to URL values).</summary>
    [Uc("SBase.SMatchInfo.RespawnFloor", Sha1 = "f976a010dc3b8585bc99d1b0ed5e9e194698dd44")]
    [PortOf("uc:SBase.SMatchInfo:RespawnFloor", Spec = "spec/game/match-options.md#rules")]
    public static int RespawnFloor(EGameMode Mode) =>
        Mode == EGameMode.GM_MercDeathMatch ? K.RespawnFloorMercDeathMatch : K.RespawnFloorOther;

    [Uc("SBase.SMatchInfo.IsDeathMatch", Sha1 = "61d99f0bb8af0a320afd21ec13b8b666b23ab210")]
    [PortOf("uc:SBase.SMatchInfo:IsDeathMatch", Spec = "spec/game/match-options.md#rules")]
    public static bool IsDeathMatch(EGameMode Mode) => Mode is EGameMode.GM_DeathMatch or EGameMode.GM_MercDeathMatch;

    /// <summary>The mode whose map settings a mode reads: Merc Deathmatch uses the Deathmatch entry.</summary>
    [Uc("SBase.SMatchInfo.RulesModeFor", Sha1 = "0e5a3df592b1f22b00da948caa21d436bc7da429")]
    [PortOf("uc:SBase.SMatchInfo:RulesModeFor", Spec = "spec/game/match-options.md#rules")]
    public static EGameMode RulesModeFor(EGameMode Mode) =>
        Mode == EGameMode.GM_MercDeathMatch ? EGameMode.GM_DeathMatch : Mode;

    private static string Int(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
