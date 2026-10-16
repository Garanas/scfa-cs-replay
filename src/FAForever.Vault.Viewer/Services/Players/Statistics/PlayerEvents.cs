namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>The ids of the events the API counts for every player (<c>playerEvent</c>), all lower case.</summary>
public static class PlayerEvents
{
    /// <summary>Custom games played.</summary>
    public const string CustomGamesPlayed = "cfa449a6-655b-48d5-9a27-6044804fe35c";

    /// <summary>Ladder games played.</summary>
    public const string LadderGamesPlayed = "4a929def-e347-45b4-b26d-4325a3115859";

    /// <summary>Commanders lost.</summary>
    public const string AcusLost = "d6a699b7-99bc-4a7f-b128-15e1e289a7b3";

    /// <summary>Air units built.</summary>
    public const string BuiltAir = "3ebb0c4d-5e92-4446-bf52-d17ba9c5cd3c";

    /// <summary>Air units lost.</summary>
    public const string LostAir = "225e9b2e-ae09-4ae1-a198-eca8780b0fcd";

    /// <summary>Land units built.</summary>
    public const string BuiltLand = "ea123d7f-bb2e-4a71-bd31-88859f0c3c00";

    /// <summary>Land units lost.</summary>
    public const string LostLand = "a1a3fd33-abe2-4e56-800a-b72f4c925825";

    /// <summary>Naval units built.</summary>
    public const string BuiltNaval = "b5265b42-1747-4ba1-936c-292202637ce6";

    /// <summary>Naval units lost.</summary>
    public const string LostNaval = "3a7b3667-0f79-4ac7-be63-ba841fd5ef05";

    /// <summary>Seconds played.</summary>
    public const string SecondsPlayed = "cc791f00-343c-48d4-b5b3-8900b83209c0";

    /// <summary>Tech 1 units built.</summary>
    public const string BuiltTech1 = "a8ee4f40-1e30-447b-bc2c-b03065219795";

    /// <summary>Tech 1 units lost.</summary>
    public const string LostTech1 = "3dd3ed78-ce78-4006-81fd-10926738fbf3";

    /// <summary>Tech 2 units built.</summary>
    public const string BuiltTech2 = "89d4f391-ed2d-4beb-a1ca-6b93db623c04";

    /// <summary>Tech 2 units lost.</summary>
    public const string LostTech2 = "aebd750b-770b-4869-8e37-4d4cfdc480d0";

    /// <summary>Tech 3 units built.</summary>
    public const string BuiltTech3 = "92617974-8c1f-494d-ab86-65c2a95d1486";

    /// <summary>Tech 3 units lost.</summary>
    public const string LostTech3 = "7f15c2be-80b7-4573-8f41-135f84773e0f";

    /// <summary>Experimentals built.</summary>
    public const string BuiltExperimentals = "ed9fd79d-5ec7-4243-9ccf-f18c4f5baef1";

    /// <summary>Experimentals lost.</summary>
    public const string LostExperimentals = "701ca426-0943-4931-85af-6a08d36d9aaa";

    /// <summary>Engineers built.</summary>
    public const string BuiltEngineers = "60bb1fc0-601b-45cd-bd26-83b1a1ac979b";

    /// <summary>Engineers lost.</summary>
    public const string LostEngineers = "e8e99a68-de1b-4676-860d-056ad2207119";

    /// <summary>Games played as Aeon.</summary>
    public const string AeonPlays = "96ccc66a-c5a0-4f48-acaa-888b00778b57";

    /// <summary>Games won as Aeon.</summary>
    public const string AeonWins = "a6b51c26-64e6-4e7a-bda7-ea1cfe771ebb";

    /// <summary>Games played as Cybran.</summary>
    public const string CybranPlays = "ad193982-e7ca-465c-80b0-5493f9739559";

    /// <summary>Games won as Cybran.</summary>
    public const string CybranWins = "56b06197-1890-42d0-8b59-25e1add8dc9a";

    /// <summary>Games played as UEF.</summary>
    public const string UefPlays = "1b900d26-90d2-43d0-a64e-ed90b74c3704";

    /// <summary>Games won as UEF.</summary>
    public const string UefWins = "7be6fdc5-7867-4467-98ce-f7244a66625a";

    /// <summary>Games played as Seraphim.</summary>
    public const string SeraphimPlays = "fefcb392-848f-4836-9683-300b283bc308";

    /// <summary>Games won as Seraphim.</summary>
    public const string SeraphimWins = "15b6c19a-6084-4e82-ada9-6c30e282191f";

    /// <summary>Every event id above.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CustomGamesPlayed, LadderGamesPlayed, AcusLost, BuiltAir, LostAir, BuiltLand, LostLand, BuiltNaval, LostNaval,
        SecondsPlayed, BuiltTech1, LostTech1, BuiltTech2, LostTech2, BuiltTech3, LostTech3, BuiltExperimentals,
        LostExperimentals, BuiltEngineers, LostEngineers, AeonPlays, AeonWins, CybranPlays, CybranWins, UefPlays,
        UefWins, SeraphimPlays, SeraphimWins,
    ];
}
