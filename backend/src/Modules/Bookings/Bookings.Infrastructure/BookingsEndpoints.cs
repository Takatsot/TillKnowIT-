using Bookings.Application;
using Bookings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Bookings.Infrastructure;

public static class BookingsEndpoints
{
    // TODO(auth): require a signed-in caller once sign-in exists.
    public static IEndpointRouteBuilder MapBookingsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapPost("/shops/{shopId:guid}/services", async (Guid shopId, SaveServiceRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SaveService(shopId, null, req), ct)));
        api.MapPut("/shops/{shopId:guid}/services/{id:guid}", async (Guid shopId, Guid id, SaveServiceRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SaveService(shopId, id, req), ct)));
        api.MapGet("/shops/{shopId:guid}/services", async (Guid shopId, Guid deviceId, Guid staffId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListServices(shopId, deviceId, staffId), ct)));

        api.MapPost("/shops/{shopId:guid}/appointments", async (Guid shopId, BookAppointmentRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new BookAppointment(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/appointments", async (Guid shopId, Guid deviceId, Guid staffId, DateTimeOffset from, DateTimeOffset to, Guid? providerStaffId, IDispatcher d, CancellationToken ct) =>
            Reply(await d.Ask(new ListAppointments(shopId, deviceId, staffId, from, to, providerStaffId), ct)));
        api.MapPatch("/shops/{shopId:guid}/appointments/{id:guid}", async (Guid shopId, Guid id, RescheduleRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new RescheduleAppointment(shopId, id, req), ct)));
        api.MapPost("/shops/{shopId:guid}/appointments/{id:guid}/action", async (Guid shopId, Guid id, AppointmentActionRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new ChangeAppointment(shopId, id, req), ct)));

        api.MapPut("/shops/{shopId:guid}/commission-rules", async (Guid shopId, SaveCommissionRuleRequest req, IDispatcher d, CancellationToken ct) => Reply(await d.Send(new SaveCommissionRule(shopId, req), ct)));
        api.MapGet("/shops/{shopId:guid}/commission-rules", async (Guid shopId, Guid callerPersonId, IDispatcher d, CancellationToken ct) => Reply(await d.Ask(new ListCommissionRules(shopId, callerPersonId), ct)));
        api.MapGet("/shops/{shopId:guid}/reports/commission", async (Guid shopId, Guid callerPersonId, DateOnly? from, DateOnly? to, string? format, IDispatcher d, CancellationToken ct) =>
        {
            var result = await d.Ask(new GetCommissionReport(shopId, callerPersonId, from, to, format), ct);
            return result.IsSuccess ? Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error!);
        });
        return api;
    }

    private static IResult Reply<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    private static IResult Problem(Error error) => Results.Problem(title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code },
        statusCode: error.Code switch
        {
            "permission.denied" or "device.unknown" or "device.not_approved" or "staff.unknown" or "staff.disabled" or "feature.not_enabled" => 403,
            var c when c.EndsWith("not_found", StringComparison.Ordinal) => 404,
            "appointment.conflict" or "appointment.not_open" or "appointment.not_booked" => 409,
            _ => 422,
        });
}
