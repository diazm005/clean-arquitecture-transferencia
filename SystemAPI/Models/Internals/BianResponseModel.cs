using System.Text.Json.Serialization;

namespace SystemAPI.Models.Internals;

public class BianResponseModel
{    
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BianErrorInternalModel>? errors { get; set; }
}
