namespace MedApp.Api.Tests.Support;

/// <summary>Ids and PHI-like strings from TestData/seed-bundle.json (all synthetic).</summary>
public static class SeedData
{
    public const string OidA = "11111111-1111-4111-8111-111111111111";
    public const string OidB = "22222222-2222-4222-8222-222222222222";
    public const string OidUnlinked = "33333333-3333-4333-8333-333333333333";

    public const string PatientA = "medapp-seed-a";
    public const string PatientB = "medapp-seed-b";

    /// <summary>Strings that must never appear in logs: names, birth dates, user oids.</summary>
    public static readonly string[] SensitiveStrings =
    [
        "Avery", "Quillfeather", "Blake", "Thornbury",
        "1971-03-14", "1984-09-02",
        OidA, OidB, OidUnlinked,
    ];

    public static string BundlePath => Path.Combine(AppContext.BaseDirectory, "TestData", "seed-bundle.json");
}
