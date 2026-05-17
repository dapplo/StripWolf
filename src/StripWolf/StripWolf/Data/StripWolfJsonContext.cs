using System.Text.Json.Serialization;
using StripWolf.Models;
using StripWolf.Models.Komga;
using StripWolf.Services;

namespace StripWolf.Data;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<KomgaLibrary>), TypeInfoPropertyName = "ListKomgaLibrary")]
[JsonSerializable(typeof(KomgaPage<KomgaSeries>), TypeInfoPropertyName = "KomgaPageKomgaSeries")]
[JsonSerializable(typeof(KomgaPage<KomgaBook>), TypeInfoPropertyName = "KomgaPageKomgaBook")]
[JsonSerializable(typeof(KomgaPage<KomgaReadList>), TypeInfoPropertyName = "KomgaPageKomgaReadList")]
[JsonSerializable(typeof(KomgaSeries))]
[JsonSerializable(typeof(KomgaBook))]
[JsonSerializable(typeof(List<KomgaPageInfo>), TypeInfoPropertyName = "ListKomgaPageInfo")]
[JsonSerializable(typeof(KomgaReadList))]
[JsonSerializable(typeof(List<KomgaHeader>), TypeInfoPropertyName = "ListKomgaHeader")]
[JsonSerializable(typeof(KomgaReadProgressUpdate))]
[JsonSerializable(typeof(KomgaReadListUpdate))]
[JsonSerializable(typeof(Dictionary<int, string>), TypeInfoPropertyName = "DictionaryIntString")]
[JsonSerializable(typeof(Dictionary<int, SettingsService.SensitiveServerData>), TypeInfoPropertyName = "DictionaryIntSensitiveServerData")]
[JsonSerializable(typeof(int?), TypeInfoPropertyName = "NullableInt32")]
public partial class StripWolfJsonContext : JsonSerializerContext
{
}
