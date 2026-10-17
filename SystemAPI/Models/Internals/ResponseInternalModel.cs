using System.Text.Json.Serialization;

namespace SystemAPI.Models.Internals;

public class ResponseInternalModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyCollection<FieldErrorInternalModel>? Errors { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public dynamic? Response { set; get; }
}
