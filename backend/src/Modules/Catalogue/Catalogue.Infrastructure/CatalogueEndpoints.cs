using Catalogue.Application.Products;
using Catalogue.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Catalogue.Infrastructure;

public static class CatalogueEndpoints
{
    // TODO(auth): require a signed-in shop member once sign-in exists.
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/catalogue/products", async (string barcode, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new FindProductByBarcode(barcode), ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Problem(result);
        });

        api.MapPost("/catalogue/suggestions", async (SuggestProductRequest req, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Send(new SuggestProduct(req.Barcode, req.Name, req.Size, req.CategoryCode), ct);
            return result.IsSuccess
                ? (result.Value!.AlreadyKnown ? Results.Ok(result.Value) : Results.Created($"/api/v1/catalogue/products?barcode={req.Barcode}", result.Value))
                : Problem(result);
        });

        return api;
    }

    private static IResult Problem(Result result)
    {
        var error = result.Error!;
        var status = error.Code == "catalogue.not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status422UnprocessableEntity;
        return Results.Problem(title: error.Message, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
