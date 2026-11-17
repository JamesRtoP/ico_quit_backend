namespace MedApp.Api.Tests.Support;

/// <summary>Ids and PHI-like strings from TestData/seed-bundle.json (all synthetic).</summary>
public static class SeedData
{
    public const string OidA = "11111111-1111-4111-8111-111111111111";
    public const string OidB = "22222222-2222-4222-8222-222222222222";
    public const string OidUnlinked = "33333333-3333-4333-8333-333333333333";

    public const string PatientA = "medapp-seed-a";
    public const string PatientB = "medapp-seed-b";

    public const string A_Lisinopril = "medapp-seed-a-mr1";
    public const string A_Metformin = "medapp-seed-a-mr2";
    public const string A_AtorvastatinStopped = "medapp-seed-a-mr3";
    public const string B_Sertraline = "medapp-seed-b-mr1";

    public const string A_Dose1 = "medapp-seed-a-ma1";
    public const string A_Dose2 = "medapp-seed-a-ma2";
    public const string B_Dose1 = "medapp-seed-b-ma1";

    /// <summary>Strings that must never appear in logs: names, birth dates, medication names, user oids.</summary>
    public static readonly string[] SensitiveStrings =
    [
        "Avery", "Quillfeather", "Blake", "Thornbury",
        "1971-03-14", "1984-09-02",
        "Lisinopril", "Metformin", "Atorvastatin", "Sertraline",
        OidA, OidB, OidUnlinked,
    ];

    public static string BundlePath => Path.Combine(AppContext.BaseDirectory, "TestData", "seed-bundle.json");
}
