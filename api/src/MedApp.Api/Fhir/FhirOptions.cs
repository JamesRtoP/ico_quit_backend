using System.ComponentModel.DataAnnotations;

namespace MedApp.Api.Fhir;

public sealed class FhirOptions
{
    public const string SectionName = "Fhir";

    /// <summary>FHIR base URL, e.g. https://&lt;workspace&gt;-&lt;fhir&gt;.fhir.azurehealthcareapis.com.</summary>
    [Required]
    [Url]
    public string Url { get; set; } = "";

    /// <summary>When true, attach a DefaultAzureCredential token (scope "{Url}/.default") to every FHIR request.</summary>
    public bool UseManagedIdentity { get; set; }
}
