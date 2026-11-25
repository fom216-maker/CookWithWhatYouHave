using System.Text.Json.Serialization;

namespace CookWithWhatYouHave.Contracts;

/// <summary>Appliance used to adapt times and temperatures.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CookingMethod>))]
public enum CookingMethod
{
    PanOven,
    AirFryer,
    PressureCooker
}
