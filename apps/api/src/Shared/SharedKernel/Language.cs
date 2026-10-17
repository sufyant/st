using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SharedKernel;

/// <summary>
/// The languages the web speaks: a user's language, and the language of an invitation's email. This is the one list: the OpenAPI
/// document and the messages to Notifications take it from here. A new language is a new member here, with its email texts. Each value
/// is its BCP 47 language tag in JSON and in the database (W3C, "Language tags in HTML and XML").
/// </summary>
[Description("A language the web speaks, by its BCP 47 language tag.")]
[JsonConverter(typeof(ExactNameEnumConverter<Language>))]
public enum Language
{
    [JsonStringEnumMemberName("en")]
    En,

    [JsonStringEnumMemberName("tr")]
    Tr,

    [JsonStringEnumMemberName("ar")]
    Ar,
}
