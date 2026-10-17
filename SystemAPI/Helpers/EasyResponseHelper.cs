using System.Collections.Concurrent;
using System.Reflection;
using Application.Internals.Adapters;
using SystemAPI.Models.Internals;

/* ********************************************************************************************************          
# * Copyright � 2026 Arify Labs - Todos los derechos reservados.
# * 
# * Descripción                  : Utilidades para construir respuestas.
# *
# * Autor                    : Victor Jhampier Caxi Maquera
# * Correo/Móvil/Teléfono    : victorjhampier@gmail.com | 968991714
# *
# * Fecha de creación         : 01/01/2026
# * 
# **********************************************************************************************************/

namespace SystemAPI.Helpers;

internal static class EasyResponseHelper
{
    // Cache para propiedades de tipos para evitar reflexión repetida
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _propertyCache = new();

    public static ResponseInternalModel ErrorResponse(string errorCode, string message = "Error general interno")
    {
        return new ResponseInternalModel
        {
            Errors = [new FieldErrorInternalModel
            {
                StatusCode = errorCode,
                Message = message
            }]
        };
    }

    public static ResponseInternalModel WarningResponse(IReadOnlyCollection<ValidationResultAdapter> errorList)
    {
        // Pre-allocar la capacidad de la lista si conocemos el tamaño
        var errors = new List<FieldErrorInternalModel>(errorList.Count);
        
        foreach (var error in errorList)
        {
            errors.Add(new FieldErrorInternalModel
            {
                StatusCode = error.Code,
                Message = error.Message,
                Field = error.Field
            });
        }

        return new ResponseInternalModel
        {
            Errors = errors
        };
    }

    public static ResponseInternalModel SuccessResponse<T>(T dataResponse)
    {
        return new ResponseInternalModel
        {
            Response = dataResponse
        };
    }

    public static TResponse SuccessResponse<TResponse>(object dataResponse) where TResponse : ResponseInternalModel, new()
    {
        var result = new TResponse();

        // Usar cache para evitar reflexión repetida
        var property = _propertyCache.GetOrAdd(typeof(TResponse), type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(prop => 
                    !string.Equals(prop.Name, "statusCode", StringComparison.OrdinalIgnoreCase) && 
                    !string.Equals(prop.Name, "errors", StringComparison.OrdinalIgnoreCase)));

        property?.SetValue(result, dataResponse);

        return result;
    }
}
