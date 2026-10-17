using Application.Internals.Adapters;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SystemAPI.Models.Internals;

/* ********************************************************************************************************          
# * Copyright � 2026 Arify Labs - Todos los derechos reservados.
# * 
# * Descripción                  : Utilidades para construir respuestas BIAN.
# *
# * Autor                    : Victor Jhampier Caxi Maquera
# * Correo/Móvil/Teléfono    : victorjhampier@gmail.com | 968991714
# *
# * Fecha de creación         : 01/01/2026
# * 
# **********************************************************************************************************/

namespace SystemAPI.Helpers;

public static class EasyBianResponseHelper
{
    // Cache para nombres de campos de respuesta para evitar recalcular
    private static readonly ConcurrentDictionary<Type, string> _responseFieldNameCache = new();
    
    // Constantes para evitar allocaciones repetidas
    private const string ErrorsKey = "errors";
    private const string DefaultErrorCode = "1099";
    private const string DefaultErrorMessage = "No es un problema de tu lado. Estamos experimentando dificultades técnicas";
    private const string GeneralField = "General";
    private const string InSeparator = " in ";
    private const string AdapterSuffix = "Adapter";
    private const string HelperSuffix = "Helper";
    private const string ResponseSuffix = "Response";
    
    // Reutilizar array para errores simples
    private static readonly BianErrorInternalModel[] _singleErrorArray = new BianErrorInternalModel[1];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, object?> SuccessResponse(object data)
    {
        var fieldName = GetResponseFieldNameFromObject(data);
        return new Dictionary<string, object?>(capacity: 1)
        {
            [fieldName] = data
        };
    }

    public static Dictionary<string, object?> WarningResponse(IReadOnlyCollection<ValidationResultAdapter> validationErrors)
    {
        var errorCount = validationErrors.Count;
        var errors = new BianErrorInternalModel[errorCount];
        var index = 0;
        
        foreach (var error in validationErrors)
        {
            var fieldName = string.IsNullOrEmpty(error.Field) ? GeneralField : error.Field;
            errors[index++] = new BianErrorInternalModel
            {
                Status_code = error.Code,
                Message = string.IsNullOrEmpty(error.Field) ? error.Message : string.Concat(error.Message, InSeparator, fieldName)
            };
        }

        return new Dictionary<string, object?>(capacity: 1)
        {
            [ErrorsKey] = errors
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, object?> ErrorResponse(string errorCode = DefaultErrorCode, string message = DefaultErrorMessage)
    {
        // Reutilizar array estático para errores únicos
        _singleErrorArray[0] = new BianErrorInternalModel
        {
            Status_code = errorCode,
            Message = message
        };

        return new Dictionary<string, object?>(capacity: 1)
        {
            [ErrorsKey] = _singleErrorArray
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, object?> EasySuccessRespond<T>(T data)
    {
        return SuccessResponse(data!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, object?> EasyValidationErrorRespond<T>(IReadOnlyCollection<ValidationResultAdapter> validationErrors)
    {
        return WarningResponse(validationErrors);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, object?> EasyErrorRespond<T>(string errorCode = DefaultErrorCode, string message = DefaultErrorMessage)
    {
        return ErrorResponse(errorCode, message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetCachedResponseFieldName<T>()
    {
        return _responseFieldNameCache.GetOrAdd(typeof(T), GenerateResponseFieldName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetResponseFieldNameFromObject(object data)
    {
        if (data == null) return "data";
        
        return _responseFieldNameCache.GetOrAdd(data.GetType(), GenerateResponseFieldName);
    }

    private static string GenerateResponseFieldName(Type type)
    {
        var typeName = type.Name;
        
        // Obtener el nombre base removiendo sufijos conocidos
        var baseName = GetBaseName(typeName);
        
        // Siempre terminar en "Response"
        return string.Concat(baseName, ResponseSuffix);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetBaseName(string typeName)
    {
        // Si ya termina en "Response", verificar si tiene otro sufijo antes
        if (typeName.EndsWith(ResponseSuffix))
        {
            var nameWithoutResponse = typeName.Substring(0, typeName.Length - ResponseSuffix.Length);
            
            // Si después de quitar "Response" termina en "Adapter", quitarlo también
            if (nameWithoutResponse.EndsWith(AdapterSuffix))
            {
                return nameWithoutResponse.Substring(0, nameWithoutResponse.Length - AdapterSuffix.Length);
            }
            
            // Si solo tenía "Response", devolver el nombre sin "Response"
            return nameWithoutResponse;
        }
        
        // Si termina en "Adapter", quitarlo
        if (typeName.EndsWith(AdapterSuffix))
        {
            return typeName.Substring(0, typeName.Length - AdapterSuffix.Length);
        }
        
        // Si termina en "Helper", quitarlo
        if (typeName.EndsWith(HelperSuffix))
        {
            return typeName.Substring(0, typeName.Length - HelperSuffix.Length);
        }
        
        // Si no tiene sufijos conocidos, devolver el nombre completo
        return typeName;
    }
}