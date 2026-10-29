using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class ProductPullTests
{
    private sealed class Gate : IDeviceGate
    {
        public Result Answer { get; set; } = Result.Ok();
        public Task<Result> Approved(Guid shopId, Guid deviceId, CancellationToken ct) => Task.FromResult(Answer);
    }

    private sealed class Store(List<PulledProduct> all) : IProductPullStore
    {
        public Task<List<PulledProduct>> ChangedSince(Guid shopId, DateTimeOffset? since, int take, CancellationToken ct) =>
            Task.FromResult(all.Where(p => since is null || p.UpdatedAt > since).OrderBy(p => p.UpdatedAt).Take(take).ToList());
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static PulledProduct Product(string name, int minutesAgo, bool active = true) =>
        new(Guid.NewGuid(), name, null, "food", 100, 200, 0, null, active, false, false, false, null, null, Now.AddMinutes(-minutesAgo));

    [Fact]
    public async Task A_till_gets_every_product_the_first_time_and_only_what_changed_after_that_including_deactivated_ones()
    {
        var all = new List<PulledProduct> { Product("Bread", 60), Product("Milk", 30), Product("Old tins", 5, active: false) };
        var handler = new PullProductsHandler(new Gate(), new Store(all), new FakeTimeProvider(Now));

        var first = (await handler.Handle(new PullProducts(Guid.NewGuid(), Guid.NewGuid(), null), default)).Value!;
        var next = (await handler.Handle(new PullProducts(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-40)), default)).Value!;

        Assert.Equal(3, first.Products.Count);
        Assert.Equal(Now, first.AsOf);
        Assert.Equal(["Milk", "Old tins"], next.Products.Select(p => p.Name));
        Assert.False(next.Products.Last().IsActive);   // the till hides it
    }

    [Fact]
    public async Task A_device_that_is_not_approved_gets_nothing()
    {
        var gate = new Gate { Answer = Result.Fail("device.not_approved", "No.") };
        var handler = new PullProductsHandler(gate, new Store([Product("Bread", 1)]), new FakeTimeProvider(Now));

        var result = await handler.Handle(new PullProducts(Guid.NewGuid(), Guid.NewGuid(), null), default);

        Assert.Equal("device.not_approved", result.Error!.Code);
    }
}
