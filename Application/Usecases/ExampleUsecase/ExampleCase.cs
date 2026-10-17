using Application.Adapters;
using Application.Internals.Adapters;
using Application.Internals.Executors;
using Application.Ports;
using Domain.Interfaces;

namespace Application.Usecases.ExampleUsecase;

public class ExampleCase : IExamplePort
{
    private readonly IExampleTitleQuery _fakeApiQuery;
    public ExampleCase(IExampleTitleQuery fakeApiQuery)
    {
        _fakeApiQuery = fakeApiQuery;
    }

    async public Task<EasyResult<RetrieveExampleAdapter>> ShowExampleAsync(TraceIdentifierAdapter header, CancellationToken ct = default)
    {
        var arrValided = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (arrValided.Any())
        {            
            return EasyResult<RetrieveExampleAdapter>.Failure(422, arrValided);
        }

        var random = new Random();
        var taskApiResult = _fakeApiQuery.GetAsync(random.Next(1, 14), ct);
        var taskApiTwoResult = _fakeApiQuery.GetProductAsync(random.Next(1, 14), ct);

        await Task.WhenAll(taskApiResult, taskApiTwoResult);
        var apiResult = await taskApiResult;
        var apiTwoResult = await taskApiTwoResult;

        var result = new RetrieveExampleAdapter
        {
            Ping = apiTwoResult.Title,
            Pong = apiResult.Title
        };

        return EasyResult<RetrieveExampleAdapter>.Success(result);
    }

    async public Task<EasyResult<ExecuteExampleTwoAdapter>> ExecuteExampleTwoAsync(TraceIdentifierAdapter header, CancellationToken ct = default)
    {
        var arrValided = FluentValidationExecutor.Execute(header, new TraceIdentifierAdapterValidator());
        if (arrValided.Any())
        {            
            return EasyResult<ExecuteExampleTwoAdapter>.Failure(403, arrValided);
        }

        var result = new ExecuteExampleTwoAdapter
        {
            Ping = "pong",
            Pong = "ping"
        };

        return EasyResult<ExecuteExampleTwoAdapter>.Success(result);
    }
}
