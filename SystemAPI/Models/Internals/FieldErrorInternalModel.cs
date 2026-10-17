using System.Text.Json.Serialization;

namespace SystemAPI.Models.Internals;

public class FieldErrorInternalModel
{
    public required string StatusCode { set; get; }
    public required string Message { set; get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { set; get; }
}
