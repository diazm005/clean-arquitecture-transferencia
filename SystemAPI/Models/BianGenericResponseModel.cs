using System.Text.Json.Serialization;
using SystemAPI.Models.Internals;

namespace SystemAPI.Models;

/// <summary>
/// Modelo genérico para respuestas BIAN que puede manejar cualquier tipo de datos
/// </summary>
/// <typeparam name="T">Tipo de datos para la respuesta exitosa</typeparam>
public class BianGenericResponseModel<T> : BianResponseModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; set; }

    /// <summary>
    /// Constructor para respuesta exitosa
    /// </summary>
    /// <param name="data">Datos de la respuesta</param>
    public BianGenericResponseModel(T data)
    {
        Data = data;
        errors = null;
    }

    /// <summary>
    /// Constructor para respuesta con errores
    /// </summary>
    /// <param name="errors">Lista de errores</param>
    public BianGenericResponseModel(List<BianErrorInternalModel> errors)
    {
        Data = default(T);
        this.errors = errors;
    }

    /// <summary>
    /// Constructor vacío para serialización
    /// </summary>
    public BianGenericResponseModel()
    {
    }
}