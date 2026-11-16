using System.ComponentModel.DataAnnotations;

namespace MedApp.Api.Fhir;

public sealed class FhirOptions
{
    public const string SectionName = "Fhir";

    /// <summary>FHIR base URL, e.g. http://localhost:8080/fhir or https://&lt;workspace&gt;-&lt;fhir&gt;.fhir.azurehealthcareapis.com.</summary>
    [Required]
    [Url]
    public string Url { get; set; } = "";

    /// <summary>When true, attach a DefaultAzureCredential token (scope "{Url}/.default") to every FHIR request.</summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>Upper bound on resources returned from one paged search, to cap work per request.</summary>
    [Range(1, 5000)]
    public int MaxSearchResults { get; set; } = 1000;
}
