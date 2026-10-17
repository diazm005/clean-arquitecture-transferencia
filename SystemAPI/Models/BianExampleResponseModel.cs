using Application.Adapters;
using System.Text.Json.Serialization;
using SystemAPI.Models.Internals;

namespace SystemAPI.Models;

public class BianExampleResponseModel: BianResponseModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RetrieveExampleAdapter? RetrieveExampleResponse { set; get; }
}
